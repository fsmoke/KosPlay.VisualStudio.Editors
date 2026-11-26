param([ValidateSet('Debug','Release')][string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
$vswhere = Join-Path (${env:ProgramFiles(x86)}) 'Microsoft Visual Studio\Installer\vswhere.exe'
if (-not (Test-Path -LiteralPath $vswhere)) { throw 'Visual Studio Installer (vswhere.exe) is required.' }
$msbuild = & $vswhere -latest -products '*' -version '[18.0,19.0)' -requires Microsoft.Component.MSBuild -find 'MSBuild\Current\Bin\MSBuild.exe'
if (-not $msbuild) { throw 'Visual Studio 2026 MSBuild was not found.' }
& $msbuild (Join-Path $PSScriptRoot 'Installer\KosPlay.VisualStudio.Editors.Setup.wixproj') /restore /t:Build "/p:Configuration=$Configuration" /nologo /verbosity:minimal
if ($LASTEXITCODE -ne 0) { throw "MSI build failed: $LASTEXITCODE" }
Write-Host "MSI: Installer\bin\$Configuration\KosPlay.VisualStudio.Editors.VS18.msi"