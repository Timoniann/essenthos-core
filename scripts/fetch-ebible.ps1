<#
.SYNOPSIS
    Fetches the German, Spanish and English Bibles the corpus holds, from eBible.org.

.DESCRIPTION
    Nine texts, one publisher, one format and one set of checks, so they are one script rather
    than nine copies of the same hundred lines. The Kulish Bible arrives the same way and has its
    own script; that one is left where it is.

        Luther1912             deu1912     Lutherbibel 1912, Strong-tagged
        Elberfelder1905        deuelo      unrevidierte Elberfelder 1905
        ReinaValera1909        spaRV1909   Santa Biblia Reina-Valera 1909, Strong-tagged
        Tyndale1534            engtnt      Tyndale's New Testament, original spelling
        Geneva1599             enggnv      the Geneva Bible, original spelling
        AmericanStandard1901   eng-asv     the American Standard Version
        Young1898              engylt      Young's Literal Translation
        WorldEnglish           engwebp     the World English Bible, updated edition
        Jps1917                engjps      the JPS TaNaKH of 1917, Old Testament only

    Every one of them is public domain and says so in three places that agree: the catalogue's
    Copyright and Redistributable columns, the copyright page on the web, and the copy of that page
    shipped inside the archive. All three are checked rather than assumed, and if any of them stops
    saying what it says today this stops and replaces nothing — a licence that moved under us is the
    owner's decision and not a download.

    That is the statement over the text. It is NOT a statement about the Strong tagging that three
    of the nine arrive with: eBible names no tagger and no terms for it anywhere, and the licence
    notes beside the data say so rather than reading eBible's public-domain line as covering a layer
    nobody has claimed.

    The copyright page is kept beside the data as copr.htm, in the archive's own copy of it, because
    a licence that lives only at a URL is one nobody can check offline.

    Only the scripture books are kept. Two of these archives also carry a title page and a preface
    as USFM files of their own, under the codes FRT, INT and GLO; they are the publisher's apparatus
    rather than the text, they use a dozen markers that appear nowhere in scripture, and what they
    say about the edition belongs in LICENCE.md where a person will read it.

    eBible resets long connections, so the archive is fetched with range resume and the zip is
    tested before anything is unpacked. A truncated download that unpacked would look like a
    shortened Bible.

.EXAMPLE
    ./scripts/fetch-ebible.ps1

.EXAMPLE
    ./scripts/fetch-ebible.ps1 -Only Luther1912
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # One folder name, where only one text is wanted. All nine by default.
    [string] $Only
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Catalogue = 'https://ebible.org/Scriptures/translations.csv'

# What the copyright page and the catalogue must say. Two spellings of one claim, because eBible
# writes it in title case on the page and in lower case in the column.
$ExpectedLicence = 'Public Domain'
$ExpectedCatalogueLicence = 'public domain'

# The codes eBible gives the files that are not scripture: a title page, a preface and a glossary.
# They are matched on the \id line rather than on the file name, which carries eBible's own book
# numbering and says nothing about what the file holds.
$NotScripture = @('FRT', 'INT', 'GLO', 'BAK')

# The folder each text is written to, eBible's identifier for it, and the book and verse counts the
# catalogue states. Books and verses are checked after unpacking: a short archive is a partial
# download, not a shorter Bible, and only these two numbers can tell the difference.
#
# The Strong count is what the archive carried when this was written; it is reported rather than
# enforced, because a publisher extending its own tagging is not a failure.
$Texts = @(
    [pscustomobject]@{ Folder = 'Luther1912';           Id = 'deu1912';   Books = 66; Verses = 23145 + 7957; Strongs = 365353 }
    [pscustomobject]@{ Folder = 'Elberfelder1905';      Id = 'deuelo';    Books = 66; Verses = 23145 + 7957; Strongs = 0 }
    [pscustomobject]@{ Folder = 'ReinaValera1909';      Id = 'spaRV1909'; Books = 66; Verses = 23145 + 7957; Strongs = 390758 }
    [pscustomobject]@{ Folder = 'Tyndale1534';          Id = 'engtnt';    Books = 27; Verses = 7957;         Strongs = 0 }
    [pscustomobject]@{ Folder = 'Geneva1599';           Id = 'enggnv';    Books = 66; Verses = 23137 + 7953; Strongs = 0 }
    [pscustomobject]@{ Folder = 'AmericanStandard1901'; Id = 'eng-asv';   Books = 66; Verses = 23145 + 7957; Strongs = 705378 }
    [pscustomobject]@{ Folder = 'Young1898';            Id = 'engylt';    Books = 66; Verses = 23145 + 7957; Strongs = 0 }
    [pscustomobject]@{ Folder = 'WorldEnglish';         Id = 'engwebp';   Books = 66; Verses = 23145 + 7958; Strongs = 683868 }
    [pscustomobject]@{ Folder = 'Jps1917';              Id = 'engjps';    Books = 39; Verses = 23145;        Strongs = 0 }
)

