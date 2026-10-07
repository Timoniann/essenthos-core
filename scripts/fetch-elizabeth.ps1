<#
.SYNOPSIS
    Fetches the Church Slavonic Elizabeth Bible as CrossWire publishes it: the SWORD module
    CSlElizabeth 1.5.2, rusbible.ru's electronic text in modernised spelling.

.DESCRIPTION
    The archive is pinned to the bytes read on 2026-10-07 and checked against their hash, and the
    terms are the configuration's own DistributionLicense line, re-read from the archive before
    anything is installed. If either changed, nothing is replaced: a different module or different
    terms are a decision, not a download.

    A folder already holding the module at the pinned version is left alone.

.EXAMPLE
    ./scripts/fetch-elizabeth.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Module = 'CSlElizabeth'
$Folder = 'ElizabethBible'
$Archive = "https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/$Module.zip"
$Sha256 = '96705c572eda109fa42203dfd0fbcece54ecbe8a940e568455f4f4051e698652'
$License = 'Public Domain'
$Version = '1.5.2'

$target = Join-Path $ResourcesPath $Folder $Module
$installed = Join-Path $target 'mods.d' 'cslelizabeth.conf'
if ((Test-Path $installed) -and ((Get-Content $installed -Encoding UTF8) -contains "Version=$Version")) {
    Write-Host "$Module $Version is already in $target."
    return
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "elizabeth-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $zip = Join-Path $staging "$Module.zip"
    Invoke-WebRequest -Uri $Archive -OutFile $zip -Headers @{ 'User-Agent' = 'essenthos' }
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256) {
        throw "$Module.zip hashes to $hash, where it hashed to $Sha256 on 2026-10-07. CrossWire has published a " +
              "different module, so nothing was replaced; read its History lines and its DistributionLicense first."
    }

    $unpacked = Join-Path $staging $Module
    Expand-Archive -Path $zip -DestinationPath $unpacked -Force
    $conf = Get-ChildItem (Join-Path $unpacked 'mods.d') -Filter '*.conf' | Select-Object -First 1
    $stated = (Get-Content $conf.FullName -Encoding UTF8 | Where-Object { $_ -like 'DistributionLicense=*' }) -replace '^DistributionLicense=', ''
    if ($stated -ne $License) {
        throw "$Module states DistributionLicense=$stated, where it stated $License when it was taken. Nothing " +
              "was replaced: read the new terms and record them in its LICENCE.md first."
    }

    if (Test-Path $target) {
        throw "$target holds another version of $Module. Move it aside and run this again."
    }

    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -Recurse -Path $unpacked -Destination $target
    Write-Host "$Module $Version fetched into $target"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
