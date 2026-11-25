param(
    [ValidateSet('Debug','Release')][string]$Configuration = 'Release',
    [Parameter(Mandatory=$true)][string]$Destination
)
$ErrorActionPreference = 'Stop'
$workspace = Split-Path $PSScriptRoot -Parent
$archive = Join-Path $workspace "VisualStudio.Editors\bin\$Configuration\net472\KosPlay.VisualStudio.Editors.vsix"
if (-not (Test-Path -LiteralPath $archive)) { throw "Build the editor VSIX before staging: $archive" }
# Only clear the build staging tree inside Installer/obj.
$target = [IO.Path]::GetFullPath($Destination).TrimEnd('\')
$allowed = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot 'obj')).TrimEnd('\') + '\'
if (-not $target.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Payload destination must be below Installer/obj: $target"
}
if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
[void][IO.Directory]::CreateDirectory($target)
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::OpenRead($archive)
try {
    foreach ($entry in $zip.Entries) {
        if ($entry.Name.Length -eq 0 -or $entry.FullName -eq '[Content_Types].xml') { continue }
        $path = [IO.Path]::GetFullPath((Join-Path $target $entry.FullName))
        if (-not $path.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase)) {
            throw "Invalid payload path: $($entry.FullName)"
        }
        [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry, $path, $true)
    }
} finally { $zip.Dispose() }
# Keep the standalone VSIX manifest unchanged; only the MSI-owned copy has this flag.
$manifestPath = Join-Path $target 'extension.vsixmanifest'
[xml]$manifest = [IO.File]::ReadAllText($manifestPath)
$installation = $manifest.DocumentElement.ChildNodes | Where-Object LocalName -eq 'Installation'
$installation.SetAttribute('InstalledByMsi', 'true')
$manifest.Save($manifestPath)
Write-Host 'Prepared native MSI extension components.'