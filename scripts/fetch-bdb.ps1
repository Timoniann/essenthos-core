<#
.SYNOPSIS
    Fetches the Open Scriptures Hebrew Lexicon: Brown-Driver-Briggs as typed so far, and the index joining
    its entries to Strong's numbers.

.DESCRIPTION
    BrownDriverBriggs.xml is the text of Brown, Driver and Briggs's Hebrew and English Lexicon (1906), marked up
    by the Open Scriptures Hebrew Bible project and still being completed: each entry says how far it has got.
    LexicalIndex.xml names the BDB entry behind each Strong number. Both are pinned to the commit read on
    2026-11-22 and checked against their hashes; the readme, which states the terms (CC BY 4.0, the BDB and
    Strong texts themselves public domain), is re-read before anything is replaced.

.EXAMPLE
    ./scripts/fetch-bdb.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Commit = '21c9add13bc727d3a951361778e97e3ff7afd1ce'
$Repository = 'https://raw.githubusercontent.com/openscriptures/HebrewLexicon'
$Files = [ordered]@{
    'BrownDriverBriggs.xml' = '2b52658a4323d91674cda4090ab8b3ebddfff640f4f18143c28300e80b2c38f8'
    'LexicalIndex.xml'      = '8f7a605c58899d2f44430149c143c00903976e1e91232476677972a69e5bc85f'
    'readme.md'             = '9a129c25674387c494571c3828aa3a8eb78459c165e275c313ae26994ce8ff22'
}

$root = Join-Path (Resolve-Path $ResourcesPath) 'BrownDriverBriggs'
if ((Test-Path $root) -and @($Files.Keys | Where-Object { -not (Test-Path (Join-Path $root $_)) }).Count -eq 0) {
    $moved = @($Files.Keys | Where-Object { (Get-FileHash -Algorithm SHA256 (Join-Path $root $_)).Hash.ToLowerInvariant() -ne $Files[$_] })
    if ($moved.Count -eq 0) {
        Write-Host "BrownDriverBriggs is current ($Commit)."
        return
    }
    throw "In $root these differ from the pinned bytes: $($moved -join ', '). Nothing was replaced; move the folder aside to fetch again."
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "bdb-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null
try {
    foreach ($file in $Files.Keys) {
        $destination = Join-Path $staging $file
        Invoke-WebRequest -Uri "$Repository/$Commit/$file" -OutFile $destination -Headers @{ 'User-Agent' = 'essenthos' }
        $hash = (Get-FileHash -Algorithm SHA256 $destination).Hash.ToLowerInvariant()
        if ($hash -ne $Files[$file]) {
            throw "$file hashes to $hash, not the $($Files[$file]) read on 2026-11-22. Nothing was replaced."
        }
    }

    if ((Get-Content -Raw (Join-Path $staging 'readme.md')) -notmatch 'Creative Commons Attribution 4\.0') {
        throw 'The readme no longer states CC BY 4.0. Read its terms before taking the lexicon.'
    }

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Get-ChildItem -Path $staging -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $root $_.Name) -Force }
}
finally {
    Remove-Item -Recurse -Force -Path $staging -ErrorAction SilentlyContinue
}

$total = (Get-ChildItem -Path $root -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("{0:N1} MB in {1}" -f $total, $root)
Write-Host 'Then run: python scripts/corpus-manifest.py --folders BrownDriverBriggs'
