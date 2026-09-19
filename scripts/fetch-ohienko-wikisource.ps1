<#
.SYNOPSIS
    Fetches Ohienko's Ukrainian Bible from uk.wikisource, which is the only complete machine-readable
    copy of it anybody publishes.

.DESCRIPTION
    The corpus serves Ohienko from bible4u, and that file belongs to a digitisation that lost text:
    its Psalm 7 has seventeen verses where Ohienko printed eighteen, and the second one — *Господи,
    Боже мій, — я до Тебе вдаюся* — is in no verse of it at all. The two families can be told apart
    by a letter: every copy missing the line spells the psalm's title *Жалібна … Куща*, every
    complete one spells it *Жалобна … Куша*.

    uk.wikisource's copy is proofread against a scan of the 1988 printing, page by page, and is
    complete: all sixty-six books, and every verse carries an anchor of the form `id="chapter:verse"`
    that this script reads. What is written here is that text, as USFM, one file per book.

    **It is not in the corpus's numbering and must not be loaded as though it were.** Two things
    differ, both of them measured rather than assumed:

      - The Old Testament follows the Hebrew chapter and verse divisions, not the English ones.
        131 verses stand at an address the loaded text gives to another — Ohienko's Genesis ends
        chapter 31 at verse 54 and opens 32 with what the English calls 31:55, and Exodus, Leviticus,
        Numbers and eleven more books do the same.
      - The Psalms are numbered as the Septuagint numbers them. The page titles say so — `90 (91)`
        is the psalm the corpus calls 91 — and 137 of the 150 pages carry such a bracket. One page,
        `113 (114, 115)`, is two psalms of the Masoretic text in one.

    So the psalms are written under the number their own page carries, and every chapter records the
    page it came from in a `\rem` line, which is a comment and not text. Placing this edition in the
    shared frame would need a versification for it, and that is a piece of work rather than a fetch.

    What the corpus takes from it today is one verse. See Resources/OhienkoWikisource/LICENCE.md.

    The licence is **CC BY-SA 4.0**, which is Wikimedia's for every page of it, and unlike everything
    else fetched here that is a licence with conditions rather than a statement that there are none.
    The API is asked for the licence Wikimedia currently states before anything is written, and if it
    stops saying Attribution-ShareAlike 4.0 this stops and replaces nothing.

.EXAMPLE
    ./scripts/fetch-ohienko-wikisource.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Api = 'https://uk.wikisource.org/w/api.php'

# Wikimedia asks that a script identify itself and give a way to be reached.
$Agent = 'essenthos-corpus/1.0 (https://github.com/essenthos; corpus research)'

$ExpectedLicence = 'CC BY-SA 4.0'

# The printed book these pages were proofread against, on Commons. Its file page is where the
# licence and the permission ticket over the translation are stated.
$Scan = 'Ivan Ohienko Bible.djvu'

$Root = 'Біблія (Огієнко)'
$OldTestament = "$Root/Книги Старого Заповіту"
$NewTestament = "$Root/Новий Заповіт"
$PsalmsPrefix = "$OldTestament/Книга Псалмів/"