if ($Only) {
    $wanted = $Texts | Where-Object { $_.Folder -eq $Only }
    if (-not $wanted) {
        throw "There is no eBible text called `"$Only`" here. It is one of: " +
              (($Texts | ForEach-Object { $_.Folder }) -join ', ') + '.'
    }

    $Texts = @($wanted)
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "ebible-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

function Get-WithResume {
    param([string] $Uri, [string] $Path)

    # eBible drops long connections partway through. curl is asked to resume rather than restart,
    # and the result is tested as a zip by the caller, which is the only check that actually knows
    # whether the file is whole.
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        try {
            curl.exe --silent --show-error --location --max-time 180 --continue-at - `
                --user-agent 'essenthos' --output $Path $Uri | Out-Null
        }
        catch {
            Write-Host "  attempt $attempt did not finish: $($_.Exception.Message)"
        }

        try {
            $zip = [IO.Compression.ZipFile]::OpenRead($Path)
            $zip.Dispose()
            return
        }
        catch {
            Write-Host "  attempt $attempt left an incomplete archive; resuming"
        }
    }

    throw "$Uri could not be fetched whole in six attempts. Nothing was replaced."
}

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $listing = Join-Path $staging 'translations.csv'
    Invoke-WebRequest -Uri $Catalogue -OutFile $listing -Headers @{ 'User-Agent' = 'essenthos' }
    $catalogue = Import-Csv $listing -Encoding UTF8

    foreach ($text in $Texts) {
        $id = $text.Id
        $copyrightPage = "https://ebible.org/$id/copyright.htm"
        $archive = "https://ebible.org/Scriptures/${id}_usfm.zip"

        Write-Host "$($text.Folder) — reading the licence at $copyrightPage"

        $copyright = (Invoke-WebRequest -Uri $copyrightPage -UseBasicParsing `
            -Headers @{ 'User-Agent' = 'essenthos' }).Content
        if ($copyright -notmatch [regex]::Escape($ExpectedLicence)) {
            throw "$copyrightPage no longer says `"$ExpectedLicence`". That statement is the whole " +
                  "reason this text could be loaded without asking anyone, so nothing was replaced. " +
                  "Read what it says now, and record it in Resources/$($text.Folder)/LICENCE.md, " +
                  'before fetching again.'
        }

        $row = $catalogue | Where-Object { $_.translationId -eq $id }
        if (-not $row) {
            throw "eBible's catalogue no longer lists $id, so there is no second statement of its " +
                  'terms to check the copyright page against; nothing was replaced.'
        }

        if ($row.Copyright -ne $ExpectedCatalogueLicence -or $row.Redistributable -ne 'True') {
            throw "eBible's catalogue now says Copyright=`"$($row.Copyright)`" and " +
                  "Redistributable=`"$($row.Redistributable)`" for $id, where it said " +
                  "`"$ExpectedCatalogueLicence`" and `"True`". Two statements about the same bytes " +
                  'that disagree is the case a person has to read; nothing was replaced.'
        }

        Write-Host "  copyright page and catalogue both say public domain, source files dated $($row.sourceDate)"

        $zipPath = Join-Path $staging "$id.zip"
        Get-WithResume -Uri $archive -Path $zipPath

        $unpacked = Join-Path $staging $id
        Expand-Archive -Path $zipPath -DestinationPath $unpacked

        $books = Get-ChildItem $unpacked -Filter '*.usfm' | Where-Object {
            $code = (Get-Content $_.FullName -TotalCount 1) -replace '^\\id\s+(\S+).*$', '$1'
            $NotScripture -notcontains $code
        }

        if ($books.Count -ne $text.Books) {
            throw "The archive holds $($books.Count) scripture books and this edition has " +
                  "$($text.Books). Either this is a partial download or the edition changed; " +
                  'nothing was replaced.'
        }

        $lines = $books | ForEach-Object { Get-Content $_.FullName -Encoding UTF8 }
        $verses = ($lines | Select-String -Pattern '^\\v ' | Measure-Object).Count
        if ($verses -ne $text.Verses) {
            throw "The archive holds $verses verses and this edition has $($text.Verses). Either " +
                  'this is a partial download or the edition changed; nothing was replaced.'
        }

        $strongs = ($lines | Select-String -Pattern 'strong="' -AllMatches |
            ForEach-Object { $_.Matches.Count } | Measure-Object -Sum).Sum
        if (-not $strongs) { $strongs = 0 }

        # The third statement of the terms, and the one closest to the bytes: the archive's own copy
        # of the copyright page, kept rather than the fetched one.
        if (-not (Test-Path (Join-Path $unpacked 'copr.htm'))) {
            throw 'The archive carries no copr.htm, so the licence would not sit beside the data. ' +
                  'A copy of the terms has to travel with the bytes; nothing was replaced.'
        }

        $root = Join-Path (Resolve-Path $ResourcesPath) $text.Folder
        New-Item -ItemType Directory -Force -Path $root | Out-Null
        Get-ChildItem $root -File -Filter '*.usfm' | Remove-Item -Force

        # The scripture books and the licence, and nothing else the archive carries: eBible ships a
        # stylesheet and its signing keys alongside, which are about eBible's own publishing and not
        # about this text. The corpus fingerprint in MANIFEST.json covers whatever is here, so
        # leaving them in would make a copy differ from this one every time eBible rotated a key.
        $books | Copy-Item -Destination $root -Force
        Copy-Item -Path (Join-Path $unpacked 'copr.htm') -Destination $root -Force

        $size = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
        Write-Host ("  {0:N0} verses in {1} books, {2:N0} Strong tags, {3:N1} MB in {4}" -f `
            $verses, $books.Count, $strongs, $size, $root)
        if ($strongs -ne $text.Strongs) {
            Write-Host ("  it carried {0:N0} when this was written; record the change in LICENCE.md" -f `
                $text.Strongs)
        }
        Write-Host "  taken from $archive, source files dated $($row.sourceDate)"
    }

    Write-Host 'Record the source date in each Resources/<folder>/LICENCE.md if this replaced an earlier fetch.'
    Write-Host 'Then run: python scripts/corpus-manifest.py, and commit the manifest.'
    Write-Host ('A reload is not automatic: the corpus loader returns early for a text whose slug is ' +
                'already in the text table, so a restart alone picks nothing up.')
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
