<#
.SYNOPSIS
    Fetches Swete's Septuagint, the corpus's second Greek Old Testament and the first diplomatic
    text of Codex Vaticanus it holds.

.DESCRIPTION
    Brenton printed a Greek text to face his English translation; Swete printed a manuscript, with
    the readings of the other uncials kept in an apparatus at the foot of the page instead of in
    the text. The two disagree about the verse division of most of the books they share, and every
    one of those disagreements is a reading somebody can ask about, which is why the corpus holds
    both rather than replacing one with the other.

    The licence is on the transcription and not on the text: Swete died in 1917 and the volumes
    were printed between 1887 and 1905, so the edition is out of copyright everywhere. What is
    licensed is the digitisation, and both statements attached to the bytes say Creative Commons
    Attribution-ShareAlike 4.0 -- the README of nathans/lxx-swete, and the full legal code that
    Open Greek and Latin ships as license.md in the First1KGreek repository the text is derived
    from. Both are checked here before anything is written, and both are kept beside the data,
    because a licence that lives only at a URL is one nobody can check offline.

    Pinned to the commit Resources/Swete/LICENCE.md quotes. An unpinned fetch of a share-alike
    source is an obligation whose terms could have moved since anybody read them.

    Three of the fifty-five files are the Old Greek of Susanna, Daniel and Bel, which are another
    translation and so another witness rather than another book, and are loaded as a text of their
    own; the Odes, two of which this edition numbers iva and ivb, are read into the chapters Rahlfs
    numbers them by. 48.Isaias.txt is taken and not loaded: it is Ottley's Codex Alexandrinus Isaiah of
    1904 rather than Swete's, because the upstream build script names its output after the book and
    the one book with two Greek editions was overwritten by the second. It is fetched so that a
    reader can see for themselves what the file holds.

    Both Isaiahs are then fetched from First1KGreek itself -- Swete's (grc1) and Ottley's (grc2),
    the TEI the upstream script converts -- into First1KGreek/ under this project's own names, so
    neither this script's clearing of the folder nor any file the upstream names can overwrite
    them. Each states its licence in its own header, and that statement is checked before either
    is kept.

.EXAMPLE
    ./scripts/fetch-swete.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# The commit Resources/Swete/LICENCE.md was read at. Move it only after reading the licence again.
$Commit = '26bad3eb42bba98471d154c954e36a6f30a0279d'

$Repository = 'https://github.com/nathans/lxx-swete'
$Raw = "https://raw.githubusercontent.com/nathans/lxx-swete/$Commit"
$Archive = "https://codeload.github.com/nathans/lxx-swete/tar.gz/$Commit"

# The upstream transcription, whose licence governs the text these files were made from.
$UpstreamLicence = 'https://raw.githubusercontent.com/OpenGreekAndLatin/First1KGreek/master/license.md'
$UpstreamZenodo = 'https://raw.githubusercontent.com/OpenGreekAndLatin/First1KGreek/master/.zenodo.json'

$ExpectedLicence = 'Creative Commons Attribution-ShareAlike 4.0'

# First1KGreek's two Greek editions of Isaiah, pinned to the last commit that changed them, with the
# name each is kept under and its size in bytes at that commit.
$First1KGreekCommit = 'b67137e6b82669d08fe6ad1c225999ca6aca362c'
$First1KGreekRaw = "https://raw.githubusercontent.com/OpenGreekAndLatin/First1KGreek/$First1KGreekCommit/data/tlg0527/tlg048"
$Isaiahs = @(
    @{ Source = 'tlg0527.tlg048.1st1K-grc1.xml'; Target = 'isaiah-swete-1905.xml'; Bytes = 688809 },
    @{ Source = 'tlg0527.tlg048.1st1K-grc2.xml'; Target = 'isaiah-ottley-1904.xml'; Bytes = 520194 }
)
$ExpectedTeiLicence = 'Creative Commons Attribution-ShareAlike 4.0 International'
$ExpectedUpstreamLicence = 'Attribution-ShareAlike 4.0 International'
$ExpectedZenodoLicence = 'CC-BY-SA-4.0'

# 55 books of the edition as this repository publishes it, and the token count of all of them.
$ExpectedBooks = 55
$ExpectedTokens = 588579

