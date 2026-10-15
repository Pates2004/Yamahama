function Get-ProjectWorkRoot {
    if ([IO.Directory]::GetLogicalDrives() -contains 'D:\') { return 'D:\work' }
    return 'C:\work'
}

function Get-ProjectCategoryPath {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot,
          [Parameter(Mandatory = $true)][ValidateSet('Forme', 'Workspace', 'Snapshots', 'Trash')][string]$Category)
    $source = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
    return [IO.Path]::Combine((Get-ProjectWorkRoot), $Category, [IO.Path]::GetFileName($source))
}

function Join-ProjectMaterialPath {
    param([Parameter(Mandatory = $true, Position = 0)][string]$Path,
          [Parameter(Mandatory = $true, Position = 1)][string]$ChildPath)
    if ([IO.Path]::IsPathRooted($ChildPath)) { throw 'A project-relative path is required.' }
    $source = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    $parts = $ChildPath -split '[\\/]', 2
    $category = switch ($parts[0].ToLowerInvariant()) {
        'forme' { 'Forme' }
        'workspace' { 'Workspace' }
        'snapshots' { 'Snapshots' }
        'trash' { 'Trash' }
        default { $null }
    }
    $root = $source
    $relative = $ChildPath
    if ($category) {
        $root = Get-ProjectCategoryPath $source $category
        $relative = if ($parts.Count -eq 2) { $parts[1] } else { '' }
    }
    $result = [IO.Path]::GetFullPath([IO.Path]::Combine($root, $relative)).TrimEnd('\')
    if ($result -ne $root -and -not $result.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escapes the permitted project area: $ChildPath"
    }
    if (-not $category -and $result -eq $source) { throw 'A child path is required.' }
    return $result
}

function Get-ProjectMaterialRelativePath {
    param([Parameter(Mandatory = $true)][string]$ProjectRoot,
          [Parameter(Mandatory = $true)][string]$Path)
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    foreach ($category in @('Forme', 'Workspace', 'Snapshots', 'Trash')) {
        $root = Get-ProjectCategoryPath $ProjectRoot $category
        if ($full.StartsWith($root + '\', [StringComparison]::OrdinalIgnoreCase)) {
            return $category + '\' + $full.Substring($root.Length + 1)
        }
    }
    $source = [IO.Path]::GetFullPath($ProjectRoot).TrimEnd('\', '/')
    if ($full.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
        return $full.Substring($source.Length + 1)
    }
    throw "Path does not belong to this project: $Path"
}

function Test-ProjectMaterialPath {
    param([string]$Path, [string]$ProjectRoot)
    try { $null = Get-ProjectMaterialRelativePath $ProjectRoot $Path; return $true }
    catch { return $false }
}
