<#
.SYNOPSIS
    Fetches the Text-Fabric dataset of the Samaritan Pentateuch — the Samaritan recension of the
    Torah, transcribed for Stefan Schorch's critical editio maior.

.DESCRIPTION
    The first witness in the corpus that disagrees with the Masoretic text in Hebrew rather than in
    translation: 1,533 of its verses have a different number of words from the Leningrad column,
    which is what a link with one empty side was built to hold.

    The same file format BHSA arrives in, so the Text-Fabric reader already here parses it. All 41
    feature files are taken rather than the dozen the loader reads: a copy of a dataset is the
    dataset, and the manifest fingerprints what is on disk.

    The licence is checked, not assumed, and it is checked in three places because the repository
    states it in three and they do not all agree. Every feature file that carries a licence line
    must still say CC BY-NC 4.0, the README badge must still say it, and the prose granting research
    and educational use must still stand. Zenodo says CC BY 4.0 for the same dataset and there is no
    LICENSE file at all; the statement inside the bytes is the one taken, and it is the stricter.
    If any of the three changed, this stops and replaces nothing.

.EXAMPLE
    ./scripts/fetch-samaritan.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # A branch, tag or commit of DT-UCPH/sp.
    [string] $Ref = 'main',

    # The dataset version under tf/. 7.1.3 is the first with phrase types over the whole text.
    [string] $Version = '7.1.3'
)

$ErrorActionPreference = 'Stop'

$Repository = 'DT-UCPH/sp'
$ExpectedLicence = 'Creative Commons Attribution-NonCommercial 4.0 International License'
$ExpectedBadge = 'License-CC_BY--NC_4.0'
$ExpectedGrant = 'You can use the dataset freely for research and education'

# What otype.tf must account for, from the node ranges. A short download is a partial one, and a
# dataset that grew a book is not the one these numbers were measured against.
$ExpectedNodes = [ordered]@{
    sign        = 399392
    book        = 5
    chapter     = 187
    verse       = 5841
    word        = 114889
}

$commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Ref" `
    -Headers @{ 'User-Agent' = 'essenthos' }).sha
$raw = "https://raw.githubusercontent.com/$Repository/$commit"

$staging = Join-Path ([IO.Path]::GetTempPath()) "samaritan-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Fetching $Repository at $($commit.Substring(0, 7)), tf/$Version"

    $readme = Invoke-RestMethod -Uri "$raw/README.md" -Headers @{ 'User-Agent' = 'essenthos' }
    if ($readme -notmatch [regex]::Escape($ExpectedBadge)) {
        throw "The README badge no longer says CC BY-NC 4.0. The licence is stated in three places " +
              "here and Zenodo already disagrees with the other two; a second one moving is the " +
              "owner's decision and not a download. Nothing was replaced."
    }

    if ($readme -notmatch [regex]::Escape($ExpectedGrant)) {
        throw "The README no longer grants free use for research and education. That sentence is " +
              "the permission this dataset is held under alongside the CC BY-NC grant; nothing " +
              "was replaced."
    }

    $listing = Invoke-RestMethod -Headers @{ 'User-Agent' = 'essenthos' } `
        -Uri "https://api.github.com/repos/$Repository/contents/tf/$Version`?ref=$commit"
    $features = @($listing | Where-Object { $_.name -like '*.tf' })
    if ($features.Count -lt 40) {
        throw "tf/$Version holds $($features.Count) feature files and the 7.1 dataset has 41. " +
              "Nothing was replaced."
    }

    $stated = 0
    foreach ($feature in $features) {
        $file = Join-Path $staging $feature.name
        Invoke-WebRequest -Uri "$raw/tf/$Version/$($feature.name)" -OutFile $file

        # Not every file carries a licence line — ETCBC_parsing.tf carries none — so the check is
        # that none of them contradicts the grant, and that most of them state it.
        $header = Get-Content $file -TotalCount 20 -Encoding UTF8
        $licence = $header | Where-Object { $_.StartsWith('@licence=') }
        if ($licence) {
            if ($licence -ne "@licence=$ExpectedLicence") {
                throw "$($feature.name) states a licence this fetch was not read under: $licence. " +
                      "Read the new statement before fetching again; nothing was replaced."
            }

            $stated++
        }
    }

    # ETCBC_parsing.tf, gloss.tf and typ.tf carry no licence line. Three silent files out of
    # forty-one is what this was read under; a repository that went quiet would be a different
    # licence question and not a download.
    if ($stated -lt 38) {
        throw "Only $stated of $($features.Count) feature files state a licence at all, against 38 " +
              "when this was written. Nothing was replaced."
    }

    $otype = Join-Path $staging 'otype.tf'
    foreach ($type in $ExpectedNodes.Keys) {
        $line = Select-String -Path $otype -Pattern "^(\d+)-(\d+)\t$type$" | Select-Object -First 1
        if (-not $line) {
            throw "otype.tf declares no range for $type, so this is not the dataset shape the " +
                  "reader expects. Nothing was replaced."
        }

        $count = [int]$line.Matches[0].Groups[2].Value - [int]$line.Matches[0].Groups[1].Value + 1
        if ($count -ne $ExpectedNodes[$type]) {
            throw "otype.tf holds $count $type nodes and this was written against " +
                  "$($ExpectedNodes[$type]). Either the download is partial or the dataset changed; " +
                  "nothing was replaced."
        }
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'SamaritanPentateuch'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Get-ChildItem $root -Filter '*.tf' | Remove-Item -Force
    Copy-Item -Path (Join-Path $staging '*.tf') -Destination $root -Force

    $size = (Get-ChildItem $root -Recurse -File -Filter '*.tf' | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0} feature files, {1:N0} words over {2:N0} verses, {3:N1} MB in {4}" -f `
        $features.Count, $ExpectedNodes.word, $ExpectedNodes.verse, $size, $root)
    Write-Host "Taken from github.com/$Repository at $commit"
    Write-Host "Record that commit in $root\LICENCE.md if this replaced an earlier fetch."
    Write-Host "Then run: python scripts/corpus-manifest.py, and commit the manifest."
    Write-Host ("A reload is not automatic: the corpus loader returns early for a text whose slug " +
                "is already in the text table, so a restart alone picks nothing up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
