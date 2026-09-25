<#
.SYNOPSIS
    Fetches the eleven non-canonical books of the Russian Synodal Bible from ru.wikisource, the only
    copy of them anybody publishes with a licence that can be read.

.DESCRIPTION
    The corpus serves the Synodal from bible4u, and that file is the sixty-six books and nothing else.
    The Synodal as the church prints it has eleven more, which it marks as non-canonical: the second
    and third books of Ezra, Tobit, Judith, the Wisdom of Solomon, Sirach, the Letter of Jeremiah,
    Baruch and the three books of Maccabees. ru.wikisource transcribes each of them as one page, from
    the Moscow Patriarchate's edition of 2000, with every verse a {{стих|глава=…|стих=…}} template.

    The Synodal's names for the two Ezras are a trap, and the files are named past it: its *Вторая
    книга Ездры* is the Greek 1 Esdras (1ES here, ordinal 68), and its *Третья книга Ездры* is the Latin
    4 Ezra, the King James's 2 Esdras (2ES here, ordinal 69), which the translators took from the Latin
    because no Greek survives.

    Each page is taken at the revision it was read at, so a later edit to the transcription changes
    nothing here until this script is changed to take it. What is written is USFM, one file per book:
    the verse templates become \v, a blank line between verses becomes \p, since the page says its
    paragraphs follow the printed edition, and the words the edition sets in italics, which the
    translators supplied for the sense, become \add spans. The margin cross-references the page draws
    beside a verse are the transcribers' furniture and are dropped. Sirach's prologue has no verse
    number, and stands before the first verse of chapter 1 as the King James sets its own.

    The additions inside Esther and Daniel and the Prayer of Manasseh are not taken: they are inside
    books the corpus already holds from bible4u, numbered there as the King James numbers them. See
    Resources/SynodalWikisource/LICENCE.md.

    The text is public domain: the translators are long dead, and ru.wikisource states it under
    article 1281 of the Civil Code of the Russian Federation on the Synodal's own page. That statement
    is read before anything is written, and if it stops being made this stops and replaces nothing.

.EXAMPLE
    ./scripts/fetch-synodal-wikisource.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Api = 'https://ru.wikisource.org/w/api.php'

# Wikimedia asks that a script identify itself and give a way to be reached.
$Agent = 'essenthos-corpus/1.0 (https://github.com/essenthos; corpus research)'

$Index = 'Библия (Синодальный перевод)'

# The statement over the text, on the Synodal's own page: public domain under article 1281.
$PublicDomain = 'перешло в общественное достояние в России согласно ст. 1281 ГК РФ'

# The index marks each non-canonical book with this template, which is how the church's printing
# marks them too; a book that stops being marked is a page that changed.
$NonCanonicalMark = '{{Oncolor|#ffff80||[['

# Each book at the revision it was read at, its canonical ordinal and USFM code, and what it held.
# A page that comes back holding another number of verses was read wrongly or has changed.
$Books = @(
    [pscustomobject]@{ Ordinal = 68; Code = '1ES'; Page = 'Вторая книга Ездры';                      Revision = 3826849; Verses = 442 }
    [pscustomobject]@{ Ordinal = 70; Code = 'TOB'; Page = 'Книга Товита';                            Revision = 3679976; Verses = 244 }
    [pscustomobject]@{ Ordinal = 71; Code = 'JDT'; Page = 'Книга Иудифи';                            Revision = 3679968; Verses = 340 }
    [pscustomobject]@{ Ordinal = 75; Code = 'WIS'; Page = 'Книга Премудрости Соломона';              Revision = 4586674; Verses = 440 }
    [pscustomobject]@{ Ordinal = 72; Code = 'SIR'; Page = 'Книга Премудрости Иисуса, сына Сирахова'; Revision = 3679970; Verses = 1523 }
    [pscustomobject]@{ Ordinal = 76; Code = 'LJE'; Page = 'Послание Иеремии';                        Revision = 3680014; Verses = 72 }
    [pscustomobject]@{ Ordinal = 67; Code = 'BAR'; Page = 'Книга пророка Варуха';                    Revision = 3679981; Verses = 141 }
    [pscustomobject]@{ Ordinal = 73; Code = '1MA'; Page = 'Первая книга Маккавейская';               Revision = 3680002; Verses = 924 }
    [pscustomobject]@{ Ordinal = 74; Code = '2MA'; Page = 'Вторая книга Маккавейская';               Revision = 3679930; Verses = 556 }
    [pscustomobject]@{ Ordinal = 80; Code = '3MA'; Page = 'Третья книга Маккавейская';               Revision = 3680028; Verses = 180 }
    [pscustomobject]@{ Ordinal = 69; Code = '2ES'; Page = 'Третья книга Ездры';                      Revision = 3826850; Verses = 875 }
)

