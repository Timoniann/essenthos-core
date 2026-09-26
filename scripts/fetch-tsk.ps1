<#
.SYNOPSIS
    Fetches the Treasury of Scripture Knowledge as CrossWire publishes it: the SWORD module TSK and
    the configuration that states its terms.

.DESCRIPTION
    The Treasury is the classic set of cross references: some half a million, gathered by Canne,
    Browne, Blayney, Scott and others and published about 1880, with each verse's references filed
    under the word of the verse they bear on. It is public domain by age, and the module says so in
    its own configuration: DistributionLicense=Public Domain.

    The module is pinned to the archive read on 2026-09-27 (version 1.4) and checked against its hash
    then. Its configuration is read before anything is replaced; if it no longer says Public Domain,
    nothing is — the terms moved, and that is the owner's to read, not a download's.

.EXAMPLE
    ./scripts/fetch-tsk.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Archive = 'https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/TSK.zip'
$Sha256 = '6784c7099465995a8e66f02ead82b0bca66603c1bdeaf8332949774b7bfd4293'
$Folder = 'TreasuryOfScriptureKnowledge'
$Licence = 'DistributionLicense=Public Domain'

$staging = Join-Path ([IO.Path]::GetTempPath()) "tsk-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Fetching $Archive"
    $zip = Join-Path $staging 'TSK.zip'
    Invoke-WebRequest -Uri $Archive -OutFile $zip -Headers @{ 'User-Agent' = 'essenthos' }
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256) {
        throw "TSK.zip hashes to $hash, where it hashed to $Sha256 on 2026-09-27. CrossWire has published a " +
              "different module, so nothing was replaced; read its History lines and compare the references " +
              "before taking it."
    }

    $module = Join-Path $staging 'TSK'
    Expand-Archive -Path $zip -DestinationPath $module -Force
    $configuration = Get-Content (Join-Path $module 'mods.d' 'tsk.conf') -Raw
    if (-not $configuration.Contains($Licence)) {
        throw "The module's configuration no longer says $Licence, while the archive hashes as pinned. " +
              "Nothing was replaced; read mods.d/tsk.conf and record what it says in " +
              "Resources/$Folder/LICENCE.md before fetching again."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) $Folder
    foreach ($part in @('mods.d', 'modules')) {
        $target = Join-Path $root $part
        if (Test-Path $target) {
            Remove-Item -Recurse -Force $target
        }
    }

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -Recurse -Path (Join-Path $module '*') -Destination $root

    $size = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N1} MB in {1}" -f $size, $root)
    Write-Host "Then run: python scripts/corpus-manifest.py --folders $Folder"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
