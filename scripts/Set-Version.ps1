<#
.SYNOPSIS
  Sets the plugin version for a release, or checks that a release tag matches it.

.DESCRIPTION
  The version in src/GamoireLaunchBox/GamoireLaunchBox.csproj is the one source. -Version writes it there.
  -Check verifies the csproj holds a valid X.Y.Z version; -Check v1.2.3 also requires the tag to equal it (the release
  workflow's guard).

.EXAMPLE
  ./scripts/Set-Version.ps1 -Version 1.0.1

.EXAMPLE
  ./scripts/Set-Version.ps1 -Check
  ./scripts/Set-Version.ps1 -Check v1.0.1
#>
[CmdletBinding(DefaultParameterSetName = 'Bump')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Bump')]
    [string] $Version,

    [Parameter(Mandatory, ParameterSetName = 'Check')]
    [switch] $Check,

    [Parameter(ParameterSetName = 'Check', Position = 0)]
    [string] $Tag
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$project = Join-Path $root 'src/GamoireLaunchBox/GamoireLaunchBox.csproj'

function Assert-Version([string] $value) {
    if ($value -notmatch '^\d+\.\d+\.\d+$') { throw "Version must look like 1.2.3, got '$value'." }
}

function Get-ProjectVersion {
    $match = Select-String -Path $project -Pattern '<Version>([^<]+)</Version>'
    if (-not $match) { throw "<Version> missing in $project." }
    $match.Matches[0].Groups[1].Value.Trim()
}

if ($PSCmdlet.ParameterSetName -eq 'Check') {
    $value = Get-ProjectVersion
    Assert-Version $value
    if ($Tag) {
        $tagVersion = $Tag.Trim().TrimStart('v')
        Assert-Version $tagVersion
        if ($tagVersion -ne $value) {
            throw "Version mismatch: the tag is v$tagVersion but the csproj <Version> is $value. To fix: tag v$value, or bump to $tagVersion with Set-Version.ps1 -Version $tagVersion."
        }
    }
    Write-Output "$value is the csproj version."
    return
}

Assert-Version $Version
$text = [System.IO.File]::ReadAllText($project) -replace '<Version>[^<]+</Version>', "<Version>$Version</Version>"
[System.IO.File]::WriteAllText($project, $text, (New-Object System.Text.UTF8Encoding($false)))
Write-Output "Version $Version set in the csproj. Commit, then merge into main to release."
