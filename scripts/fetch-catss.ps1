<#
.SYNOPSIS
    Puts CATSS's parallel aligned Hebrew and Greek texts (Oxford Text Archive item 1111) into Resources/CATSS, checked
    against the pinned hashes.

.DESCRIPTION
    The Oxford Text Archive (Literary and Linguistic Data Service) serves item 1111, "Aligned texts of Hebrew and Greek
    Jewish Scriptures (CATSS database: TLG format)", under CC BY-NC-SA 3.0 as four files, file01-1111.txt to
    file04-1111.txt. The site sits behind a bot check that only a browser passes, so this script does not download: a
    person downloads the four files from the item page in a browser and passes their folder as -From. The script checks
    each against the hash the corpus was measured on and copies them in. Resources/CATSS/LICENCE.md records the terms
    and why only this copy may be used, never CCAT's at the University of Pennsylvania.

    A file already in place with the pinned hash is left alone. One with another hash is not replaced: say so and stop.

.EXAMPLE
    ./scripts/fetch-catss.ps1 -From $HOME/Downloads
#>

[CmdletBinding()]
param(
    # The folder holding the four files as the browser saved them.
    [string] $From,

    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'

$ItemPage = 'https://llds.ling-phil.ox.ac.uk/llds/xmlui/handle/20.500.14106/1111'
$Folder = 'CATSS'
$Pinned = [ordered]@{
    'file01-1111.txt' = '39b0eb8157acf509e1f7635095015e86ee110f13abfd429c9833f03f2a335cc8'
    'file02-1111.txt' = '0dc6b00716de5b2b61a0cf2384243bef24d85b0349f7a0f55e57c826af22087a'
    'file03-1111.txt' = '57fcb29df38746d69f559c650ed5c3df3728a608f787c4b404bc6ae0df40be2b'
    'file04-1111.txt' = '3c923b7dde8bfe0076f4a6eb0d08fc216b0033e32b02c55d42ebd522e8797af2'
}

$targetFolder = Join-Path (Resolve-Path $ResourcesPath) $Folder
$missing = @()
foreach ($name in $Pinned.Keys) {
    $target = Join-Path $targetFolder $name
    if (Test-Path $target) {
        $present = (Get-FileHash -Algorithm SHA256 $target).Hash.ToLowerInvariant()
        if ($present -ne $Pinned[$name]) {
            throw "$target hashes to $present, not to the pinned $($Pinned[$name]). Nothing was replaced: find out where it came from."
        }
    }
    else {
        $missing += $name
    }
}

if ($missing.Count -eq 0) {
    Write-Host "$targetFolder already holds the four pinned files of OTA item 1111."
    return
}

if (-not $From) {
    throw "Missing: $($missing -join ', '). Download them in a browser from $ItemPage (the site refuses programs) and run again with -From <their folder>."
}

foreach ($name in $missing) {
    $source = Join-Path $From $name
    if (-not (Test-Path $source)) {
        throw "$source is not there. Download it from $ItemPage."
    }

    $hash = (Get-FileHash -Algorithm SHA256 $source).Hash.ToLowerInvariant()
    if ($hash -ne $Pinned[$name]) {
        throw "$source hashes to $hash, not to the pinned $($Pinned[$name]). Nothing was copied: the archive's file differs from the one the corpus was measured on."
    }
}

New-Item -ItemType Directory -Force -Path $targetFolder | Out-Null
foreach ($name in $missing) {
    Copy-Item -Path (Join-Path $From $name) -Destination (Join-Path $targetFolder $name)
}

Write-Host "$($missing.Count) file(s) copied into $targetFolder."
Write-Host "Then run: python scripts/corpus-manifest.py --folders $Folder"