# The whole Bible is 31,168 verses on these pages. A page that comes back short is a page whose
# transcription changed, so each book states what it held when this was written.
$Books = @(
    [pscustomobject]@{ Ordinal = 1;  Code = 'GEN'; Page = "$OldTestament/Перша книга Мойсеєва: Буття";                    Verses = 1533 }
    [pscustomobject]@{ Ordinal = 2;  Code = 'EXO'; Page = "$OldTestament/Друга книга Мойсеєва: Вихід";                    Verses = 1213 }
    [pscustomobject]@{ Ordinal = 3;  Code = 'LEV'; Page = "$OldTestament/Третя книга Мойсеєва: Левит";                    Verses = 859 }
    [pscustomobject]@{ Ordinal = 4;  Code = 'NUM'; Page = "$OldTestament/Четверта книга Мойсеєва: Числа";                 Verses = 1289 }
    [pscustomobject]@{ Ordinal = 5;  Code = 'DEU'; Page = "$OldTestament/П'ята книга Мойсеєва: Повторення Закону";        Verses = 959 }
    [pscustomobject]@{ Ordinal = 6;  Code = 'JOS'; Page = "$OldTestament/Книга Ісуса Навина (Книга Єгошуї)";              Verses = 658 }
    [pscustomobject]@{ Ordinal = 7;  Code = 'JDG'; Page = "$OldTestament/Книга Суддів";                                   Verses = 618 }
    [pscustomobject]@{ Ordinal = 8;  Code = 'RUT'; Page = "$OldTestament/Книга Рут";                                      Verses = 85 }
    [pscustomobject]@{ Ordinal = 9;  Code = '1SA'; Page = "$OldTestament/Перша книга Самуїлова (або Перша книга царів)";  Verses = 811 }
    [pscustomobject]@{ Ordinal = 10; Code = '2SA'; Page = "$OldTestament/Друга книга Самуїлова (або Друга книга царів)";  Verses = 695 }
    [pscustomobject]@{ Ordinal = 11; Code = '1KI'; Page = "$OldTestament/Перша книга царів";                              Verses = 817 }
    [pscustomobject]@{ Ordinal = 12; Code = '2KI'; Page = "$OldTestament/Друга книга царів";                              Verses = 719 }
    [pscustomobject]@{ Ordinal = 13; Code = '1CH'; Page = "$OldTestament/Перша книга хроніки";                            Verses = 942 }
    [pscustomobject]@{ Ordinal = 14; Code = '2CH'; Page = "$OldTestament/Друга книга хроніки";                            Verses = 821 }
    [pscustomobject]@{ Ordinal = 15; Code = 'EZR'; Page = "$OldTestament/Книга Ездри";                                    Verses = 280 }
    [pscustomobject]@{ Ordinal = 16; Code = 'NEH'; Page = "$OldTestament/Книга Неемії";                                   Verses = 406 }
    [pscustomobject]@{ Ordinal = 17; Code = 'EST'; Page = "$OldTestament/Книга Естер";                                    Verses = 167 }
    [pscustomobject]@{ Ordinal = 18; Code = 'JOB'; Page = "$OldTestament/Книга Йова";                                     Verses = 1069 }
    [pscustomobject]@{ Ordinal = 20; Code = 'PRO'; Page = "$OldTestament/Книга приказок Соломонових";                     Verses = 915 }
    [pscustomobject]@{ Ordinal = 21; Code = 'ECC'; Page = "$OldTestament/Книга Екклезіястова (або Проповідника)";         Verses = 222 }
    [pscustomobject]@{ Ordinal = 22; Code = 'SNG'; Page = "$OldTestament/Пісня над піснями";                              Verses = 117 }
    [pscustomobject]@{ Ordinal = 23; Code = 'ISA'; Page = "$OldTestament/Книга пророка Ісаї";                             Verses = 1292 }
    [pscustomobject]@{ Ordinal = 24; Code = 'JER'; Page = "$OldTestament/Книга пророка Єремії";                           Verses = 1363 }
    [pscustomobject]@{ Ordinal = 25; Code = 'LAM'; Page = "$OldTestament/Плач Єремії";                                    Verses = 154 }
    [pscustomobject]@{ Ordinal = 26; Code = 'EZK'; Page = "$OldTestament/Книга пророка Єзекіїля";                         Verses = 1273 }
    [pscustomobject]@{ Ordinal = 27; Code = 'DAN'; Page = "$OldTestament/Книга пророка Даниїла";                          Verses = 357 }
    [pscustomobject]@{ Ordinal = 28; Code = 'HOS'; Page = "$OldTestament/Книга пророка Осії";                             Verses = 197 }
    [pscustomobject]@{ Ordinal = 29; Code = 'JOL'; Page = "$OldTestament/Книга пророка Йоіла";                            Verses = 73 }
    [pscustomobject]@{ Ordinal = 30; Code = 'AMO'; Page = "$OldTestament/Книга пророка Амоса";                            Verses = 146 }
    [pscustomobject]@{ Ordinal = 31; Code = 'OBA'; Page = "$OldTestament/Книга пророка Овдія";                            Verses = 21 }
    [pscustomobject]@{ Ordinal = 32; Code = 'JON'; Page = "$OldTestament/Книга пророка Йони";                             Verses = 48 }
    [pscustomobject]@{ Ordinal = 33; Code = 'MIC'; Page = "$OldTestament/Книга пророка Михея";                            Verses = 105 }
    [pscustomobject]@{ Ordinal = 34; Code = 'NAM'; Page = "$OldTestament/Книга пророка Наума";                            Verses = 47 }
    [pscustomobject]@{ Ordinal = 35; Code = 'HAB'; Page = "$OldTestament/Книга пророка Авакума";                          Verses = 56 }
    [pscustomobject]@{ Ordinal = 36; Code = 'ZEP'; Page = "$OldTestament/Книга пророка Софонії";                          Verses = 53 }
    [pscustomobject]@{ Ordinal = 37; Code = 'HAG'; Page = "$OldTestament/Книга пророка Огія";                             Verses = 38 }
    [pscustomobject]@{ Ordinal = 38; Code = 'ZEC'; Page = "$OldTestament/Книга пророка Захарія";                          Verses = 211 }
    [pscustomobject]@{ Ordinal = 39; Code = 'MAL'; Page = "$OldTestament/Книга пророка Малахії";                          Verses = 55 }
    [pscustomobject]@{ Ordinal = 40; Code = 'MAT'; Page = "$NewTestament/Євангелія від св. Матвія";                       Verses = 1071 }
    [pscustomobject]@{ Ordinal = 41; Code = 'MRK'; Page = "$NewTestament/Євангелія від св. Марка";                        Verses = 678 }
    [pscustomobject]@{ Ordinal = 42; Code = 'LUK'; Page = "$NewTestament/Євангелія від св. Луки";                         Verses = 1151 }
    [pscustomobject]@{ Ordinal = 43; Code = 'JHN'; Page = "$NewTestament/Євангелія від св. Івана";                        Verses = 879 }
    [pscustomobject]@{ Ordinal = 44; Code = 'ACT'; Page = "$NewTestament/Дії святих апостолів";                           Verses = 1006 }
    [pscustomobject]@{ Ordinal = 45; Code = 'ROM'; Page = "$NewTestament/Послання св. апостола Павла до римлян";          Verses = 433 }
    [pscustomobject]@{ Ordinal = 46; Code = '1CO'; Page = "$NewTestament/Перше послання св. апостола Павла до коринтян";  Verses = 437 }
    [pscustomobject]@{ Ordinal = 47; Code = '2CO'; Page = "$NewTestament/Друге послання св. апостола Павла до коринтян";  Verses = 256 }
    [pscustomobject]@{ Ordinal = 48; Code = 'GAL'; Page = "$NewTestament/Послання св. апостола Павла до галатів";         Verses = 149 }
    [pscustomobject]@{ Ordinal = 49; Code = 'EPH'; Page = "$NewTestament/Послання св. апостола Павла до ефесян";          Verses = 155 }
    [pscustomobject]@{ Ordinal = 50; Code = 'PHP'; Page = "$NewTestament/Послання св. апостола Павла до филип'ян";        Verses = 104 }
    [pscustomobject]@{ Ordinal = 51; Code = 'COL'; Page = "$NewTestament/Послання св. апостола Павла до колосян";         Verses = 95 }
    [pscustomobject]@{ Ordinal = 52; Code = '1TH'; Page = "$NewTestament/Перше послання св. апостола Павла до солунян";   Verses = 89 }
    [pscustomobject]@{ Ordinal = 53; Code = '2TH'; Page = "$NewTestament/Друге послання св. апостола Павла до солунян";   Verses = 47 }
    [pscustomobject]@{ Ordinal = 54; Code = '1TI'; Page = "$NewTestament/Перше послання св. апостола Павла до Тимофія";   Verses = 113 }
    [pscustomobject]@{ Ordinal = 55; Code = '2TI'; Page = "$NewTestament/Друге послання св. апостола до Тимофія";         Verses = 83 }
    [pscustomobject]@{ Ordinal = 56; Code = 'TIT'; Page = "$NewTestament/Послання св. апостола Павла до Тита";            Verses = 46 }
    [pscustomobject]@{ Ordinal = 57; Code = 'PHM'; Page = "$NewTestament/Послання св. апостола Павла до Филимона";        Verses = 25 }
    [pscustomobject]@{ Ordinal = 58; Code = 'HEB'; Page = "$NewTestament/Послання до євреїв";                             Verses = 303 }
    [pscustomobject]@{ Ordinal = 59; Code = 'JAS'; Page = "$NewTestament/Соборне послання св. апостола Якова";            Verses = 108 }
    [pscustomobject]@{ Ordinal = 60; Code = '1PE'; Page = "$NewTestament/Перше соборне послання св. апостола Петра";      Verses = 105 }
    [pscustomobject]@{ Ordinal = 61; Code = '2PE'; Page = "$NewTestament/Друге соборне послання св. апостола Петра";      Verses = 61 }
    [pscustomobject]@{ Ordinal = 62; Code = '1JN'; Page = "$NewTestament/Перше соборне послання св. апостола Івана";      Verses = 105 }
    [pscustomobject]@{ Ordinal = 63; Code = '2JN'; Page = "$NewTestament/Друге соборне послання св. апостола Івана";      Verses = 13 }
    [pscustomobject]@{ Ordinal = 64; Code = '3JN'; Page = "$NewTestament/Третє соборне послання св. апостола Івана";      Verses = 15 }
    [pscustomobject]@{ Ordinal = 65; Code = 'JUD'; Page = "$NewTestament/Соборне послання св. апостола Юди";              Verses = 25 }
    [pscustomobject]@{ Ordinal = 66; Code = 'REV'; Page = "$NewTestament/Об'явлення св. Івана Богослова";                 Verses = 405 }
)

