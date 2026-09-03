<#
.SYNOPSIS
    Fetches the Berean Standard Bible: the published text, and the translation tables that say
    which of its words renders which original word.

.DESCRIPTION
    The corpus had one whole-Bible stated word mapping — the King James against the Hebrew — and
    nothing of the kind for the New Testament. This is a second, independent one, and the only
    calibration set the New Testament has. FTR-0182 has the measurements.

    Public domain since 30 April 2023 by the project's own licensing page, which is quoted in the
    LICENCE.md this writes beside the data. Attributed anyway, per RUL-0181.

    85 MB for the tables, so they are fetched rather than committed. Without them the corpus loads
    and the Berean simply reaches no Greek word, which the log says.

.EXAMPLE
    ./scripts/fetch-berean.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$root = Join-Path (Resolve-Path $ResourcesPath) 'Berean'
New-Item -ItemType Directory -Force -Path $root | Out-Null

foreach ($file in 'bsb.txt', 'bsb_tables.tsv') {
    $target = Join-Path $root $file
    Write-Host "Fetching $file"
    Invoke-WebRequest -Uri "https://bereanbible.com/$file" -OutFile $target
}

# The licence is the reason this data can be here at all, so it is checked rather than assumed. The
# published text carries the dedication in its own second line.
$dedication = (Get-Content (Join-Path $root 'bsb.txt') -TotalCount 2)[1]
if ($dedication -notmatch 'public domain') {
    throw "bsb.txt no longer says its text is dedicated to the public domain. That is a decision " +
          "for the owner, not something a download script should carry on through; stop and read it."
}

$verses = (Get-Content (Join-Path $root 'bsb.txt') | Measure-Object -Line).Lines
$rows = (Get-Content (Join-Path $root 'bsb_tables.tsv') | Measure-Object -Line).Lines
Write-Host ("{0:N0} lines of text, {1:N0} table rows, in {2}" -f $verses, $rows, $root)
