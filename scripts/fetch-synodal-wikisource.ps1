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

    The Greek additions the Synodal prints inside three canonical books are taken as well, from those
    books' pages, and only they: the Song of the Three in Daniel 3:24-90 and Susanna and Bel as Daniel
    13 and 14 (27-DAG), the additions to Esther it sets in brackets inside six verses (17-ESG, each
    written as the verse's lettered piece), and the Prayer of Manasseh after 2 Chronicles 36, whose
    twelve verses the page letters а to м (79-MAN, numbered 1 to 12). The rest of those books the
    corpus holds from bible4u. See Resources/SynodalWikisource/LICENCE.md.

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

# The canonical books the Synodal prints Greek additions inside, each at the revision it was read at,
# and what it held.
$Additions = @(
    [pscustomobject]@{ Key = 'DAN'; Page = 'Книга пророка Даниила';      Revision = 3679983; Verses = 530 }
    [pscustomobject]@{ Key = 'EST'; Page = 'Книга Есфири';               Revision = 3679964; Verses = 168 }
    [pscustomobject]@{ Key = '2CH'; Page = 'Вторая книга Паралипоменон'; Revision = 4588701; Verses = 834 }
)

# The verses of Esther inside which the Synodal sets the additions, in brackets: Mordecai's dream as a
# verse of its own before 1:1, the king's two letters, the two prayers and Esther's audience.
$EstherAdditions = @('1:1', '3:13', '4:17', '5:1', '5:2', '8:12', '10:3')

# The letters the page numbers the Prayer of Manasseh's verses by, in order.
$ManassehLetters = 'а', 'б', 'в', 'г', 'д', 'е', 'ж', 'з', 'и', 'к', 'л', 'м'

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

# The verses of a canonical book's page, each with the raw text between its template and the next.
# The Prayer of Manasseh's verses are numbered by a letter in brackets, stih=[а].
function Read-Page {
    param([string] $Wikitext)

    $body = $Wikitext
    $end = $body.LastIndexOf('</div>')
    if ($end -ge 0) { $body = $body.Substring(0, $end) }

    $body = Remove-Template -Text $body -Name 'bible parallels'
    $body = Remove-Template -Text $body -Name 'heading'
    $body = $body -replace '(?m)^==[^=].*?==\s*$', ''
    $body = $body -replace '\{\{глава\|\d+\}\}', ''
    $body = $body -replace '<ref[^>]*>.*?</ref>', ''

    $found = [regex]::Matches($body, '\{\{стих\|глава=(?<chapter>\d+)\|стих=(?<verse>[^}]+)\}\}')
    $verses = [Collections.Generic.List[object]]::new()
    for ($i = 0; $i -lt $found.Count; $i++) {
        $from = $found[$i].Index + $found[$i].Length
        $to = if ($i + 1 -lt $found.Count) { $found[$i + 1].Index } else { $body.Length }
        $verses.Add([pscustomobject]@{
            Chapter = [int]$found[$i].Groups['chapter'].Value
            Verse = $found[$i].Groups['verse'].Value.Trim('[', ']')
            Raw = $body.Substring($from, $to - $from)
        })
    }

    return $verses
}

# What the Synodal sets in brackets inside a verse, joined: the words it translates from the Greek.
function Get-Bracketed {
    param([string] $Raw)

    return (([regex]::Matches($Raw, '\[([^\[\]]+)\]') | ForEach-Object { $_.Groups[1].Value }) -join ' ')
}

# A line every word of which the Synodal sets in brackets, as one supplied span.
function Add-Whole {
    param([string] $Line)

    return "\add $((($Line -replace '\\add\*?', ' ') -replace '\s+', ' ' -replace '\s+([,.;:!?»])', '$1').Trim())\add*"
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

    foreach ($page in $Additions) {
        $read = Invoke-Api @{ action = 'parse'; oldid = $page.Revision; prop = 'wikitext' }
        if ($read.parse.title -ne $page.Page) {
            throw "Revision $($page.Revision) is of `"$($read.parse.title)`", not `"$($page.Page)`"; nothing was replaced."
        }

        $verses = Read-Page -Wikitext $read.parse.wikitext
        if ($verses.Count -ne $page.Verses) {
            throw "$($page.Page) holds $($verses.Count) verses at revision $($page.Revision) and held " +
                  "$($page.Verses) when this was written, so the page was read wrongly; nothing was replaced."
        }

        $lines = [Collections.Generic.List[string]]::new()
        $rem = "\rem ru.wikisource: $($page.Page), revision $($page.Revision)"
        switch ($page.Key) {
            'DAN' {
                # The song is one bracketed passage over 3:24-90, and every word of it is the Greek's.
                $lines.Add('\id DAG - Синодальный перевод, ru.wikisource'); $lines.Add($rem)
                $chapter = 0
                foreach ($verse in $verses | Where-Object {
                        ($_.Chapter -eq 3 -and [int]$_.Verse -ge 24 -and [int]$_.Verse -le 90) -or $_.Chapter -ge 13 }) {
                    if ($verse.Chapter -ne $chapter) { $chapter = $verse.Chapter; $lines.Add("\c $chapter"); $lines.Add('\p') }
                    $text = Convert-Line ($verse.Raw -replace '[\[\]]', '')
                    if ($chapter -eq 3) { $text = Add-Whole $text }
                    $lines.Add("\v $($verse.Verse) $text")
                }
            }
            'EST' {
                $lines.Add('\id ESG - Синодальный перевод, ru.wikisource'); $lines.Add($rem)
                $chapter = 0
                $seen = @{}
                foreach ($verse in $verses) {
                    $key = "$($verse.Chapter):$($verse.Verse)"
                    if ($EstherAdditions -notcontains $key -or $seen[$key]) { continue }
                    if ($key -eq '1:1' -and ($verse.Raw.Trim() -notmatch '^\[')) { continue }
                    $seen[$key] = $true
                    $added = Get-Bracketed $verse.Raw
                    if (-not $added) { throw "$($page.Page) $key sets nothing in brackets; nothing was replaced." }
                    if ($verse.Chapter -ne $chapter) { $chapter = $verse.Chapter; $lines.Add("\c $chapter"); $lines.Add('\p') }
                    $lines.Add("\v $($verse.Verse)a $(Add-Whole (Convert-Line $added))")
                }
                if ($seen.Count -ne $EstherAdditions.Count) {
                    throw "$($page.Page) holds additions in $($seen.Count) of the $($EstherAdditions.Count) verses; nothing was replaced."
                }
            }
            '2CH' {
                $lines.Add('\id MAN - Синодальный перевод, ru.wikisource'); $lines.Add($rem)
                $lines.Add('\h Молитва Манассии'); $lines.Add('\c 1'); $lines.Add('\p')
                $prayer = @($verses | Where-Object { $_.Chapter -eq 36 -and $ManassehLetters -contains $_.Verse })
                if ($prayer.Count -ne $ManassehLetters.Count) {
                    throw "$($page.Page) holds $($prayer.Count) lettered verses after 36:23, not $($ManassehLetters.Count); nothing was replaced."
                }
                for ($i = 0; $i -lt $prayer.Count; $i++) {
                    $lines.Add("\v $($i + 1) $(Convert-Line $prayer[$i].Raw)")
                }
            }
        }

        $file = @{ DAN = '27-DAG'; EST = '17-ESG'; '2CH' = '79-MAN' }[$page.Key]
        [IO.File]::WriteAllLines((Join-Path $destination "$file.usfm"), $lines, [Text.UTF8Encoding]::new($false))
        Write-Host ("  {0,-4} additions from {1}" -f $page.Key, $page.Page)
        Start-Sleep -Milliseconds 300
    }

    $size = (Get-ChildItem $destination -File -Filter '*.usfm' | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} verses in {1} books and the additions of {2} more, {3:N1} MB in {4}" -f $total,
        $Books.Count, $Additions.Count, $size, $destination)
    Write-Host 'Then run: python scripts/corpus-manifest.py, and commit the manifest.'
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
