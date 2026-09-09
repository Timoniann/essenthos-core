<#
.SYNOPSIS
    Fetches MorphGNT's morphological parsing of the SBL Greek New Testament — a second stated
    morphology for the Greek, against which the one Nestle 1904 carries can be checked.

.DESCRIPTION
    The corpus holds one analysis of the Greek New Testament and has no way to tell a mistake in it
    from a fact. MorphGNT is a second: part of speech, person, tense, voice, mood, case, number,
    gender and degree, given by a different editor over a different edition, for the same sentences.
    Where the two agree the corpus can say so; where they disagree that is a finding rather than a
    field to overwrite.

    Two licences meet in these files and only one of them is being exercised here. The parsing and
    the lemmatisation are Tauber's, CC BY-SA 3.0. The four text columns beside them are the SBLGNT,
    which is a different work with its own terms — CC BY 4.0 since December 2022, and read at the
    source rather than assumed. **Only the parsing is loaded.** The text columns are read to find
    which Nestle word each parsing belongs to and are then dropped: no SBLGNT word is stored, no
    SBLGNT text row is created, and nothing this corpus serves reproduces that edition. See
    Resources/MorphGnt/LICENCE.md, which quotes both statements and names the commit they were read
    at.

    The licence is checked rather than assumed. The README is the only statement attached to these
    bytes — the repository carries no LICENSE file at all — so if its two sentences stop saying what
    they say today, this stops and replaces nothing.

.EXAMPLE
    ./scripts/fetch-morphgnt.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # A branch, tag or commit of morphgnt/sblgnt. The repository is versioned by release tag in its
    # citation line rather than by branch, and master has been the released state since 2024.
    [string] $Ref = 'master'
)

$ErrorActionPreference = 'Stop'

$Repository = 'morphgnt/sblgnt'

# The two sentences that are the whole licence of this data. They are in the README because there is
# no LICENSE file; a repository with neither could not be loaded here at all.
$ExpectedParsingLicence = 'morphological parsing and lemmatization is made available under a'
$ExpectedParsingUrl = 'creativecommons.org/licenses/by-sa/3.0'
$ExpectedTextStatement = 'The SBLGNT text itself is subject to the'

# The 27 books, named as the repository names them. The leading number is the old Nestle numbering,
# in which Matthew is 61; the addresses inside the files count the New Testament from 01.
$Books = @(
    '61-Mt', '62-Mk', '63-Lk', '64-Jn', '65-Ac', '66-Ro', '67-1Co', '68-2Co', '69-Ga',
    '70-Eph', '71-Php', '72-Col', '73-1Th', '74-2Th', '75-1Ti', '76-2Ti', '77-Tit', '78-Phm',
    '79-Heb', '80-Jas', '81-1Pe', '82-2Pe', '83-1Jn', '84-2Jn', '85-3Jn', '86-Jud', '87-Re'
)

# Every word of the SBLGNT as MorphGNT parses it, version 6.12. A short download is a partial one,
# and a partial one is a Nestle book that silently gains no second opinion at all.
$ExpectedWords = 137554

# Seven columns: address, part of speech, parsing code, text, word, normalised word, lemma.
$Columns = 7

$commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Ref" `
    -Headers @{ 'User-Agent' = 'essenthos' }).sha
$raw = "https://raw.githubusercontent.com/$Repository/$commit"

$staging = Join-Path ([IO.Path]::GetTempPath()) "morphgnt-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'parsing') | Out-Null

try {
    Write-Host "Fetching $Repository at $($commit.Substring(0, 7))"

    Invoke-WebRequest -Uri "$raw/README.md" -OutFile (Join-Path $staging 'README-upstream.md')
    $readme = Get-Content (Join-Path $staging 'README-upstream.md') -Raw

    if ($readme -notmatch [regex]::Escape($ExpectedParsingLicence) -or
        $readme -notmatch [regex]::Escape($ExpectedParsingUrl)) {
        throw "README.md no longer offers the parsing and lemmatisation under CC BY-SA 3.0. That " +
              "sentence is the only licence statement attached to these bytes — the repository " +
              "has no LICENSE file — so a change to it is the owner's decision and not a " +
              "download: nothing was replaced. Read the new statement before fetching again."
    }

    if ($readme -notmatch [regex]::Escape($ExpectedTextStatement)) {
        throw "README.md no longer separates the SBLGNT text from the parsing. The two are " +
              "licensed differently and only the parsing is loaded here, so a README that stops " +
              "distinguishing them has to be read by a person; nothing was replaced."
    }

    $words = 0
    foreach ($book in $Books) {
        $file = Join-Path $staging "parsing\$book-morphgnt.txt"
        Invoke-WebRequest -Uri "$raw/$book-morphgnt.txt" -OutFile $file

        foreach ($line in Get-Content $file) {
            if (-not $line.Trim()) { continue }
            if (($line -split '\s+').Count -ne $Columns) {
                throw "$book-morphgnt.txt has a line with something other than $Columns columns: " +
                      "'$line'. The format changed, or the download is corrupt; nothing was replaced."
            }
            $words++
        }
    }

    if ($words -ne $ExpectedWords) {
        throw "The parsing files hold $words words and version 6.12 has $ExpectedWords. Either " +
              "this is a partial download or the edition changed; nothing was replaced. If the " +
              "edition changed, the join against Nestle 1904 has to be re-measured before the " +
              "count here is moved."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'MorphGnt'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -Path (Join-Path $staging '*') -Destination $root -Recurse -Force

    $size = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} parsed words in 27 books, {1:N1} MB in {2}" -f $words, $size, $root)
    Write-Host "Taken from github.com/$Repository at $commit"
    Write-Host "Record that commit in $root\LICENCE.md if this replaced an earlier fetch."
    Write-Host "Then run: python scripts/corpus-manifest.py, and commit the manifest."
    Write-Host ("A reload is not automatic: the parsing loader returns early once any Nestle word " +
                "carries a MorphGNT parsing, so a restart alone picks nothing up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
