<#
.SYNOPSIS
    Fetches the Kulish-Nechui-Levytsky-Puliui Bible of 1903, the first complete Ukrainian Bible and
    the only one that is free of anyone's permission.

.DESCRIPTION
    Everything else in Ukrainian is owned by a Bible society, a religious order or a mission, or is
    ShareAlike, which this project cannot take. This one is not: all three translators were dead by
    1918 and the text was printed well before 1929, and eBible.org states Public Domain on the page
    attached to the bytes.

    That statement is checked rather than assumed, in both of the places eBible makes it — the
    copyright page shipped inside the archive, and the Copyright and Redistributable columns of the
    catalogue every eBible text is listed in. If either stops saying what it says today, this stops
    and replaces nothing: a licence that moved under us is the owner's decision and not a download.

    The copyright page is kept beside the data as copr.htm, the way Brenton's is, because a licence
    that lives only at a URL is one nobody can check offline.

    What is not taken is eBible's other Ukrainian text, ukrfb, "Біблія свободи", which its catalogue
    describes as this translation updated. Its 31,082 verses were compared with these line by line
    on 2026-09-06 and 31,050 of them are identical; of the 32 that differ, most are words broken in
    half or letters transposed, and one drops a footnote. It is a copy of this text with scanning
    damage, not a revision of it, so loading it would put a second Ukrainian witness in the corpus
    that witnesses nothing.

.EXAMPLE
    ./scripts/fetch-kulish.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# eBible's identifier for this translation, which is also the identifier the corpus publishes it
# under. It names the Vienna New Testament of 1871 rather than the complete Bible of 1903, but it is
# what the field spells this text with and a slug nobody else uses is a slug nobody types.
$Translation = 'ukr1871'

$CopyrightPage = "https://ebible.org/$Translation/copyright.htm"
$Catalogue = 'https://ebible.org/Scriptures/translations.csv'
$Archive = "https://ebible.org/Scriptures/${Translation}_usfm.zip"

$ExpectedLicence = 'Public Domain'
$ExpectedCatalogueLicence = 'public domain'

# 39 Old Testament books and 27 New, as the catalogue counts them. A short download is a partial one.
$ExpectedBooks = 66
$ExpectedVerses = 23127 + 7955

$staging = Join-Path ([IO.Path]::GetTempPath()) "kulish-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Reading the licence at $CopyrightPage"

    $copyright = (Invoke-WebRequest -Uri $CopyrightPage -UseBasicParsing `
        -Headers @{ 'User-Agent' = 'essenthos' }).Content
    if ($copyright -notmatch [regex]::Escape($ExpectedLicence)) {
        throw "$CopyrightPage no longer says `"$ExpectedLicence`". That statement is the whole " +
              "reason this text could be loaded without asking anyone, so nothing was replaced. " +
              "Read what it says now, and record it in Resources/Kulish/LICENCE.md, before " +
              "fetching again."
    }

    $listing = Join-Path $staging 'translations.csv'
    Invoke-WebRequest -Uri $Catalogue -OutFile $listing -Headers @{ 'User-Agent' = 'essenthos' }
    $row = Import-Csv $listing -Encoding UTF8 |
        Where-Object { $_.translationId -eq $Translation }

    if (-not $row) {
        throw "eBible's catalogue no longer lists $Translation, so there is no second statement " +
              "of its terms to check the copyright page against; nothing was replaced."
    }

    if ($row.Copyright -ne $ExpectedCatalogueLicence -or $row.Redistributable -ne 'True') {
        throw "eBible's catalogue now says Copyright=`"$($row.Copyright)`" and " +
              "Redistributable=`"$($row.Redistributable)`" for $Translation, where it said " +
              "`"$ExpectedCatalogueLicence`" and `"True`". Two statements about the same bytes " +
              "that disagree is the case a person has to read; nothing was replaced."
    }

    Write-Host "  copyright page and catalogue both say public domain, source files dated $($row.sourceDate)"

    $zip = Join-Path $staging "$Translation.zip"
    Invoke-WebRequest -Uri $Archive -OutFile $zip -Headers @{ 'User-Agent' = 'essenthos' }

    $unpacked = Join-Path $staging 'unpacked'
    Expand-Archive -Path $zip -DestinationPath $unpacked

    $books = Get-ChildItem $unpacked -Filter '*.usfm'
    if ($books.Count -ne $ExpectedBooks) {
        throw "The archive holds $($books.Count) books and the complete Bible has $ExpectedBooks. " +
              "Either this is a partial download or the edition changed; nothing was replaced."
    }

    $verses = ($books | ForEach-Object { Get-Content $_.FullName -Encoding UTF8 } |
        Select-String -Pattern '^\\v ' | Measure-Object).Count
    if ($verses -ne $ExpectedVerses) {
        throw "The archive holds $verses verses and this edition has $ExpectedVerses. Either this " +
              "is a partial download or the edition changed; nothing was replaced."
    }

    # The copyright page as it stands today, beside the bytes it governs. copr.htm is the archive's
    # own copy of it and is kept rather than the fetched one, so that what sits beside the data is
    # what eBible shipped with the data.
    if (-not (Test-Path (Join-Path $unpacked 'copr.htm'))) {
        throw "The archive carries no copr.htm, so the licence would not sit beside the data. " +
              "RUL-0105 is what this check is; nothing was replaced."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'Kulish'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Get-ChildItem $root -File -Filter '*.usfm' | Remove-Item -Force

    # The books and the licence, and nothing else the archive carries: eBible ships a stylesheet
    # and its signing keys alongside, which are about eBible's own publishing and not about this
    # text. The corpus fingerprint in MANIFEST.json covers whatever is here, so leaving them in
    # would make a copy differ from this one every time eBible rotated a key.
    Copy-Item -Path (Join-Path $unpacked '*.usfm') -Destination $root -Force
    Copy-Item -Path (Join-Path $unpacked 'copr.htm') -Destination $root -Force

    $size = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} verses in {1} books, {2:N1} MB in {3}" -f $verses, $books.Count, $size, $root)
    Write-Host "Taken from $Archive, source files dated $($row.sourceDate)"
    Write-Host "Record that date in $root\LICENCE.md if this replaced an earlier fetch."
    Write-Host "Then run: python scripts/corpus-manifest.py, and commit the manifest."
    Write-Host ("A reload is not automatic: the corpus loader returns early for a text whose slug " +
                "is already in the text table, so a restart alone picks nothing up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
