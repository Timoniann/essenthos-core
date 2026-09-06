<#
.SYNOPSIS
    Fetches Tischendorf's eighth edition Greek New Testament, parsed, numbered and lemmatised.

.DESCRIPTION
    One of the two editions Nestle 1904 was voted out of. The corpus already holds Nestle's output
    and none of his inputs, so this and the Westcott-Hort beside it are what let a reader see which
    two of the three editions outvoted the third at any given place.

    Two things are taken. The Unicode word-per-line release, which is what gets loaded; and the two
    READMEs inside the release, which are where the public-domain grant actually is — the repository
    has no LICENSE file and its top-level README states no terms at all. The BETA-encoded copy of the
    same words and the OSIS-XML export are left where they are; nothing reads them.

    The licence is checked, not assumed. Both READMEs must still dedicate the text *and its analysis*
    to the public domain, and if either stops this stops and replaces nothing. The commit is printed
    so it can be recorded in the LICENCE.md kept beside the data.

.EXAMPLE
    ./scripts/fetch-tischendorf.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # A branch, tag or commit of morphgnt/tischendorf-data.
    [string] $Ref = 'master',

    # The release inside the repository. 2.8 is the latest and the only one this reader was
    # written against; the folder name is part of the path the loader reads.
    [string] $Release = '2.8'
)

$ErrorActionPreference = 'Stop'

$Repository = 'morphgnt/tischendorf-data'
$ExpectedLicence = 'in the Public Domain'
$ExpectedGrant = 'its analysis'

# The 27 books, named as the release names them: the Online Bible book stems.
$Books = @(
    'MT', 'MR', 'LU', 'JOH', 'AC', 'RO', '1CO', '2CO', 'GA', 'EPH', 'PHP', 'COL', '1TH', '2TH',
    '1TI', '2TI', 'TIT', 'PHM', 'HEB', 'JAS', '1PE', '2PE', '1JO', '2JO', '3JO', 'JUDE', 'RE'
)

# The whole New Testament, one word to a line. A short download is a partial one.
$ExpectedWords = 137711

$commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Ref" `
    -Headers @{ 'User-Agent' = 'essenthos' }).sha
$raw = "https://raw.githubusercontent.com/$Repository/$commit"
# Not $release: PowerShell does not distinguish it from the $Release parameter, and
# assigning it here silently turned every path built from $Release into word-per-line/2.8.
$releaseUrl = "word-per-line/$Release"

$staging = Join-Path ([IO.Path]::GetTempPath()) "tischendorf-$([guid]::NewGuid().ToString('n'))"
$unicode = Join-Path $staging "word-per-line\$Release\Unicode"
New-Item -ItemType Directory -Force -Path $unicode | Out-Null

try {
    Write-Host "Fetching $Repository at $($commit.Substring(0, 7)), release $Release"

    Invoke-WebRequest -Uri "$raw/README.md" -OutFile (Join-Path $staging 'README-upstream.md')
    foreach ($name in @('README.txt', 'README-short.txt', 'parsing.txt')) {
        Invoke-WebRequest -Uri "$raw/$releaseUrl/$name" `
            -OutFile (Join-Path $staging "word-per-line\$Release\$name")
    }

    foreach ($name in @('README.txt', 'README-short.txt')) {
        $text = Get-Content (Join-Path $staging "word-per-line\$Release\$name") -Raw
        if ($text -notmatch [regex]::Escape($ExpectedLicence)) {
            throw "$name no longer places this edition in the public domain. The repository has no " +
                  "LICENSE file, so these two READMEs are the whole of the grant and a change in " +
                  "either is the owner's decision, not a download: nothing was replaced. Read the " +
                  "new statement before fetching again."
        }

        if ($text -notmatch [regex]::Escape($ExpectedGrant)) {
            throw "$name no longer says the *analysis* is in the public domain, only the text. The " +
                  "morphology, the Strong numbers and the lemmas are the whole reason to take this " +
                  "file rather than a scan, and a grant that covers only the text does not cover " +
                  "them; nothing was replaced."
        }
    }

    $words = 0
    foreach ($book in $Books) {
        $path = Join-Path $unicode "$book.txt"
        Invoke-WebRequest -Uri "$raw/$releaseUrl/Unicode/$book.txt" -OutFile $path
        $words += (Get-Content $path | Where-Object { $_.Trim() } | Measure-Object).Count
    }

    if ($words -ne $ExpectedWords) {
        throw "The release holds $words words and 2.8 has $ExpectedWords. Either this is a partial " +
              "download or the release changed; nothing was replaced."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'Tischendorf'
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
    Write-Host ("{0:N0} words in 27 books, {1:N1} MB in {2}" -f $words, $size, $root)
    Write-Host "Taken from github.com/$Repository at $commit"
    Write-Host "Record that commit in $root\LICENCE.md if this replaced an earlier fetch."
    Write-Host "Then run: python scripts/corpus-manifest.py, and commit the manifest."
    Write-Host ("A reload is not automatic: the corpus loader returns early for a text whose slug " +
                "is already in the text table, so a restart alone picks nothing up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
