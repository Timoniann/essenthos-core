<#
.SYNOPSIS
    Fetches the 57 Septuagint books of the GLAUx treebank, which give Brenton its lemmas.

.DESCRIPTION
    Brenton's Septuagint is public domain and arrived with no annotation at all. GLAUx annotates a
    different edition of the same book, and 99.4% of Brenton's tokens are written the same way
    somewhere in it, so it is used as a form-to-lemma dictionary against the text we already serve.
    GLAUx's own Greek is never loaded. DOC-0161 has the licence reading in full.

    Licence: CC BY-SA 3.0, which is what metadata.txt states for every Septuagint row and is the
    most restrictive of the three statements GLAUx makes about itself. The corpus attributes it at
    /v1/datasets. The owner accepted share-alike on 2026-09-03.

    111 MB, so it is fetched rather than committed, like etcbc and the Septuagint text beside it.
    The loader warns and does nothing if it is absent, so a checkout without this still runs.

    Which file is which book comes from the TLG column of metadata.txt, not from the GLAUX_TEXT_ID:
    Genesis is text 720 and file 0527-001.xml. Getting that wrong is 57 quiet 404s.

.EXAMPLE
    ./scripts/fetch-glaux.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$raw = 'https://raw.githubusercontent.com/alekkeersmaekers/glaux/main'
$root = Join-Path (Resolve-Path $ResourcesPath) 'Glaux'
$xml = Join-Path $root 'xml'

New-Item -ItemType Directory -Force -Path $xml | Out-Null

$metadata = Join-Path $root 'metadata.txt'
Write-Host "Fetching metadata.txt"
Invoke-WebRequest -Uri "$raw/metadata.txt" -OutFile $metadata

$rows = Import-Csv -Path $metadata -Delimiter "`t"
$books = $rows | Where-Object { $_.AUTHOR_STANDARD -eq 'Septuaginta' }

if ($books.Count -ne 57) {
    throw "metadata.txt names $($books.Count) Septuagint books and DOC-0161 measured 57. Either " +
          "GLAUx has changed or the file did not download whole; check before trusting the result."
}

# @() so that a single distinct value stays an array; indexing a bare string gives a character.
$licences = @($books.SOURCE_LICENSE | Sort-Object -Unique)
if ($licences.Count -ne 1 -or $licences[0] -ne 'CC-BY-SA 3.0') {
    throw "The Septuagint rows now state '$($licences -join ', ')' and the corpus attributes " +
          "CC BY-SA 3.0. A licence that changed under us is a decision, not a download; stop."
}

$done = 0
foreach ($book in $books) {
    $file = Join-Path $xml "$($book.TLG).xml"
    if ((Test-Path $file) -and (Get-Item $file).Length -gt 0) {
        $done++
        continue
    }

    Write-Host "  $($book.TLG)  $($book.TITLE_STANDARD)"
    Invoke-WebRequest -Uri "$raw/xml/$($book.TLG).xml" -OutFile $file
    $done++
}

$total = (Get-ChildItem $xml -Filter *.xml | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("{0} books, {1:N0} MB in {2}" -f $done, $total, $xml)
