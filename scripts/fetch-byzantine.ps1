<#
.SYNOPSIS
    Fetches Robinson and Pierpont's Byzantine Textform — the reading of the majority of the
    surviving Greek manuscripts, parsed and numbered.

.DESCRIPTION
    The fourth Greek witness. Nestle 1904 is a critical text and the two Textus Receptus editions
    are Erasmus's; none of the three says what the manuscript tradition as a whole reads, and this
    does. Every word carries a Strong number, so it joins the other three Greek panes on the numbers
    they all state, with no aligner.

    Two things are taken. Robinson's own parsed beta code, which is what gets loaded; and the
    repository's Unicode conversion of exactly those files, which is not loaded and is kept as the
    answer key the beta-code table is checked against. The accented CCAT text with its apparatus and
    the TEI-XML are left where they are — nothing reads them yet.

    The licence is checked, not assumed. Both statements attached to the bytes must still be
    public-domain dedications, and if either stops this stops and replaces nothing. The commit is
    printed so it can be recorded in the LICENCE.md kept beside the data.

.EXAMPLE
    ./scripts/fetch-byzantine.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # A branch, tag or commit of byztxt/byzantine-majority-text. Releases are 3.x.x; the README
    # warns that the Unicode files of anything before 2.0.3 carry conversion errors.
    [string] $Ref = 'master'
)

$ErrorActionPreference = 'Stop'

$Repository = 'byztxt/byzantine-majority-text'
$ExpectedLicence = 'released into the public domain'
$ExpectedReadmeLicence = 'in the Public Domain'

# The 27 books, named as the repository names them.
$Books = @(
    '01_MAT', '02_MAR', '03_LUK', '04_JOH', '05_ACT', '06_ROM', '07_1CO', '08_2CO', '09_GAL',
    '10_EPH', '11_PHP', '12_COL', '13_1TH', '14_2TH', '15_1TI', '16_2TI', '17_TIT', '18_PHM',
    '19_HEB', '20_JAM', '21_1PE', '22_2PE', '23_1JO', '24_2JO', '25_3JO', '26_JUD', '27_REV'
)

# The whole New Testament, as the 2018 edition prints it. A short download is a partial one.
$ExpectedVerses = 7957

$commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Ref" `
    -Headers @{ 'User-Agent' = 'essenthos' }).sha
$raw = "https://raw.githubusercontent.com/$Repository/$commit"

$staging = Join-Path ([IO.Path]::GetTempPath()) "byzantine-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'strongs') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'unicode') | Out-Null

try {
    Write-Host "Fetching $Repository at $($commit.Substring(0, 7))"

    Invoke-WebRequest -Uri "$raw/LICENSE.txt" -OutFile (Join-Path $staging 'LICENSE-upstream.txt')
    Invoke-WebRequest -Uri "$raw/README.md" -OutFile (Join-Path $staging 'README-upstream.md')

    $licence = Get-Content (Join-Path $staging 'LICENSE-upstream.txt') -Raw
    if ($licence -notmatch [regex]::Escape($ExpectedLicence)) {
        throw "LICENSE.txt no longer dedicates this text to the public domain. That is the whole " +
              "reason this edition could be loaded without asking anyone, so a licence that " +
              "changed under us is the owner's decision and not a download: nothing was replaced. " +
              "Read the new statement before fetching again."
    }

    $readme = Get-Content (Join-Path $staging 'README-upstream.md') -Raw
    if ($readme -notmatch [regex]::Escape($ExpectedReadmeLicence)) {
        throw "README.md no longer says the text is in the Public Domain while LICENSE.txt still " +
              "does. Two statements about the same bytes that disagree is exactly the case that " +
              "has to be read by a person; nothing was replaced."
    }

    $verses = 0
    foreach ($book in $Books) {
        $parsed = Join-Path $staging "strongs\$book.BP5"
        Invoke-WebRequest -Uri "$raw/source/Strongs/$book.BP5" -OutFile $parsed
        $verses += (Get-Content $parsed | Where-Object { $_.Trim() } | Measure-Object).Count

        # The repository's own Unicode conversion of the same file, kept as the answer key the
        # beta-code table is checked against. The book stem loses its number here.
        $stem = $book.Substring(3)
        Invoke-WebRequest -Uri "$raw/csv-unicode/strongs/with-parsing/$stem.csv" `
            -OutFile (Join-Path $staging "unicode\$stem.csv")
    }

    if ($verses -ne $ExpectedVerses) {
        throw "The parsed files hold $verses verses and the 2018 edition has $ExpectedVerses. " +
              "Either this is a partial download or the edition changed; nothing was replaced."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'Byzantine'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -Path (Join-Path $staging '*') -Destination $root -Recurse -Force

    $size = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} verses in 27 books, {1:N1} MB in {2}" -f $verses, $size, $root)
    Write-Host "Taken from github.com/$Repository at $commit"
    Write-Host "Record that commit in $root\LICENCE.md if this replaced an earlier fetch."
    Write-Host ("Then run: python scripts/corpus-manifest.py, and commit the manifest.")
    Write-Host ("A reload is not automatic: the corpus loader returns early for a text whose slug " +
                "is already in the text table, so a restart alone picks nothing up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
