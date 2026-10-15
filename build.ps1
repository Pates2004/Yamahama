param([switch]$Diagnostic)

. (Join-Path $PSScriptRoot 'project_materials.ps1')

$ErrorActionPreference = 'Stop'
$projectRoot = $PSScriptRoot
. (Join-ProjectMaterialPath $projectRoot 'build-preservation.ps1')
$project = Join-ProjectMaterialPath $projectRoot 'src\YamahaRemote.Windows\YamahaRemote.Windows.csproj'
$publishRelativePath = if ($Diagnostic) { 'Workspace\validation\publish' } else { 'installfiles' }
$destination = Get-SnapshotPath -ProjectRoot $projectRoot -RelativePath $publishRelativePath
$configurationPath = Get-SnapshotPath -ProjectRoot $projectRoot -RelativePath 'installfiles\config.json'
$null = Get-Command dotnet -ErrorAction Stop
$buildLock = Enter-YamahaBuildLock -ProjectRoot $projectRoot
$configurationLock = $null

try {
    if (Test-Path -LiteralPath $configurationPath) {
        $configurationLock = [IO.File]::Open($configurationPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    }
    $payloadPaths = @(
        "$publishRelativePath\Yamahama.exe"
        "$publishRelativePath\YamahaRemote.exe"
        "$publishRelativePath\langs"
        'Workspace\build\bin\YamahaRemote.Windows'
    )
    $snapshot = Move-YamahaBuildSnapshot -ProjectRoot $projectRoot -RelativePaths $payloadPaths -Label 'Yamahama-before-publish' -Provenance "Existing $publishRelativePath EXE/languages and complete main-application build output, preserved before dotnet publish."
    if ($snapshot) { Write-Output "Previous artifacts preserved: $snapshot" }

    & dotnet publish $project -c Release -r win-x64 --self-contained false -p:PublishSelfContained=false -p:UseAppHost=true -p:PublishSingleFile=true -p:DebugType=embedded -p:DebugSymbols=false -o $destination --nologo
    if ($LASTEXITCODE -ne 0) {
        throw 'Publishing failed. Previous artifacts remain in snapshots; user configuration is unchanged.'
    }
    Write-Output "Published output: $destination"
}
finally {
    if ($configurationLock) { $configurationLock.Dispose() }
    $buildLock.ReleaseMutex()
    $buildLock.Dispose()
}
