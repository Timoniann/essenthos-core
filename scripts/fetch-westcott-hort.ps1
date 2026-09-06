<#
.SYNOPSIS
    Fetches Maurice Robinson's parsed Westcott-Hort 1881, and the plain transcription that is its
    answer key.

.DESCRIPTION
    The other edition Nestle 1904 was voted out of, and the cheapest text in the corpus to load:
    Robinson writes it in the same notation as his Textus Receptus, so the parser already exists.

    Two things are taken. The parsed files, which are what gets loaded; and the repository's own
    plain transcription of the same text, which is not loaded and is kept as the answer key. That
    file is what settles which side of a variant group Westcott and Hort actually printed — the
    parsed file carries their marginal readings inline at 1,653 places, and a transcription that
    preferred the wrong side would be a different edition under the same name.

    The licence is checked, not assumed. The README must still read Public Domain, and if it stops
    this stops and replaces nothing. The commit is printed so it can be recorded in the LICENCE.md
    kept beside the data.

.EXAMPLE
    ./scripts/fetch-westcott-hort.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # A branch, tag or commit of byztxt/greektext-westcott-hort.
    [string] $Ref = 'master'
)

$ErrorActionPreference = 'Stop'

$Repository = 'byztxt/greektext-westcott-hort'
$ExpectedLicence = 'Public Domain'

# The 27 books, named as the repository names them: the Online Bible book stems.
$Books = @(
    'MT', 'MR', 'LU', 'JOH', 'AC', 'RO', '1CO', '2CO', 'GA', 'EPH', 'PHP', 'COL', '1TH', '2TH',
    '1TI', '2TI', 'TIT', 'PHM', 'HEB', 'JAS', '1PE', '2PE', '1JO', '2JO', '3JO', 'JUDE', 'RE'
)

# Every address the file numbers, including the four the critical text has no words under. A short
# download is a partial one.
$ExpectedVerses = 7941

$commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Ref" `
    -Headers @{ 'User-Agent' = 'essenthos' }).sha
$raw = "https://raw.githubusercontent.com/$Repository/$commit"

$staging = Join-Path ([IO.Path]::GetTempPath()) "westcott-hort-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'parsed') | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'textonly') | Out-Null

try {
    Write-Host "Fetching $Repository at $($commit.Substring(0, 7))"

    Invoke-WebRequest -Uri "$raw/README.md" -OutFile (Join-Path $staging 'README-upstream.md')

    $readme = Get-Content (Join-Path $staging 'README-upstream.md') -Raw
    if ($readme -notmatch [regex]::Escape($ExpectedLicence)) {
        throw "README.md no longer says this text is in the Public Domain. There is no LICENSE file " +
              "in this repository, so that one line is the whole of the grant, and a licence that " +
              "changed under us is the owner's decision and not a download: nothing was replaced."
    }

    $verses = 0
    foreach ($book in $Books) {
        $parsed = Join-Path $staging "parsed\$book.UWH"
        Invoke-WebRequest -Uri "$raw/parsed/$book.UWH" -OutFile $parsed
        $verses += (Select-String -Path $parsed -Pattern '^\d+:\d+ ' -AllMatches).Count

        # The plain transcription of the same text, kept as the answer key the choice of variant
        # side is checked against, and never loaded.
        Invoke-WebRequest -Uri "$raw/textonly/$book.WH" `
            -OutFile (Join-Path $staging "textonly\$book.WH")
    }

    Invoke-WebRequest -Uri "$raw/textonly/TITLES.W-H" `
        -OutFile (Join-Path $staging 'textonly\TITLES.W-H')

    if ($verses -ne $ExpectedVerses) {
        throw "The parsed files hold $verses verse addresses and this edition has $ExpectedVerses. " +
              "Either this is a partial download or the transcription changed; nothing was replaced."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'WestcottHort'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    # File by file, at the path it stands at under the staging root. Copying a folder with
    # -Recurse puts a nested copy inside one that is already there, which is how the release first
    # landed at word-per-line/word-per-line/2.8 and the loader found not one book of it.
    foreach ($file in Get-ChildItem $staging -Recurse -File -Force) {
        $destination = Join-Path $root $file.FullName.Substring($staging.Length + 1)
        New-Item -ItemType Directory -Force -Path (Split-Path $destination) | Out-Null
        Copy-Item -Path $file.FullName -Destination $destination -Force
    }

    $size = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} verse addresses in 27 books, {1:N1} MB in {2}" -f $verses, $size, $root)
    Write-Host "Taken from github.com/$Repository at $commit"
    Write-Host "Record that commit in $root\LICENCE.md if this replaced an earlier fetch."
    Write-Host "Then run: python scripts/corpus-manifest.py, and commit the manifest."
    Write-Host ("A reload is not automatic: the corpus loader returns early for a text whose slug " +
                "is already in the text table, so a restart alone picks nothing up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
