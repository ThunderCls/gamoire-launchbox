<#
.SYNOPSIS
  Fails unless every file in lib/ matches its hash in lib/SHA256SUMS.
#>
$ErrorActionPreference = 'Stop'
$lib = Join-Path (Split-Path $PSScriptRoot -Parent) 'lib'
$sums = Join-Path $lib 'SHA256SUMS'
$lines = @([System.IO.File]::ReadAllLines($sums) | Where-Object { $_.Trim() })
if ($lines.Count -eq 0) { throw 'lib/SHA256SUMS is empty.' }
foreach ($line in $lines) {
    if ($line -notmatch '^([0-9a-fA-F]{64})\s+\*?(.+)$') { throw "Unreadable line in SHA256SUMS: $line" }
    $expected = $Matches[1].ToLowerInvariant()
    $file = Join-Path $lib $Matches[2].Trim()
    if (-not (Test-Path $file)) { throw "$($Matches[2]) is listed in SHA256SUMS but missing." }
    $actual = (Get-FileHash $file -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $expected) { throw "Hash mismatch for $($Matches[2]): $actual" }
}
Write-Output "lib/ matches SHA256SUMS ($($lines.Count) file(s))."
