<#
.SYNOPSIS
    Fetches Abbott-Smith's Manual Greek Lexicon of the New Testament (1922) as marked up in TEI.

.DESCRIPTION
    The TEI file of the translatable-exegetical-tools project, each entry keyed by its headword and Strong
    number. Pinned to the commit read on 2026-11-22 and checked against its hash; the README, which says the
    lexicon and its markup are in the public domain, is re-read before anything is replaced. The scanned PDF
    in the same repository carries Internet Archive's own restrictions and is not taken.

.EXAMPLE
    ./scripts/fetch-abbott-smith.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Commit = '8c00cb244761aa23659421a8cbbbf7a3b27b7d59'
$Repository = 'https://raw.githubusercontent.com/translatable-exegetical-tools/Abbott-Smith'
$Files = [ordered]@{
    'abbott-smith.tei.xml' = '99821bee411626b4aa3e674040297b54b267eff86cb4f9b7ef69a8bdb18b6f90'
    'README.md'            = '8c67a8c904d8cbf2b66a9a6eaaed19cc5462b24edec3dc07ee689d30140f2c59'
}

$root = Join-Path (Resolve-Path $ResourcesPath) 'AbbottSmith'
if ((Test-Path $root) -and @($Files.Keys | Where-Object { -not (Test-Path (Join-Path $root $_)) }).Count -eq 0) {
    $moved = @($Files.Keys | Where-Object { (Get-FileHash -Algorithm SHA256 (Join-Path $root $_)).Hash.ToLowerInvariant() -ne $Files[$_] })
    if ($moved.Count -eq 0) {
        Write-Host "AbbottSmith is current ($Commit)."
        return
    }
    throw "In $root these differ from the pinned bytes: $($moved -join ', '). Nothing was replaced; move the folder aside to fetch again."
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "abbott-smith-$([guid]::NewGuid().ToString('n'))"
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

    if ((Get-Content -Raw (Join-Path $staging 'README.md')) -notmatch 'is in the public domain') {
        throw 'The README no longer says the lexicon is in the public domain. Read its terms before taking it.'
    }

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Get-ChildItem -Path $staging -File | ForEach-Object { Copy-Item $_.FullName (Join-Path $root $_.Name) -Force }
}
finally {
    Remove-Item -Recurse -Force -Path $staging -ErrorAction SilentlyContinue
}

$total = (Get-ChildItem -Path $root -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("{0:N1} MB in {1}" -f $total, $root)
Write-Host 'Then run: python scripts/corpus-manifest.py --folders AbbottSmith'