$staging = Join-Path ([IO.Path]::GetTempPath()) "swete-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Reading the licence at $Repository at $($Commit.Substring(0, 7))"

    $readme = (Invoke-WebRequest -Uri "$Raw/README.md" -UseBasicParsing `
        -Headers @{ 'User-Agent' = 'essenthos' }).Content
    if ($readme -notmatch [regex]::Escape($ExpectedLicence)) {
        throw "$Raw/README.md no longer says `"$ExpectedLicence`" over the data directory. That " +
              "statement is the whole of the permission this text is held under, so nothing was " +
              "replaced. Read what it says now, and record it in Resources/Swete/LICENCE.md, " +
              "before fetching again."
    }

    $upstream = (Invoke-WebRequest -Uri $UpstreamLicence -UseBasicParsing `
        -Headers @{ 'User-Agent' = 'essenthos' }).Content
    if ($upstream -notmatch [regex]::Escape($ExpectedUpstreamLicence)) {
        throw "First1KGreek's license.md no longer states `"$ExpectedUpstreamLicence`". The text " +
              "here is derived from that transcription, so its terms are the ones that reach us " +
              "whatever the repackaging says; nothing was replaced."
    }

    $zenodo = (Invoke-WebRequest -Uri $UpstreamZenodo -UseBasicParsing `
        -Headers @{ 'User-Agent' = 'essenthos' }).Content
    if ($zenodo -notmatch [regex]::Escape($ExpectedZenodoLicence)) {
        throw "First1KGreek's Zenodo record no longer declares $ExpectedZenodoLicence. Two " +
              "statements about the same bytes that disagree is the case a person has to read; " +
              "nothing was replaced."
    }

    Write-Host '  README, First1KGreek license.md and its Zenodo record all say CC BY-SA 4.0'

    $tarball = Join-Path $staging 'lxx-swete.tar.gz'
    Invoke-WebRequest -Uri $Archive -OutFile $tarball -Headers @{ 'User-Agent' = 'essenthos' }

    $unpacked = Join-Path $staging 'unpacked'
    New-Item -ItemType Directory -Force -Path $unpacked | Out-Null
    tar -xzf $tarball -C $unpacked
    if ($LASTEXITCODE -ne 0) {
        throw "The archive did not unpack, so nothing was replaced."
    }

    $root = Get-ChildItem $unpacked -Directory | Select-Object -First 1
    $data = Join-Path $root.FullName 'data'

    $files = Get-ChildItem $data -Filter '*.txt'
    if ($files.Count -ne $ExpectedBooks) {
        throw "The archive holds $($files.Count) books and this edition has $ExpectedBooks as this " +
              "repository publishes it. Either the download is partial or the transcription grew a " +
              "book; nothing was replaced. A new book needs a place in SweteTextSource before it " +
              "can be loaded."
    }

    $tokens = ($files | ForEach-Object { (Get-Content $_.FullName -Encoding UTF8).Count } |
        Measure-Object -Sum).Sum
    if ($tokens -ne $ExpectedTokens) {
        throw "The archive holds $tokens tokens and this edition has $ExpectedTokens. The " +
              "transcription has been corrected since it was read, which is good news and is still " +
              "a change to the text; nothing was replaced. Re-measure, update the counts in " +
              "SweteTests, and move the pinned commit deliberately."
    }

    $target = Join-Path (Resolve-Path $ResourcesPath) 'Swete'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem $target -File | Remove-Item -Force

    # The books, and the two licence statements that govern them, beside the bytes they govern.
    Copy-Item -Path (Join-Path $data '*.txt') -Destination $target -Force
    Copy-Item -Path (Join-Path $root.FullName 'README.md') -Destination $target -Force
    Copy-Item -Path (Join-Path $root.FullName 'COPYING-Code') -Destination $target -Force
    Set-Content -Path (Join-Path $target 'first1kgreek-license.md') -Value $upstream -Encoding UTF8 -NoNewline

    $transcribed = Join-Path $target 'First1KGreek'
    New-Item -ItemType Directory -Force -Path $transcribed | Out-Null
    foreach ($isaiah in $Isaiahs) {
        $staged = Join-Path $staging $isaiah.Target
        Invoke-WebRequest -Uri "$First1KGreekRaw/$($isaiah.Source)" -OutFile $staged `
            -Headers @{ 'User-Agent' = 'essenthos' }
        $bytes = (Get-Item $staged).Length
        if ($bytes -ne $isaiah.Bytes) {
            throw "$($isaiah.Source) is $bytes bytes where it was $($isaiah.Bytes) at $First1KGreekCommit. " +
                  "A pinned file cannot change size; the download is partial or not the file. It was not kept."
        }
        # The header's <licence> element runs over a line break, so the whitespace is folded first.
        $header = ((Get-Content $staged -Raw -Encoding UTF8) -split '</teiHeader>')[0] -replace '\s+', ' '
        if ($header -notmatch [regex]::Escape($ExpectedTeiLicence)) {
            throw "$($isaiah.Source) no longer states `"$ExpectedTeiLicence`" in its header. Read what " +
                  "it says and record it in Resources/Swete/LICENCE.md before keeping it."
        }
        Move-Item -Path $staged -Destination (Join-Path $transcribed $isaiah.Target) -Force
    }
    Write-Host "  Isaiah from First1KGreek at $($First1KGreekCommit.Substring(0, 7)): Swete's and Ottley's, both CC BY-SA 4.0 in their headers"

    $size = (Get-ChildItem $target -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} tokens in {1} books, {2:N1} MB in {3}" -f $tokens, $files.Count, $size, $target)
    Write-Host "Taken from $Repository at $Commit"
    Write-Host "Then run: python scripts/corpus-manifest.py, and commit the manifest."
    Write-Host ("A reload is not automatic: the corpus loader returns early for a text whose slug " +
                "is already in the text table, so a restart alone picks nothing up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
