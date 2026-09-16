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

$Licence = @"
# PanLex Swadesh Corpora — NLTK package snapshot

- **Data:** `panlex_swadesh.zip`, SHA-256 `$ExpectedSha256`
- **Pinned source:** NLTK `nltk_data` commit `$Commit`
- **URL:** `$Url`
- **Declared licence on the exact NLTK package index:** [CC0 1.0 Universal](https://creativecommons.org/publicdomain/zero/1.0/)
- **Upstream project:** [PanLex](https://panlex.org/); editor credited by NLTK: Jonathan Pool
- **Scope:** compact Swadesh-207 list corpus, not the complete PanLex database and not a Bible/Strong dictionary.

NLTK's exact package metadata declares the archive CC0. PanLex's current general database licence page declares CC BY-NC-SA 4.0; keep both facts in provenance and do not assume this small historical NLTK package represents the current full database.

Attribution retained even though the package is CC0: PanLex / The Long Now Foundation, via NLTK data, with the PanLex citation specified in the archive README.
"@
Set-Content -LiteralPath (Join-Path $Resource 'LICENCE.md') -Value $Licence -Encoding utf8
Write-Host "Verified $Archive ($ActualSha256)"