$PrologueTitle = 'Предисловие'

# The combining acute accent.
$StressMark = [string][char]0x0301

$VerseTemplate = '\{\{стих\|глава=(?<chapter>\d+)\|стих=(?<verse>\d+)\}\}'

function Invoke-Api {
    param([hashtable] $Query)

    $pairs = $Query.GetEnumerator() | ForEach-Object { "$($_.Key)=$([uri]::EscapeDataString([string]$_.Value))" }
    $uri = "$Api`?" + ($pairs -join '&') + '&format=json&formatversion=2'

    for ($attempt = 1; $attempt -le 4; $attempt++) {
        try {
            return (Invoke-WebRequest -Uri $uri -UseBasicParsing -Headers @{ 'User-Agent' = $Agent } `
                -TimeoutSec 120).Content | ConvertFrom-Json
        }
        catch {
            if ($attempt -eq 4) { throw }
            Start-Sleep -Seconds ($attempt * 2)
        }
    }
}

# A template with templates inside it — {{bible parallels|2|{{библия|4Цар|17:3}}.}} — cannot be
# matched by one expression, so it is cut out by counting braces.
function Remove-Template {
    param([string] $Text, [string] $Name)

    $builder = [Text.StringBuilder]::new()
    $at = 0
    while ($true) {
        $start = $Text.IndexOf("{{$Name", $at, [StringComparison]::OrdinalIgnoreCase)
        if ($start -lt 0) {
            [void]$builder.Append($Text.Substring($at))
            return $builder.ToString()
        }

        [void]$builder.Append($Text.Substring($at, $start - $at))
        $depth = 0
        $i = $start
        do {
            if ([string]::CompareOrdinal($Text, $i, '{{', 0, 2) -eq 0) { $depth++; $i += 2 }
            elseif ([string]::CompareOrdinal($Text, $i, '}}', 0, 2) -eq 0) { $depth--; $i += 2 }
            else { $i++ }
        } while ($depth -gt 0 -and $i -lt $Text.Length)

        $at = $i
    }
}

function Read-Book {
    param([string] $Wikitext)

    $body = $Wikitext
    $end = $body.LastIndexOf('</div>')
    if ($end -ge 0) { $body = $body.Substring(0, $end) }
    $open = $body.IndexOf('<div class="indent">')
    if ($open -ge 0) { $body = $body.Substring($open + '<div class="indent">'.Length) }

    $body = Remove-Template -Text $body -Name 'bible parallels'
    $body = $body -replace '(?m)^==[^=].*?==\s*$', ''
    $body = $body -replace '\{\{глава\|\d+\}\}', ''
    $body = $body -replace '<ref[^>]*>.*?</ref>', ''

    if ($body -match '\{\{(?!стих\|)') {
        throw "A template other than a verse remains in the text: `"$($Matches[0])`". Say in this " +
              'script what it is before fetching again; nothing was replaced.'
    }

    $found = [regex]::Matches($body, $VerseTemplate)

    # The prologue's title is the page's heading over it and not a word of it.
    $prologue = $body.Substring(0, $found[0].Index).Trim() -replace "^$PrologueTitle\s*", ''
    $verses = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $found.Count; $i++) {
        $from = $found[$i].Index + $found[$i].Length
        $to = if ($i + 1 -lt $found.Count) { $found[$i + 1].Index } else { $body.Length }
        $raw = $body.Substring($from, $to - $from)

        # A blank line before the next verse ends the paragraph this one closes.
        $closesParagraph = $raw -match '\n\s*\n\s*$'
        $verses.Add([pscustomobject]@{
            Chapter = [int]$found[$i].Groups['chapter'].Value
            Verse = [int]$found[$i].Groups['verse'].Value
            Text = Convert-Line $raw
            ClosesParagraph = $closesParagraph
        })
    }

    return [pscustomobject]@{ Prologue = (Convert-Line $prologue); Verses = $verses }
}

# The page's inline markup as USFM: italics the translators' supplied words, and a square bracket
# in Sirach's prologue a word the translators supplied the same way. The stress marks the
# transcription sets on a few words are an aid to reading aloud, which bible4u's Synodal prints on
# no word at all, so they go.
function Convert-Line {
    param([string] $Raw)

    $text = $Raw -replace "'''''|'''", '' -replace $StressMark, ''
    $text = [regex]::Replace($text, "''(.+?)''", '\add $1\add*')
    $text = [regex]::Replace($text, '\[([^\[\]]+)\]', '\add $1\add*')
    $text = $text -replace '<[^>]+>', ' '
    return ($text -replace '\s+', ' ').Trim()
}

function Write-Usfm {
    param([string] $Path, $Book, $Read, [int] $Revision)

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("\id $($Book.Code) - Синодальный перевод, ru.wikisource")
    $lines.Add("\h $($Book.Page)")
    $lines.Add("\toc1 $($Book.Page)")
    $lines.Add("\rem ru.wikisource: $($Book.Page), revision $Revision")

    $chapter = 0
    $paragraph = $true
    foreach ($verse in $Read.Verses) {
        if ($verse.Chapter -ne $chapter) {
            $chapter = $verse.Chapter
            $lines.Add("\c $chapter")
            $paragraph = $true
            if ($chapter -eq 1 -and $Read.Prologue) {
                $lines.Add("\p $($Read.Prologue)")
            }
        }

        if ($paragraph) { $lines.Add('\p') }
        $lines.Add("\v $($verse.Verse) $($verse.Text)")
        $paragraph = $verse.ClosesParagraph
    }

    [IO.File]::WriteAllLines($Path, $lines, [Text.UTF8Encoding]::new($false))
}

Write-Host "Reading the licence stated on `"$Index`""
$index = Invoke-Api @{ action = 'parse'; page = $Index; prop = 'text|wikitext' }
$rendered = [Net.WebUtility]::HtmlDecode((($index.parse.text -replace '<[^>]+>', ' ') -replace '\s+', ' '))
if ($rendered -notmatch [regex]::Escape($PublicDomain)) {
    throw "ru.wikisource no longer says `"$PublicDomain`" on `"$Index`". That statement is the whole " +
          'basis on which this text is taken, so nothing was replaced. Read what it says now, and ' +
          'record it in Resources/SynodalWikisource/LICENCE.md, before fetching again.'
}

