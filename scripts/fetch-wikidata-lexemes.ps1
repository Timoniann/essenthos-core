<#
.SYNOPSIS
    Fetches one immutable Wikidata Lexeme dump for offline lexical research.

.DESCRIPTION
    The dump contains Lexemes, Forms and Senses across languages, including explicit sense-to-sense
    translation claims where contributors supplied them.  It is a source for extracting a compact,
    corpus-specific bilingual prior later; it is not loaded into the corpus and does not assert an
    occurrence-level alignment.
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Snapshot = '20260909'
$Name = "wikidata-$Snapshot-lexemes.json.bz2"
$Url = "https://dumps.wikimedia.org/wikidatawiki/entities/$Snapshot/$Name"
$root = Join-Path (Resolve-Path $ResourcesPath) 'WikidataLexemes'
$staging = Join-Path ([IO.Path]::GetTempPath()) "wikidata-lexemes-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $download = Join-Path $staging $Name
    Write-Host "Fetching Wikidata Lexemes snapshot $Snapshot"
    Invoke-WebRequest -Uri $Url -OutFile $download
    if ((Get-Item $download).Length -lt 400MB) {
        throw "Wikidata returned $((Get-Item $download).Length) bytes, below the reviewed 400 MB minimum; do not retain a partial dump."
    }

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Move-Item -Path $download -Destination (Join-Path $root $Name) -Force
    $hash = (Get-FileHash -Algorithm SHA256 (Join-Path $root $Name)).Hash.ToLowerInvariant()
    $licence = @"
# Wikidata Lexemes snapshot $Snapshot

Source: <$Url>

Downloaded as an immutable compressed JSON export on $(Get-Date -Format 'yyyy-MM-dd').
SHA-256: ``$hash``

## Licence

Wikidata's official database-download page states that all structured data in the main,
Property, Lexeme and EntitySchema namespaces is available under **CC0 1.0**. The official
policy page gives the same statement. This copy is therefore CC0 1.0, subject to the ordinary
Wikidata terms for non-structured site text, which this dataset does not contain.

- <https://www.wikidata.org/wiki/Wikidata:Database_download>
- <https://www.wikidata.org/wiki/Wikidata:Licensing>
- <https://creativecommons.org/publicdomain/zero/1.0/>

## Intended use

The dump is a candidate source for lemma/form/sense and explicit translation relations. It is
not a word-alignment source and is never evidence that two *occurrences* in a verse correspond.
Any extract must preserve its source snapshot, query and confidence/provenance.
"@
    Set-Content -Path (Join-Path $root 'LICENCE.md') -Value $licence -Encoding utf8
}
finally {
    Remove-Item -Recurse -Force -Path $staging -ErrorAction SilentlyContinue
}

Write-Host "Saved $Name and its SHA-256 provenance in $root"
