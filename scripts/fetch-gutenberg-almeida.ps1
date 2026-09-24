<#
.SYNOPSIS
    Fetches João Ferreira de Almeida's Portuguese Bible in its Lisbon printing of 1911, as Project
    Gutenberg transcribed it (eBook #62383).

.DESCRIPTION
    The Almeida is the name Portuguese readers know, and every Almeida they buy today is a revision
    held by a Bible society. This one is the 1911 reprint of the Revista e Corrigida of 1900, out of
    copyright by any arithmetic: Almeida died in 1691 and the revisers worked in the nineteenth
    century.

    Gutenberg's transcription is taken rather than any other because it is the text Clear Bible's
    Portuguese alignment was numbered over: the alignment names this eBook as its target, and its
    token file reads word for word as this file does.

    The plain-text file is pinned to its hash on 2026-09-25, since Gutenberg revises a transcription
    in place and the reader's rules about the file's layout were written against these bytes. The
    catalogue page is re-read for its statement of the terms before anything is replaced.

.EXAMPLE
    ./scripts/fetch-gutenberg-almeida.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$EBook = 62383
$Text = "https://www.gutenberg.org/ebooks/$EBook.txt.utf-8"
$Page = "https://www.gutenberg.org/ebooks/$EBook"
$Sha256 = 'bf9c6053bfb441d117a2a128d4edec9fbe935f415bc8f7bf4e1f3f9121790215'
$ExpectedStatement = 'Public domain in the USA.'
$ExpectedTranslator = 'Almeida, João Ferreira d'

$staging = Join-Path ([IO.Path]::GetTempPath()) "almeida-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Reading the terms at $Page"
    $catalogue = Join-Path $staging 'gutenberg-62383.html'
    curl.exe --silent --show-error --location --max-time 120 --user-agent 'essenthos' --output $catalogue $Page
    if ($LASTEXITCODE -ne 0) {
        throw "$Page could not be read (curl exit $LASTEXITCODE). Nothing was replaced."
    }

    $flattened = [IO.File]::ReadAllText($catalogue, [Text.Encoding]::UTF8) -replace '\s+', ' '
    foreach ($phrase in @($ExpectedStatement, $ExpectedTranslator)) {
        if (-not $flattened.Contains($phrase)) {
            throw "$Page no longer says `"$phrase`". The decision to take this text was made against " +
                  'that statement, so nothing was replaced. Read what the page says now and record it in ' +
                  'Resources/Almeida1911/LICENCE.md before fetching again.'
        }
    }

    $file = Join-Path $staging "pg$EBook.txt"
    curl.exe --silent --show-error --location --max-time 180 --user-agent 'essenthos' --output $file $Text
    if ($LASTEXITCODE -ne 0) {
        throw "$Text could not be fetched (curl exit $LASTEXITCODE). Nothing was replaced."
    }

    $hash = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256) {
        throw "pg$EBook.txt hashes to $hash, where it hashed to $Sha256 on 2026-09-25. Gutenberg revises " +
              'a transcription in place, and the reader was written against those bytes; read what changed ' +
              'and run the reader''s tests before taking it. Nothing was replaced.'
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'Almeida1911'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -Path $file, $catalogue -Destination $root -Force

    Write-Host "Almeida 1911 in $root, taken from $Text"
    Write-Host 'Then run: python scripts/corpus-manifest.py, and commit the manifest.'
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