foreach ($book in $Books) {
    if ($index.parse.wikitext -notmatch [regex]::Escape("$NonCanonicalMark$($book.Page)]]")) {
        throw "`"$Index`" no longer marks `"$($book.Page)`" as a non-canonical book, or no longer lists " +
              'it under that title; nothing was replaced.'
    }
}

Write-Host '  the Synodal is stated public domain under article 1281, and the eleven books are marked non-canonical'

$staging = Join-Path ([IO.Path]::GetTempPath()) "synodal-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $total = 0
    foreach ($book in $Books) {
        $page = Invoke-Api @{ action = 'parse'; oldid = $book.Revision; prop = 'wikitext' }
        if ($page.parse.title -ne $book.Page) {
            throw "Revision $($book.Revision) is of `"$($page.parse.title)`", not `"$($book.Page)`"; nothing was replaced."
        }

        $read = Read-Book -Wikitext $page.parse.wikitext
        if ($read.Verses.Count -ne $book.Verses) {
            throw "$($book.Page) holds $($read.Verses.Count) verses at revision $($book.Revision) and held " +
                  "$($book.Verses) when this was written, so the page was read wrongly; nothing was replaced."
        }

        Write-Usfm -Path (Join-Path $staging ('{0:d2}-{1}.usfm' -f $book.Ordinal, $book.Code)) `
            -Book $book -Read $read -Revision $book.Revision
        $total += $read.Verses.Count
        Write-Host ("  {0,-4} {1,5} verses in {2,2} chapters  {3}" -f $book.Code, $read.Verses.Count,
            ($read.Verses | Select-Object -ExpandProperty Chapter -Unique).Count, $book.Page)
        Start-Sleep -Milliseconds 300
    }

    $destination = Join-Path (Resolve-Path $ResourcesPath) 'SynodalWikisource'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem $destination -File -Filter '*.usfm' | Remove-Item -Force
    Copy-Item -Path (Join-Path $staging '*.usfm') -Destination $destination -Force

    $size = (Get-ChildItem $destination -File -Filter '*.usfm' | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} verses in {1} books, {2:N1} MB in {3}" -f $total, $Books.Count, $size, $destination)
    Write-Host 'Then run: python scripts/corpus-manifest.py, and commit the manifest.'
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
