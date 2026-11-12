<#
.SYNOPSIS
    Fetches OpenBible.info's region outlines: the geometry index and the GeoJSON files it names.

.DESCRIPTION
    The outlines a region is drawn by on the map. geometry.jsonl lists every shape the gazetteer
    holds beyond a point, and says whose it is: `format` kml is the gazetteer's own drawing (rough
    boundaries, confidence bands, probability areas, rough paths), CC BY 4.0 with the rest of the
    dataset; `format` osm is OpenStreetMap's, ODbL 1.0, as the readme names it. Both are taken and
    kept apart by the producer that reads them (see Resources/OpenBible/LICENCE.md).

    Only the files geometry.jsonl names are fetched (the g*.geojson files): the per-place
    collections (a*.geojson) and the per-location ones (m*.geojson) repeat those shapes beside
    circles and points the gazetteer's own files already give.

    It fetches the commit ancient.jsonl was taken from, so every geometry id the places name is
    there. The licence is checked as fetch-openbible.ps1 checks it, and the readme must still name
    OpenStreetMap's data as ODbL: if any statement moved, nothing is replaced.

.EXAMPLE
    ./scripts/fetch-openbible-geometry.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # The commit ancient.jsonl and modern.jsonl were taken from (Resources/OpenBible/LICENCE.md).
    [string] $Ref = '7eb18a5ee62f27b9b93bd6689ea272d76dd23b8f'
)

$ErrorActionPreference = 'Stop'

$Repository = 'openbibleinfo/Bible-Geocoding-Data'
$ExpectedLicence = 'Attribution 4.0 International'
$ExpectedReadmeLicence = 'Creative Commons Attribution 4.0'
$ExpectedOpenStreetMap = 'OpenStreetMap data is licensed under [ODbL 1.0]'

$commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Ref" `
    -Headers @{ 'User-Agent' = 'essenthos' }).sha
$raw = "https://raw.githubusercontent.com/$Repository/$commit"

$staging = Join-Path ([IO.Path]::GetTempPath()) "openbible-geometry-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'geometry') | Out-Null

try {
    Write-Host "Fetching $Repository geometry at $($commit.Substring(0, 7))"
    foreach ($file in @('data/geometry.jsonl', 'license.txt', 'readme.md')) {
        Invoke-WebRequest -Uri "$raw/$file" -OutFile (Join-Path $staging (Split-Path $file -Leaf))
    }

    $licence = Get-Content (Join-Path $staging 'license.txt') -Raw
    $readme = Get-Content (Join-Path $staging 'readme.md') -Raw
    if ($licence -notmatch [regex]::Escape($ExpectedLicence) -or
        $readme -notmatch [regex]::Escape($ExpectedReadmeLicence) -or
        $readme -notmatch [regex]::Escape($ExpectedOpenStreetMap)) {
        throw "The licence statements no longer read as Resources/OpenBible/LICENCE.md records them " +
              "(CC BY 4.0, OpenStreetMap's data ODbL 1.0). Nothing was replaced; read them first."
    }

    $names = foreach ($line in Get-Content (Join-Path $staging 'geometry.jsonl')) {
        if (-not $line) { continue }
        $entry = $line | ConvertFrom-Json
        foreach ($key in @('geojson_file', 'simplified_geojson_file', 'isobands_geojson_file')) {
            if ($entry.$key) { $entry.$key }
        }
    }
    $names = $names | Sort-Object -Unique
    if ($names.Count -lt 700) {
        throw "geometry.jsonl names only $($names.Count) files and the release named 745; nothing was replaced."
    }

    $target = Join-Path $staging 'geometry'
    $names | ForEach-Object -ThrottleLimit 16 -Parallel {
        if ($_ -notmatch '^g[0-9a-f]{6}\.[a-z]+\.geojson$') { throw "Unexpected geometry file name '$_'." }
        Invoke-WebRequest -Uri "$using:raw/geometry/$_" -OutFile (Join-Path $using:target $_)
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'OpenBible'
    New-Item -ItemType Directory -Force -Path (Join-Path $root 'geometry') | Out-Null
    Copy-Item -Path (Join-Path $staging 'geometry.jsonl') -Destination (Join-Path $root 'geometry.jsonl') -Force
    Copy-Item -Path (Join-Path $target '*.geojson') -Destination (Join-Path $root 'geometry') -Force

    $size = (Get-ChildItem (Join-Path $root 'geometry') -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} geometry files, {1:N1} MB in {2}" -f $names.Count, $size, (Join-Path $root 'geometry'))
    Write-Host "Taken from github.com/$Repository at $commit"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
