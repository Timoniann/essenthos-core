<#
.SYNOPSIS
    Fetches OpenBible.info's Bible Geocoding data — every identifiable place the Bible names, and
    the verses each one is named in.

.DESCRIPTION
    The place layer's second source. BibleData's own places are marked in progress by its author:
    118 of them, referenced only through Genesis and Exodus, so Jerusalem is named in one verse.
    This dataset states 1,342 places and 8,742 place-verse references across 61 books, records the
    spelling each of ten English translations uses, and says per verse which of them carry the name
    at all.

    Only ancient.jsonl is taken. The repository also holds modern locations, thousands of GeoJSON
    and KML geometry files, and images: the geometry is partly derived from OpenStreetMap and
    carries ODbL 1.0, which is share-alike, so none of it is downloaded here. What is fetched is
    covered by the repository's own Attribution 4.0 and by nothing else.

    The licence is checked, not assumed — both statements attached to the bytes must still say
    Attribution 4.0, and if either stops this stops and says so. The commit is recorded in the
    LICENCE.md kept beside the data.

.EXAMPLE
    ./scripts/fetch-openbible.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # A branch, tag or commit of openbibleinfo/Bible-Geocoding-Data.
    [string] $Ref = 'main'
)

$ErrorActionPreference = 'Stop'

$Repository = 'openbibleinfo/Bible-Geocoding-Data'
$ExpectedLicence = 'Attribution 4.0 International'
$ExpectedReadmeLicence = 'Creative Commons Attribution 4.0'

# The only place file the corpus reads. Everything else in the release is either a different
# question (modern locations, images) or under different terms (the OpenStreetMap geometry).
$Data = 'ancient.jsonl'

$commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Ref" `
    -Headers @{ 'User-Agent' = 'essenthos' }).sha
$raw = "https://raw.githubusercontent.com/$Repository/$commit"

$staging = Join-Path ([IO.Path]::GetTempPath()) "openbible-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Fetching $Repository at $($commit.Substring(0, 7))"

    foreach ($file in @("data/$Data", 'license.txt', 'readme.md')) {
        Invoke-WebRequest -Uri "$raw/$file" -OutFile (Join-Path $staging (Split-Path $file -Leaf))
    }

    $licence = Get-Content (Join-Path $staging 'license.txt') -Raw
    if ($licence -notmatch [regex]::Escape($ExpectedLicence)) {
        throw "license.txt no longer states '$ExpectedLicence'. The corpus attributes this dataset " +
              "as CC BY 4.0 and its share-alike-free terms are why it was chosen over the " +
              "alternative. A licence that changed under us is the owner's decision, not a " +
              "download: nothing was replaced. Read the new statement before fetching again."
    }

    $readme = Get-Content (Join-Path $staging 'readme.md') -Raw
    if ($readme -notmatch [regex]::Escape($ExpectedReadmeLicence)) {
        throw "readme.md no longer states '$ExpectedReadmeLicence' while license.txt still does. " +
              "Two statements about the same bytes that disagree is exactly the case that has to " +
              "be read by a person; nothing was replaced."
    }

    $places = Get-Content (Join-Path $staging $Data) | Measure-Object -Line
    if ($places.Lines -lt 1000) {
        throw "$Data has only $($places.Lines) places and the release measured 1,342. This is a " +
              "partial download; nothing was replaced."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'OpenBible'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    foreach ($name in @($Data, 'license.txt', 'readme.md')) {
        Copy-Item -Path (Join-Path $staging $name) -Destination (Join-Path $root $name) -Force
    }

    $size = (Get-ChildItem $root -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} places, {1:N1} MB in {2}" -f $places.Lines, $size, $root)
    Write-Host "Taken from github.com/$Repository at $commit"
    Write-Host "Record that commit in $root\LICENCE.md if this replaced an earlier fetch."
    Write-Host ("The encyclopedia loader does nothing when the tables already hold rows, so a " +
                "restart alone does not pick this up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
