<#
.SYNOPSIS
    Fetches Brady Stephenson's BibleData — the people, relationships, names and Old Testament
    chronology the encyclopedia is built from — and records what it was taken under.

.DESCRIPTION
    GitHub is the default source because it is where the LICENSE that governs lives, it needs no
    credentials, and a commit can be recorded. Kaggle republishes the same twenty-two files and is
    offered for the day GitHub is not reachable; it needs an API token and says so before it
    downloads anything rather than half-way through.

    The download lands in a temporary folder and is checked whole before anything is replaced, so
    an interrupted fetch leaves the corpus sources exactly as they were.

    The licence is checked, not assumed. This dataset was ShareAlike until May 2026 and its
    CITATION.cff went on saying so long after the LICENSE file had changed, so both are read and
    both must say Attribution 4.0. If either stops, this stops and says so: a licence that changed
    under us is a decision for the owner, not a download. Kaggle still serves the old CITATION.cff
    and its dataset page still says NonCommercial-ShareAlike, which is the second reason GitHub is
    the default.

    Sixteen of the files are loaded into the corpus and six are not. Where a loaded file changes,
    this says which, because the encyclopedia loader does nothing when the tables already hold rows
    — a new release is not picked up by restarting.

.EXAMPLE
    ./scripts/fetch-bibledata.ps1

.EXAMPLE
    ./scripts/fetch-bibledata.ps1 -From Kaggle
#>

[CmdletBinding()]
param(
    # Where this project keeps its resources; the default matches Dataset:ResourcesPath.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    [ValidateSet('GitHub', 'Kaggle')]
    [string] $From = 'GitHub',

    # GitHub only. A branch, tag or commit; 'main' is the only place the Attribution licence holds.
    [string] $Ref = 'main'
)

$ErrorActionPreference = 'Stop'

$FolderName = 'BibleData2026'
$Repository = 'BradyStephenson/bible-data'
$KaggleDataset = 'bradystephenson/bibledata'
$ExpectedLicence = 'Creative Commons Attribution 4.0 International Public License'
$ExpectedCitationLicence = 'CC-BY-4.0'

# The files the encyclopedia reads. Everything else in the release is carried but never loaded.
$Loaded = @(
    'BibleData-Book.csv', 'BibleData-Commandments.csv', 'BibleData-Epoch.csv',
    'BibleData-Event.csv', 'BibleData-Person.csv', 'BibleData-PersonLabel.csv',
    'BibleData-PersonRelationship.csv', 'BibleData-PersonVerse.csv',
    'BibleData-PersonVerseApostolic.csv', 'BibleData-PersonVerseTanakh.csv',
    'BibleData-Place.csv', 'BibleData-PlaceLabel.csv', 'BibleData-PlaceVerse.csv',
    'BibleData-Reference.csv', 'Ussher-AnnalsOfTheWorld.csv', 'LICENSE'
)

$Carried = @(
    'README.md', 'CITATION.cff', 'AlamoPolyglot.csv', 'HebrewStrongs.csv',
    'HitchcocksBibleNamesDictionary.csv', 'NavesTopicalDictionary.csv'
)

