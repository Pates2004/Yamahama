. (Join-Path $PSScriptRoot 'project_materials.ps1')

function Get-SnapshotPath {
    param([string]$ProjectRoot, [string]$RelativePath)

    $root = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
    if ([IO.Path]::IsPathRooted($RelativePath)) {
        throw "A project-relative path is required: $RelativePath"
    }
    $candidate = Join-ProjectMaterialPath $root $RelativePath
    $ancestor = $candidate
    while ($ancestor) {
        if (Test-Path -LiteralPath $ancestor) {
            $item = Get-Item -LiteralPath $ancestor -Force -ErrorAction Stop
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse points are not permitted in preservation paths: $ancestor"
            }
        }
        $ancestor = [IO.Path]::GetDirectoryName($ancestor)
    }
    return $candidate
}

function Get-SnapshotItems {
    param([string]$ProjectRoot, [string[]]$RelativePaths)

    foreach ($relativePath in $RelativePaths) {
        $path = Get-SnapshotPath -ProjectRoot $ProjectRoot -RelativePath $relativePath
        if (-not (Test-Path -LiteralPath $path)) { continue }
        $item = Get-Item -LiteralPath $path -Force -ErrorAction Stop
        $item
        if ($item.PSIsContainer) {
            $children = @(Get-ChildItem -LiteralPath $path -Force -ErrorAction Stop)
            foreach ($child in $children) {
                $childRelativePath = $relativePath.TrimEnd('\', '/') + '\' + $child.Name
                Get-SnapshotItems -ProjectRoot $ProjectRoot -RelativePaths $childRelativePath
            }
        }
    }
}

function Assert-SnapshotProcessesIdle {
    param([string]$ProjectRoot, [string[]]$Paths)

    $processes = @(Get-CimInstance Win32_Process -ErrorAction Stop)
    $processById = @{}
    foreach ($process in $processes) { $processById[[int]$process.ProcessId] = $process }
    $ancestors = [Collections.Generic.HashSet[int]]::new()
    $ancestorId = $PID
    while ($processById.ContainsKey($ancestorId) -and $ancestors.Add($ancestorId)) {
        $ancestorId = [int]$processById[$ancestorId].ParentProcessId
    }
    $rootPattern = [regex]::Escape($ProjectRoot.TrimEnd('\', '/').Replace('/', '\')) + '(?=$|[\s"''\\])'
    foreach ($process in $processes) {
        if ($process.ProcessId -eq $PID) { continue }
        $executable = ([string]$process.ExecutablePath).Replace('/', '\')
        $commandLine = ([string]$process.CommandLine).Replace('/', '\')
        foreach ($path in $Paths) {
            if ($executable.Equals($path, [StringComparison]::OrdinalIgnoreCase) -or
                $executable.StartsWith($path.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw "A process is using a preservation path: $($process.Name) (PID $($process.ProcessId)), $path"
            }
        }
        if ($ancestors.Contains([int]$process.ProcessId)) { continue }
        foreach ($path in $Paths) {
            $pathPattern = [regex]::Escape($path.TrimEnd('\').Replace('/', '\')) + '(?=$|[\s"''\\])'
            if ($commandLine -match $pathPattern) {
                throw "A process references a preservation path: $($process.Name) (PID $($process.ProcessId)), $path"
            }
        }
        $isBuildProcess = $process.Name -match '^(dotnet|MSBuild|testhost|vstest\.console)(\.exe)?$'
        $isBuildCommand = $commandLine -match '(?i)build\.ps1|dotnet\s+(build|publish|test)'
        if ($commandLine -match $rootPattern -and ($isBuildProcess -or $isBuildCommand)) {
            throw "Another project build command is active (PID $($process.ProcessId))."
        }
    }
}

function Get-SnapshotFileRecord {
    param([string]$ProjectRoot, [string]$RelativePath)

    $path = Get-SnapshotPath -ProjectRoot $ProjectRoot -RelativePath $RelativePath
    $file = Get-Item -LiteralPath $path -Force -ErrorAction Stop
    if ($file.Name -ieq 'config.json') {
        throw "User configuration must never be archived: $path"
    }
    $version = if ($file.Extension -in @('.exe', '.dll')) { $file.VersionInfo.ProductVersion } else { $null }
    $stream = [IO.File]::Open($path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::None)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $contentCheck = 'Exclusive read and SHA256 of all bytes'
        if ($file.Extension -in @('.exe', '.dll')) {
            if ($stream.ReadByte() -ne 0x4D -or $stream.ReadByte() -ne 0x5A) {
                throw "Invalid executable header: $path"
            }
            $contentCheck += '; MZ header'
        }
        if ($file.Extension -in @('.json', '.lng')) {
            $reader = [IO.StreamReader]::new($stream, [Text.Encoding]::UTF8, $true, 1024, $true)
            try {
                $text = $reader.ReadToEnd()
                if ([string]::IsNullOrWhiteSpace($text)) { throw "Empty JSON payload: $path" }
                $null = ConvertFrom-Json -InputObject $text -ErrorAction Stop
            }
            finally { $reader.Dispose() }
            $contentCheck += '; parsed JSON'
        }
        $stream.Position = 0
        $hash = [BitConverter]::ToString($algorithm.ComputeHash($stream)).Replace('-', '')
        [pscustomobject]@{
            RelativePath = $RelativePath
            Bytes = $stream.Length
            BeforeSHA256 = $hash
            AfterSHA256 = $null
            ProductVersion = $version
            ContentCheck = $contentCheck
        }
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Move-YamahaBuildSnapshot {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string[]]$RelativePaths,
        [Parameter(Mandatory = $true)][string]$Label,
        [Parameter(Mandatory = $true)][string]$Provenance
    )

    $ProjectRoot = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
    if ($Label -notmatch '^[a-zA-Z0-9][a-zA-Z0-9._-]*$') { throw 'Unsafe snapshot label.' }
    $snapshotBase = Get-SnapshotPath -ProjectRoot $ProjectRoot -RelativePath 'snapshots'
    $sources = @()
    foreach ($relativePath in $RelativePaths) {
        $path = Get-SnapshotPath -ProjectRoot $ProjectRoot -RelativePath $relativePath
        if ($path -eq $snapshotBase -or $path.StartsWith($snapshotBase + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Existing snapshots must not be moved or overwritten.'
        }
        foreach ($source in $sources) {
            if ($path -eq $source -or $path.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase) -or
                $source.StartsWith($path + '\', [StringComparison]::OrdinalIgnoreCase)) {
                throw 'Snapshot selections must not overlap.'
            }
        }
        $sources += $path
    }
    $items = @(Get-SnapshotItems -ProjectRoot $ProjectRoot -RelativePaths $RelativePaths)
    $files = @($items | Where-Object { -not $_.PSIsContainer } | Sort-Object FullName)
    if ($files.Count -eq 0) { return }
    Assert-SnapshotProcessesIdle -ProjectRoot $ProjectRoot -Paths $sources
    $records = @($files | ForEach-Object {
        Get-SnapshotFileRecord -ProjectRoot $ProjectRoot -RelativePath (Get-ProjectMaterialRelativePath $ProjectRoot $_.FullName)
    })
    $snapshotRelativePath = 'snapshots\' + $Label + '-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '-' + [Guid]::NewGuid().ToString('N')
    $snapshotRoot = Get-SnapshotPath -ProjectRoot $ProjectRoot -RelativePath $snapshotRelativePath
    if (Test-Path -LiteralPath $snapshotRoot) { throw "Snapshot already exists: $snapshotRoot" }
    foreach ($source in $sources) {
        $relativePath = Get-ProjectMaterialRelativePath $ProjectRoot $source
        $target = Get-SnapshotPath -ProjectRoot $ProjectRoot -RelativePath ($snapshotRelativePath + '\' + $relativePath)
        if (Test-Path -LiteralPath $target) { throw "Snapshot target already exists: $target" }
    }
    $currentItems = @(Get-SnapshotItems -ProjectRoot $ProjectRoot -RelativePaths $RelativePaths)
    $changes = @(Compare-Object -ReferenceObject @($items.FullName | Sort-Object) -DifferenceObject @($currentItems.FullName | Sort-Object))
    if ($changes.Count -ne 0) { throw 'The artifact inventory changed during preflight.' }
    foreach ($record in $records) {
        $current = Get-SnapshotFileRecord -ProjectRoot $ProjectRoot -RelativePath $record.RelativePath
        if ($current.BeforeSHA256 -ne $record.BeforeSHA256 -or $current.Bytes -ne $record.Bytes) {
            throw "Artifact changed during preflight: $($record.RelativePath)"
        }
    }
    Assert-SnapshotProcessesIdle -ProjectRoot $ProjectRoot -Paths $sources
    $null = New-Item -ItemType Directory -Path $snapshotRoot -ErrorAction Stop
    $manifest = [ordered]@{
        State = 'Prepared'
        CreatedUtc = [DateTime]::UtcNow.ToString('o')
        ProjectRoot = $ProjectRoot
        Provenance = $Provenance
        Preservation = 'Move only; retain all bytes, relative paths and dependencies; verify SHA256 before and after.'
        SelectedPaths = $RelativePaths
        MovedPaths = @()
        Directories = @($items | Where-Object { $_.PSIsContainer } | ForEach-Object { Get-ProjectMaterialRelativePath $ProjectRoot $_.FullName })
        Files = $records
    }
    $manifestPath = Join-Path $snapshotRoot 'manifest.json'
    $encoding = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $encoding)
    try {
        foreach ($source in $sources) {
            if (-not (Test-Path -LiteralPath $source)) { continue }
            $relativePath = Get-ProjectMaterialRelativePath $ProjectRoot $source
            $null = Get-SnapshotPath -ProjectRoot $ProjectRoot -RelativePath $relativePath
            $target = Get-SnapshotPath -ProjectRoot $ProjectRoot -RelativePath ($snapshotRelativePath + '\' + $relativePath)
            if (Test-Path -LiteralPath $target) { throw "Snapshot target already exists: $target" }
            $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
            Move-Item -LiteralPath $source -Destination $target -ErrorAction Stop
            $manifest.MovedPaths += $relativePath
        }
        foreach ($record in $records) {
            $preserved = Get-SnapshotFileRecord -ProjectRoot $ProjectRoot -RelativePath ($snapshotRelativePath + '\' + $record.RelativePath)
            $record.AfterSHA256 = $preserved.BeforeSHA256
            if ($record.BeforeSHA256 -ne $record.AfterSHA256 -or $record.Bytes -ne $preserved.Bytes) {
                throw "Snapshot integrity check failed: $($record.RelativePath)"
            }
        }
        foreach ($directory in $manifest.Directories) {
            $target = Get-SnapshotPath -ProjectRoot $ProjectRoot -RelativePath ($snapshotRelativePath + '\' + $directory)
            if (-not (Test-Path -LiteralPath $target -PathType Container)) { throw "Missing snapshot directory: $directory" }
        }
        foreach ($source in $sources) {
            if (Test-Path -LiteralPath $source) { throw "A selected source remains after preservation: $source" }
        }
        $manifest.State = 'Verified'
    }
    catch {
        $manifest.State = 'Incomplete; publishing prohibited'
        throw "Preservation failed. Keep all remaining sources and inspect $snapshotRoot. $($_.Exception.Message)"
    }
    finally {
        [IO.File]::WriteAllText($manifestPath, ($manifest | ConvertTo-Json -Depth 6), $encoding)
    }
    return $snapshotRoot
}

function Enter-YamahaBuildLock {
    param([string]$ProjectRoot)

    $algorithm = [Security.Cryptography.SHA256]::Create()
    try {
        $identity = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/').ToUpperInvariant()
        $hash = [BitConverter]::ToString($algorithm.ComputeHash([Text.Encoding]::UTF8.GetBytes($identity))).Replace('-', '')
    }
    finally { $algorithm.Dispose() }
    $mutex = [Threading.Mutex]::new($false, 'Local\Yamahama.Build.' + $hash)
    try {
        try { $acquired = $mutex.WaitOne(0) }
        catch [Threading.AbandonedMutexException] { $acquired = $true }
        if (-not $acquired) { throw 'Another build.ps1 invocation is already active for this project.' }
        return $mutex
    }
    catch {
        $mutex.Dispose()
        throw
    }
}
