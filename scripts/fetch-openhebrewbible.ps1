<#
.SYNOPSIS
    Fetches the Open Hebrew Bible's mapping of the Chinese Union Version to BHS, and the README its
    licence is stated in.

.DESCRIPTION
    009-BHS-mapping-CUV/CUV-OT-mapped-to-BHS.csv is Eliran Wong's mapping of FHL's Strong-numbered
    spans of the Union Version's Old Testament to the running word numbers of BHS, the numbers his
    King James mapping (Resources/mapping) writes too. The repository has no LICENSE file: its terms
    are stated in its README, CC BY-NC 4.0, so the README is kept beside the data and re-read before
    anything is replaced. Both are pinned to commit 28ae9b2 (2023-05-31) and checked by hash.

.EXAMPLE
    ./scripts/fetch-openhebrewbible.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Commit = '28ae9b2bd340eed4c483482852f2ed3b2bd07919'
$Raw = "https://raw.githubusercontent.com/eliranwong/OpenHebrewBible/$Commit"
$Folder = 'OpenHebrewBible'

$Files = [ordered]@{
    'CUV-OT-mapped-to-BHS.csv' = @{
        Path   = '009-BHS-mapping-CUV/CUV-OT-mapped-to-BHS.csv'
        Sha256 = 'ecd3bff9e49f9d7cb21ef08579e5c8cbd2695ca4d8b5e369699932916e5aa9a1'
    }
    'README.md'                = @{
        Path   = 'README.md'
        Sha256 = '4a84d12f5d2c99d727ca23d1baf29bb4d30a577413ccea2e263b21ab4f152502'
    }
}

# What the README has to go on saying for the decision to stand.
$Phrases = @('creativecommons.org/licenses/by-nc/4.0', 'Eliran Wong', 'Chinese Union Version')

$staging = Join-Path ([IO.Path]::GetTempPath()) "openhebrewbible-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    foreach ($name in $Files.Keys) {
        $path = Join-Path $staging $name
        Invoke-WebRequest -Uri "$Raw/$($Files[$name].Path)" -OutFile $path -Headers @{ 'User-Agent' = 'essenthos' }
        $hash = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
        if ($hash -ne $Files[$name].Sha256) {
            throw "$name hashes to $hash, where it hashed to $($Files[$name].Sha256) at commit $Commit. " +
                  "Nothing was replaced; a pinned commit should not change, so look at what was served."
        }
    }

    $readme = [IO.File]::ReadAllText((Join-Path $staging 'README.md'), [Text.Encoding]::UTF8)
    foreach ($phrase in $Phrases) {
        if (-not $readme.Contains($phrase)) {
            throw "The README no longer says `"$phrase`". Nothing was replaced: read the terms again and " +
                  "record them in Resources/$Folder/LICENCE.md first."
        }
    }

    $target = Join-Path (Resolve-Path $ResourcesPath) $Folder
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    foreach ($name in $Files.Keys) {
        Copy-Item -Path (Join-Path $staging $name) -Destination (Join-Path $target $name) -Force
    }

    $size = (Get-ChildItem $target -File | Where-Object Name -ne 'LICENCE.md' | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N1} MB in {1}" -f $size, $target)
    Write-Host "Then run: python scripts/corpus-manifest.py --folders $Folder"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