function Get-KaggleCredential {
    if ($env:KAGGLE_USERNAME -and $env:KAGGLE_KEY) {
        return @{ Username = $env:KAGGLE_USERNAME; Key = $env:KAGGLE_KEY }
    }

    $json = Join-Path $HOME '.kaggle' 'kaggle.json'
    if (Test-Path $json) {
        $parsed = Get-Content $json -Raw | ConvertFrom-Json
        if ($parsed.username -and $parsed.key) {
            return @{ Username = $parsed.username; Key = $parsed.key }
        }
    }

    throw "Kaggle needs an API token and none was found. Set KAGGLE_USERNAME and KAGGLE_KEY, or " +
          "put the token at $json — create it under Account on kaggle.com. Nothing was downloaded " +
          "and the corpus sources are untouched. GitHub carries the same files and needs no token: " +
          "run this without -From Kaggle."
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "bibledata-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $archive = Join-Path $staging 'release.zip'

    if ($From -eq 'GitHub') {
        $commit = (Invoke-RestMethod -Uri "https://api.github.com/repos/$Repository/commits/$Ref" `
            -Headers @{ 'User-Agent' = 'essenthos' }).sha
        $taken = "github.com/$Repository at $commit"
        Write-Host "Fetching $Repository at $($commit.Substring(0, 7))"
        Invoke-WebRequest -Uri "https://codeload.github.com/$Repository/zip/$commit" -OutFile $archive
    }
    else {
        $credential = Get-KaggleCredential
        $pair = [Convert]::ToBase64String(
            [Text.Encoding]::ASCII.GetBytes("$($credential.Username):$($credential.Key)"))
        $taken = "kaggle.com/datasets/$KaggleDataset"
        Write-Host "Fetching $KaggleDataset from Kaggle as $($credential.Username)"
        Invoke-WebRequest -Uri "https://www.kaggle.com/api/v1/datasets/download/$KaggleDataset" `
            -Headers @{ Authorization = "Basic $pair" } -OutFile $archive
    }

    $unpacked = Join-Path $staging 'unpacked'
    Expand-Archive -Path $archive -DestinationPath $unpacked -Force

    # GitHub's zipball nests everything one folder deep; Kaggle's does not.
    $found = @{}
    foreach ($name in ($Loaded + $Carried)) {
        $file = Get-ChildItem -Path $unpacked -Filter $name -Recurse -File | Select-Object -First 1
        if (-not $file) {
            throw "The download has no $name. All $($Loaded.Count + $Carried.Count) expected files " +
                  "must be present, so this is a partial download or a restructured release. " +
                  "Nothing was replaced; look at what $taken now holds before running this again."
        }
        $found[$name] = $file.FullName
    }

    $licence = (Get-Content $found['LICENSE'] -TotalCount 1).Trim()
    if ($licence -ne $ExpectedLicence) {
        throw "The LICENSE file now begins '$licence' and the corpus attributes this dataset as " +
              "CC BY 4.0, which is what governs by the author's own ruling. A licence that changed " +
              "is a decision for the owner, not a download. Nothing was replaced; take the answer " +
              "to the owner before fetching again."
    }

    $cited = (Select-String -Path $found['CITATION.cff'] -Pattern '^license:\s*"?([^"\s]+)"?' `
        ).Matches.Groups[1].Value
    if ($cited -ne $ExpectedCitationLicence) {
        throw "CITATION.cff states '$cited' where the LICENSE file states Attribution 4.0. This " +
              "dataset was ShareAlike until May 2026 and its citation file lagged the change by " +
              "months, so the two disagreeing again means the copy is stale or the terms moved. " +
              "Kaggle serves exactly this disagreement. Nothing was replaced; fetch from GitHub " +
              "main, and if that is where this came from, the answer is the owner's."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) $FolderName
    New-Item -ItemType Directory -Force -Path $root | Out-Null

    $changed = @()
    foreach ($name in $Loaded) {
        $target = Join-Path $root $name
        if (Test-Path $target) {
            $before = (Get-FileHash $target -Algorithm SHA256).Hash
            $after = (Get-FileHash $found[$name] -Algorithm SHA256).Hash
            if ($before -ne $after) { $changed += $name }
        }
        else {
            $changed += $name
        }
    }

    foreach ($name in ($Loaded + $Carried)) {
        Copy-Item -Path $found[$name] -Destination (Join-Path $root $name) -Force
    }

    $when = (Get-Date).ToString('yyyy-MM-dd')
    $licenceNote = @"
# BibleData

**BibleData: Structured Datasets from the Holy Bible**, by Brady Stephenson,
<https://github.com/BradyStephenson/bible-data>, also on Zenodo as
<https://doi.org/10.5281/zenodo.19539956>.

Taken from $taken on $when by ``scripts/fetch-bibledata.ps1``.

## What it is used under: CC BY 4.0

<https://creativecommons.org/licenses/by/4.0/>

## Every statement attached to these bytes, and they now agree

On current ``main`` all three files in the release say the same thing:

| Where | What it says |
|---|---|
| ``LICENSE`` | Creative Commons Attribution 4.0 International |
| ``README.md`` badge and Licence section | CC BY 4.0, adding "including for commercial purposes" |
| ``CITATION.cff`` | ``CC-BY-4.0`` |
| the GitHub repository record | Creative Commons Attribution 4.0 International |

That was not always true. The repository was CC BY-NC-SA 3.0 from 2021 and CC BY-NC-SA 4.0 from
April 2026, and its ``CITATION.cff`` went on saying ``CC-BY-NC-SA-4.0`` after the LICENSE file had
changed. The author settled that in his own issue tracker — the LICENSE file governs, the CITATION
file was wrong — and has since corrected the CITATION file to match. Nothing in the release
contradicts anything else any more.

So this copy is Attribution 4.0: **no ShareAlike obligation reaches anything derived from it.**
That is the clause worth being sure about, because the annotation this corpus builds on top of the
dataset would inherit it.

**The version matters, and Kaggle is behind.** The ``v1.0.0`` tag, the Zenodo release, and the
Kaggle dataset page are all still ShareAlike, and the copy Kaggle serves carries the old
``CITATION.cff`` saying ``CC-BY-NC-SA-4.0`` while its fifteen loaded data files are byte-identical
to ``main``. Its dataset page states CC BY-NC-SA 3.0 IGO. That is why this script fetches from
GitHub by default: same data, current terms, and a commit that can be recorded. A copy taken from
anywhere else would bind the corpus to ShareAlike without anyone deciding to.

## Attribution

Credited at ``/v1/datasets`` whether or not the licence demands it, so that a reader can tell what
rests on someone else's work and what is ours.

> Brady Stephenson. (2026). *BibleData: Structured Datasets from the Holy Bible* (Version 1.0)
> [Data set]. Zenodo. https://doi.org/10.5281/zenodo.19539956

Contributors named in the release's own README: Brady Stephenson for all but two files, Dan Raby
for the person labels, Fernando Falci for the person relationships.

## What is loaded and what is not

Sixteen files are read by the encyclopedia loader. Six are carried and never loaded: the
release's own ``README.md`` and ``CITATION.cff``, and four datasets that are separate works —
the Alamo Polyglot, Strong's Hebrew concordance, Naves Topical Dictionary and Hitchcock's Bible
Names Dictionary. Loading any of those is a corpus decision and not a consequence of downloading
them.

None of the four is Stephenson's own composition, and their underlying works are out of copyright
rather than licensed by him: Hitchcock (1869), Naves (1897) and Strong (1890) are public domain,
and the Polyglot's ten component texts each carry their own terms — the World English Bible and
the King James are free, but Brenton, the Leningrad Codex and the JPS 1917 have to be read one by
one before any of them is served. What CC BY 4.0 covers is his transcription and structuring of
them, which is a real contribution and is what the credit above is for.

## Ussher's *Annals of the World*, which is loaded and is not Stephenson's work

``Ussher-AnnalsOfTheWorld.csv`` is the sixteenth loaded file and the only one of the separate
works the corpus reads, so its terms are worth stating on their own rather than inside the
paragraph above.

**Two layers, and both are clear.** James Ussher's *Annales Veteris Testamenti* is 1650 and its
English translation by Edmund Pierce is 1658; both are out of copyright by age everywhere, and no
licence of Stephenson's could take that away or add to it. What he contributes is the
transcription into 7,000 numbered paragraphs with a year in four reckonings against each, and
that structuring is his — covered by the CC BY 4.0 above, with no ShareAlike obligation to carry.
He names himself for this file in the release's own contributor list.

Nothing in the release says anything narrower about it. The ``LICENSE`` file, the ``README``
badge and licence section, the corrected ``CITATION.cff`` and the repository record are the four
statements read above; none of them carves any file out, and there is no per-file notice beside
this one. So the most restrictive statement actually attached to these bytes is Attribution 4.0,
over a public-domain work.

**Credited as Ussher's, not as ours.** Every row the corpus writes from it carries him as the
author, the paragraph number it came from, and Stephenson's transcription as the route — and
where an event's title had to be made rather than quoted, the row says which and by what. That
last part is not a licence obligation. It is the same rule the rest of the corpus keeps: a reader
has to be able to tell what a source said from what this project did with it.
"@

    Set-Content -Path (Join-Path $root 'LICENCE.md') -Value $licenceNote -Encoding UTF8

    $size = (Get-ChildItem $root -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0} files, {1:N0} MB in {2}" -f (Get-ChildItem $root -File).Count, $size, $root)

    if ($changed.Count -eq 0) {
        Write-Host "No loaded file changed. The corpus is already this release."
    }
    else {
        Write-Warning ("$($changed.Count) loaded file(s) changed: $($changed -join ', '). " +
            "Restarting the API will not pick this up — the encyclopedia loader does nothing when " +
            "the tables already hold rows. The encyclopedia has to be cleared and rebuilt, and " +
            "everything hanging off its entities goes with it.")
    }
}
finally {
    Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
}
