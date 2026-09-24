<#
.SYNOPSIS
    Fetches PeriodO — scholarly definitions of the archaeological and historical periods, each under
    the published work it comes from — for the bands the timeline draws behind its events.

.DESCRIPTION
    One file, the whole dataset as JSON-LD: https://data.perio.do/d.json, about 7.8 MB. The loader
    (the Forge `lands` step, also run by `load`) takes from it the periods of the lands around the
    Bible between 4000 BCE and AD 150 and keeps every authority's own dates.

    The licence is checked, not assumed. PeriodO's licence page dedicates the dataset to the public
    domain under CC0 1.0, and the periodo-data repository carries the Unlicense; both are read, both
    must still say so, and a copy of each is kept beside the data. If either stops, this stops: a licence that
    changed under us is a decision for the owner, not a download.

    The download lands in a temporary file and is checked whole before it replaces anything, so an
    interrupted fetch leaves the corpus as it was.

.EXAMPLE
    ./scripts/fetch-periodo.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$root = Join-Path (Resolve-Path $ResourcesPath) 'PeriodO'
New-Item -ItemType Directory -Force -Path $root | Out-Null

$dataset = 'https://data.perio.do/d.json'
$licencePage = 'https://perio.do/license/'
$unlicense = 'https://raw.githubusercontent.com/periodo/periodo-data/master/LICENSE'

# The page wraps its sentences across lines and links the dedication to CC0, so it is read as one
# line of text before anything is matched, and the link is checked on its own.
$html = (Invoke-WebRequest -Uri $licencePage).Content -replace '\s+', ' '
$page = $html -replace '<[^>]+>', ''
$dedication = 'dedicated the PeriodO dataset to the public domain'
if ($page -notmatch [regex]::Escape($dedication) -or $html -notmatch 'creativecommons\.org/publicdomain/zero/1\.0') {
    throw "$licencePage no longer says the contributors have $dedication under CC0. That is a decision for " +
          "the owner, not something a download script should carry on through; stop and read it."
}

$unlicensed = (Invoke-WebRequest -Uri $unlicense).Content
if ($unlicensed -notmatch 'released into the public domain') {
    throw "$unlicense is no longer the Unlicense. Stop and read it before loading anything under it."
}

$statement = [regex]::Match($page, 'To the extent allowed by law[^<]*').Value
$fetched = Get-Date -Format 'yyyy-MM-dd'
Set-Content -Path (Join-Path $root 'licence-at-source.txt') -Encoding utf8 -Value @(
    "Read from $licencePage on ${fetched}, where 'public domain' links to CC0 1.0:",
    '',
    $statement.Trim()
)
Set-Content -Path (Join-Path $root 'periodo-data-LICENSE') -Encoding utf8 -Value $unlicensed

$partial = Join-Path $root 'd.json.partial'
Write-Host "Fetching $dataset"
$response = Invoke-WebRequest -Uri $dataset -OutFile $partial -PassThru

$json = Get-Content -Raw -Path $partial | ConvertFrom-Json
if (-not $json.authorities) {
    Remove-Item $partial
    throw "$dataset came back without its authorities, so it is not the dataset the loader reads. " +
          "Nothing was replaced; try again, or look at what the address serves now."
}

Move-Item -Force -Path $partial -Destination (Join-Path $root 'd.json')

$authorities = @($json.authorities.PSObject.Properties).Count
$bytes = (Get-Item (Join-Path $root 'd.json')).Length
Write-Host ("{0:N0} bytes, {1:N0} authorities, version {2}, in {3}" -f
    $bytes, $authorities, ($response.Headers['ETag'] -join ''), $root)
Write-Host 'Load it with `dotnet run --project Essenthos.Forge -- lands` (the full `load` does it too).'
