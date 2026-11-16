<#
.SYNOPSIS
    Fetches MACULA Hebrew's word table, for the one column of it this corpus reads past the
    morphology: which word a Hebrew pronoun, or a pronoun suffix, refers back to.

.DESCRIPTION
    MACULA Greek is published over our own Nestle 1904 word for word, so its `referent` column sits
    on our words by address. MACULA Hebrew is not: it is published over the Westminster Leningrad
    Codex, one row per morph (`בְּ` and `רֵאשִׁית` are two rows of GEN 1:1!1), and the loader joins
    it to BHSA's words by their letters, verse by verse (MaculaHebrewJoin). So this script checks
    the shape and the licence, and the join is what proves the correspondence.

    Only the word table is taken: `WLC/tsv/macula-hebrew.tsv`, 84 MB, kept under git LFS, so it is
    read from media.githubusercontent.com at the pinned commit and not from raw.githubusercontent,
    which would give the three-line LFS pointer.

    The licence is read here rather than trusted. MACULA Hebrew's own layers, participant referents
    among them, are CC BY 4.0 with an attribution string given verbatim. The same file names the
    layers that are not Clear's: the Semantic Dictionary of Biblical Hebrew's domains and senses are
    "used with permission", which is no licence anybody else can rely on, and the loader never reads
    those columns (sdbh, lexdomain, contextualdomain, coredomain). If the licence sentence or the
    attribution string stops saying what it says today, this stops and replaces nothing.

.EXAMPLE
    ./scripts/fetch-macula-hebrew.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # A commit of Clear-Bible/macula-hebrew. The repository publishes no release tags, so the
    # commit read and recorded in Resources/MaculaHebrew/LICENCE.md is the default; moving it is a
    # new reading of the licence and a new measurement of the join.
    [string] $Ref = '47db250bd55d0d8577f2a94fba114ef16c35b23c'
)

$ErrorActionPreference = 'Stop'

$Repository = 'Clear-Bible/macula-hebrew'

$ExpectedLicence = 'is licensed under [CC BY 4.0 ](http://creativecommons.org/licenses/by/4.0/)'
$ExpectedAttribution = 'MACULA Hebrew Linguistic Datasets, available at https://github.com/Clear-Bible/macula-hebrew/'
$ExpectedReferents = 'Participant referents (see `@subjref`, `@participantref`)'

$Table = 'WLC/tsv/macula-hebrew.tsv'

# One row per morph of the Westminster Leningrad Codex as read at the pinned commit. A short
# download would look like a shorter Bible rather than like a failure.
$ExpectedMorphs = 475911

# The columns the loader reads by name. A header that has lost one is a format change.
$Columns = @(
    'xml:id', 'ref', 'class', 'text', 'lang', 'lemma', 'pos', 'person', 'gender', 'number', 'type',
    'participantref'
)

$commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Ref" `
    -Headers @{ 'User-Agent' = 'essenthos' }).sha
$raw = "https://raw.githubusercontent.com/$Repository/$commit"
$media = "https://media.githubusercontent.com/media/$Repository/$commit"

$staging = Join-Path ([IO.Path]::GetTempPath()) "macula-hebrew-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'tsv') | Out-Null

try {
    Write-Host "Fetching $Repository at $($commit.Substring(0, 7))"

    Invoke-WebRequest -Uri "$raw/LICENSE.md" -OutFile (Join-Path $staging 'LICENSE-upstream.md')
    Invoke-WebRequest -Uri "$raw/README.md" -OutFile (Join-Path $staging 'README-upstream.md')
    $licence = Get-Content (Join-Path $staging 'LICENSE-upstream.md') -Raw

    if ($licence -notmatch [regex]::Escape($ExpectedLicence)) {
        throw "LICENSE.md no longer licenses the MACULA Hebrew datasets under CC BY 4.0. That " +
              "sentence is why the participant referents can be served, so a change to it is the " +
              "owner's decision and not a download: nothing was replaced."
    }

    if ($licence -notmatch [regex]::Escape($ExpectedAttribution)) {
        throw "LICENSE.md no longer gives the attribution string this corpus records on every row " +
              "it loads from here. Carry the new wording into Resources/MaculaHebrew/LICENCE.md and " +
              "into MaculaHebrewAnnotation.Source by hand; nothing was replaced."
    }

    if ($licence -notmatch [regex]::Escape($ExpectedReferents)) {
        throw "LICENSE.md no longer lists the participant referents among Clear's own datasets " +
              "under that licence. They are the reason this file is fetched; nothing was replaced."
    }

    $file = Join-Path $staging 'tsv\macula-hebrew.tsv'
    Invoke-WebRequest -Uri "$media/$Table" -OutFile $file

    $lines = [IO.File]::ReadAllLines($file)
    if ($lines[0].StartsWith('version https://git-lfs')) {
        throw "The download is a git LFS pointer, not the table. Nothing was replaced."
    }

    $header = $lines[0] -split "`t"
    foreach ($column in $Columns) {
        if ($header -notcontains $column) {
            throw "The word table has no column named '$column'. Its generator has changed; the " +
                  "reader (MaculaHebrewReader) has to change with it. Nothing was replaced."
        }
    }

    $morphs = $lines.Length - 1
    if ($morphs -ne $ExpectedMorphs) {
        throw "The word table holds $morphs morphs where the commit read held $ExpectedMorphs. " +
              "The join to BHSA was measured on that file; re-measure it before moving this count. " +
              "Nothing was replaced."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'MaculaHebrew'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -Path (Join-Path $staging '*') -Destination $root -Recurse -Force

    $size = (Get-Item $file).Length / 1MB
    Write-Host ("{0:N0} morphs of the Westminster Leningrad Codex, {1:N1} MB, in {2}" -f $morphs, $size, $root)
    Write-Host "Taken from github.com/$Repository at $commit"
    Write-Host "If the commit moved, record it in $root\LICENCE.md and run: python scripts/corpus-manifest.py"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
