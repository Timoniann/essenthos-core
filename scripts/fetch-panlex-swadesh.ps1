$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# NLTK's immutable gh-pages commit, not its moving package index.
$Commit = '550b6625bcef1f2abff2ff770a5a0d272c9c6b2a'
$ExpectedSha256 = 'dc028da016ba7d5f9bcc39263b0c3dc27bd56025672b18ccaec4578833fe4dff'
$Url = "https://raw.githubusercontent.com/nltk/nltk_data/$Commit/packages/corpora/panlex_swadesh.zip"
$Resource = Join-Path $PSScriptRoot '..\Resources\PanLexSwadesh'
$Archive = Join-Path $Resource 'panlex_swadesh.zip'
$Data = Join-Path $Resource 'data'

New-Item -ItemType Directory -Force -Path $Resource | Out-Null
if (-not (Test-Path -LiteralPath $Archive)) {
    Write-Host 'Fetching the NLTK-distributed PanLex Swadesh snapshot'
    Invoke-WebRequest -UseBasicParsing -Uri $Url -OutFile $Archive
}

$ActualSha256 = (Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash.ToLowerInvariant()
if ($ActualSha256 -ne $ExpectedSha256) {
    throw "Unexpected SHA-256 for $Archive. Expected $ExpectedSha256, got $ActualSha256."
}

if (-not (Test-Path -LiteralPath $Data)) {
    Expand-Archive -LiteralPath $Archive -DestinationPath $Data
}

# Every code span below doubles its backticks. Inside a double-quoted here-string a single backtick
# is PowerShell's escape character, so `$ExpectedSha256 writes the variable's name and `n writes a
# newline -- which is how this file once recorded the literal text "SHA-256 $ExpectedSha256" as the
# only proof of what had been downloaded.
$Fetched = (Get-Item -LiteralPath $Archive).LastWriteTime.ToString('yyyy-MM-dd')
$Licence = @"
# PanLex Swadesh Corpora — NLTK package snapshot

- **Data:** ``panlex_swadesh.zip``, $('{0:N0}' -f (Get-Item -LiteralPath $Archive).Length) bytes, SHA-256 ``$ActualSha256`` — measured on these bytes and equal to the pinned value, or this file would not have been written
- **Pinned source:** NLTK ``nltk_data`` commit ``$Commit``
- **URL:** <$Url>
- **Fetched:** $Fetched
- **Declared licence on the exact NLTK package index:** [CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/)
- **Upstream project:** [PanLex](https://panlex.org/); editor credited by NLTK: Jonathan Pool
- **Scope:** compact Swadesh-207 list corpus, not the complete PanLex database and not a Bible/Strong dictionary.

NLTK's exact package metadata declares the archive CC0. The ``panlex_swadesh`` entry in ``index.xml`` at the pinned commit reads ``license="CC0 1.0 Universal"`` and ``sha256_checksum="$ExpectedSha256"``, so the licence and the bytes come from the same record. PanLex's current general database licence page declares CC BY-NC-SA 4.0; keep both facts in provenance and do not assume this small historical NLTK package represents the current full database.

Attribution retained even though the package is CC0: PanLex / The Long Now Foundation, via NLTK data, with the PanLex citation specified in the archive README.
"@
Set-Content -LiteralPath (Join-Path $Resource 'LICENCE.md') -Value $Licence -Encoding utf8
Write-Host "Verified $Archive ($ActualSha256)"
