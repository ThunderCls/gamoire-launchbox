<#
.SYNOPSIS
  Zips src/GamoireLaunchBox/bin/Release as artifacts/Gamoire-LaunchBox-<version>.zip with a top folder "Gamoire".

.DESCRIPTION
  Fails unless the build output is exactly GamoireLaunchBox.dll and GamoireLaunchBox.deps.json: LaunchBox loads its own
  SDK and System.Drawing, so none of them may ship.
#>
param(
    [Parameter(Mandatory)]
    [string] $Version,

    [string] $OutDir
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts' }
$release = Join-Path $root 'src/GamoireLaunchBox/bin/Release'
$allowed = @('GamoireLaunchBox.deps.json', 'GamoireLaunchBox.dll')

$found = @(Get-ChildItem $release -Recurse -File | ForEach-Object { $_.FullName.Substring($release.Length + 1) } | Sort-Object)
if (($found -join '|') -ne ($allowed -join '|')) { throw "Unexpected build output: $($found -join ', ')." }

$stage = Join-Path $OutDir 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force (Join-Path $stage 'Gamoire') | Out-Null
foreach ($name in $allowed) { Copy-Item (Join-Path $release $name) (Join-Path $stage 'Gamoire') }

$zip = Join-Path $OutDir "Gamoire-LaunchBox-$Version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($name in $allowed) {
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, (Join-Path $stage "Gamoire/$name"), "Gamoire/$name", [System.IO.Compression.CompressionLevel]::Optimal)
    }
} finally {
    $archive.Dispose()
}
Remove-Item $stage -Recurse -Force
Write-Output $zip