# The psalms as the Hebrew counts them, a superscription being a verse: the same 2,527 BHSA holds.
$PsalmsVerses = 2527

function Get-Page {
    param([string] $Title, [string] $Property)

    $query = [ordered]@{
        action = 'parse'; page = $Title; prop = $Property; format = 'json'; formatversion = '2'
    }
    $pairs = $query.GetEnumerator() | ForEach-Object { "$($_.Key)=$([uri]::EscapeDataString([string]$_.Value))" }
    $uri = "$Api`?" + ($pairs -join '&')

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

# Everything the transclusion puts on the page that is not the text: the scan's page markers, the
# editorial headings the printed book sets in italics between verses, and the footnote callers. They
# stand between one verse anchor and the next, so leaving them would append a heading to the verse
# before it rather than drop it.
$Furniture = @(
    '<span[^>]*class="[^"]*pagenum[^"]*"[^>]*>.*?</span>'
    '<sup[^>]*class="[^"]*reference[^"]*"[^>]*>.*?</sup>'
    '<div[^>]*class="[^"]*tiInherit[^"]*"[^>]*>.*?</div>'
    '<table[^>]*>.*?</table>'
)

function Read-Verses {
    param([string] $Html)

    # The footnote list and the navigation box come after the text and hold the same kind of markup.
    foreach ($marker in '<div class="mw-references-wrap', '<ol class="references') {
        $cut = $Html.IndexOf($marker)
        if ($cut -ge 0) { $Html = $Html.Substring(0, $cut) }
    }

    foreach ($pattern in $Furniture) {
        $Html = [regex]::Replace($Html, $pattern, ' ', 'Singleline')
    }

    $anchors = [regex]::Matches($Html, '<span[^>]*\sid="(?<chapter>\d+):(?<verse>\d+)"[^>]*>')
    $verses = [ordered]@{}
    for ($i = 0; $i -lt $anchors.Count; $i++) {
        $from = $anchors[$i].Index + $anchors[$i].Length
        $to = if ($i + 1 -lt $anchors.Count) { $anchors[$i + 1].Index } else { $Html.Length }

        $text = [System.Net.WebUtility]::HtmlDecode(($Html.Substring($from, $to - $from) -replace '<[^>]+>', ' '))
        # The anchor wraps the printed verse number, which is not a word of the verse.
        $text = ($text -replace '^\s*\d+\s*', '') -replace '\s+', ' '
        $verses["$($anchors[$i].Groups['chapter'].Value):$($anchors[$i].Groups['verse'].Value)"] = $text.Trim()
    }

    return $verses
}

function Write-Usfm {
    param([string] $Path, [string] $Code, [string] $Name, $Verses, [hashtable] $Pages)

    $lines = [Collections.Generic.List[string]]::new()
    $lines.Add("\id $Code")
    $lines.Add("\h $Name")

    $chapter = -1
    foreach ($address in $Verses.Keys) {
        $parts = $address.Split(':')
        if ([int]$parts[0] -ne $chapter) {
            $chapter = [int]$parts[0]
            $lines.Add("\c $chapter")
            if ($Pages -and $Pages.ContainsKey($chapter)) {
                $lines.Add("\rem uk.wikisource page: $($Pages[$chapter])")
            }
        }

        $lines.Add("\v $($parts[1]) $($Verses[$address])")
    }

    [IO.File]::WriteAllLines($Path, $lines, [Text.UTF8Encoding]::new($false))
}

# The statement closest to the bytes: the scan these pages were proofread against, whose file page
# on Commons carries the licence and the permission ticket the translation was released under.
Write-Host "Reading the licence stated for $Scan"
$file = (Invoke-WebRequest -UseBasicParsing -Headers @{ 'User-Agent' = $Agent } -Uri (
    "$Api`?action=query&titles=$([uri]::EscapeDataString("Файл:$Scan"))" +
    '&prop=imageinfo&iiprop=extmetadata&format=json&formatversion=2')).Content | ConvertFrom-Json
$metadata = $file.query.pages[0].imageinfo[0].extmetadata

if ($metadata.LicenseShortName.value -ne $ExpectedLicence) {
    throw "Commons now states `"$($metadata.LicenseShortName.value)`" for $Scan, where it said " +
          "`"$ExpectedLicence`". That statement is the whole basis on which this text may be copied, " +
          'so nothing was replaced. Read what it says now, and record it in ' +
          'Resources/OhienkoWikisource/LICENCE.md, before fetching again.'
}

if ($metadata.Categories.value -notmatch 'VRTS permission confirmed') {
    throw "Commons no longer records a confirmed permission ticket for $Scan. The translation is in " +
          'copyright and the ticket is what releases it; nothing was replaced.'
}

# And the statement over the transcription itself, which is a second work over the first: every page
# of uk.wikisource is contributed under the site's own licence, and the site states which.
$rights = (Invoke-WebRequest -Uri "$Api`?action=query&meta=siteinfo&siprop=rightsinfo&format=json&formatversion=2" `
    -UseBasicParsing -Headers @{ 'User-Agent' = $Agent }).Content | ConvertFrom-Json
if ($rights.query.rightsinfo.text -notmatch 'Attribution-Share Alike 4\.0') {
    throw "uk.wikisource now states its content licence as `"$($rights.query.rightsinfo.text)`" at " +
          "$($rights.query.rightsinfo.url), where it said Creative Commons Attribution-Share Alike " +
          "4.0. Two statements about the same bytes that disagree is the case a person has to read; " +
          'nothing was replaced.'
}

Write-Host "  the scan is $($metadata.LicenseShortName.value) with a confirmed permission ticket"
Write-Host "  the site states $($rights.query.rightsinfo.text) at $($rights.query.rightsinfo.url)"

$staging = Join-Path ([IO.Path]::GetTempPath()) "ohienko-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $total = 0
    foreach ($book in $Books) {
        $page = Get-Page -Title $book.Page -Property 'text'
        $verses = Read-Verses -Html $page.parse.text

        if ($verses.Count -ne $book.Verses) {
            throw "$($book.Page) holds $($verses.Count) verses and held $($book.Verses) when this was " +
                  'written. Either the transcription changed or the page was read wrongly; nothing ' +
                  'was replaced. Check the page, then correct the count in this script.'
        }

        $name = $book.Page.Split('/')[-1]
        Write-Usfm -Path (Join-Path $staging ('{0:d2}-{1}.usfm' -f $book.Ordinal, $book.Code)) `
            -Code $book.Code -Name $name -Verses $verses -Pages $null
        $total += $verses.Count
        Write-Host ("  {0,-4} {1,5} verses  {2}" -f $book.Code, $verses.Count, $name)
        Start-Sleep -Milliseconds 300
    }

    # The Psalms are one page per psalm, and their titles are the Septuagint numbering with the
    # Masoretic one in brackets. Both are kept: the number is the page's own, and the page it came
    # from stands beside the chapter, so nothing here decides which psalm of the corpus it is.
    $titles = (Invoke-WebRequest -UseBasicParsing -Headers @{ 'User-Agent' = $Agent } -Uri (
        "$Api`?action=query&list=allpages&apprefix=$([uri]::EscapeDataString($PsalmsPrefix))" +
        '&aplimit=500&format=json&formatversion=2')).Content | ConvertFrom-Json

    $psalms = [ordered]@{}
    $pages = @{}
    foreach ($leaf in ($titles.query.allpages.title |
            ForEach-Object { $_.Substring($PsalmsPrefix.Length) } |
            Sort-Object { [int]($_ -split ' ')[0] })) {
        $number = [int]($leaf -split ' ')[0]
        $pages[$number] = $leaf
        foreach ($entry in (Read-Verses -Html (Get-Page -Title "$PsalmsPrefix$leaf" -Property 'text').parse.text).GetEnumerator()) {
            $psalms[$entry.Key] = $entry.Value
        }
        Start-Sleep -Milliseconds 250
    }

    if ($psalms.Count -ne $PsalmsVerses) {
        throw "The psalm pages hold $($psalms.Count) verses and held $PsalmsVerses when this was " +
              'written; nothing was replaced.'
    }

    Write-Usfm -Path (Join-Path $staging '19-PSA.usfm') -Code 'PSA' -Name 'Книга Псалмів' `
        -Verses $psalms -Pages $pages
    $total += $psalms.Count
    Write-Host ("  {0,-4} {1,5} verses  Книга Псалмів ({2} pages)" -f 'PSA', $psalms.Count, $pages.Count)

    $destination = Join-Path (Resolve-Path $ResourcesPath) 'OhienkoWikisource'
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem $destination -File -Filter '*.usfm' | Remove-Item -Force
    Copy-Item -Path (Join-Path $staging '*.usfm') -Destination $destination -Force

    $size = (Get-ChildItem $destination -File -Filter '*.usfm' | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} verses in 66 books, {1:N1} MB in {2}" -f $total, $size, $destination)
    Write-Host "Taken from `"$Root`" on uk.wikisource under $ExpectedLicence — see the LICENCE.md there,"
    Write-Host 'which names what that licence obliges. Record today''s date there if this replaced an earlier fetch.'
    Write-Host 'Then run: python scripts/corpus-manifest.py, and commit the manifest.'
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
