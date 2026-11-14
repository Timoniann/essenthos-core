<#
.SYNOPSIS
    Fetches CrossWire's King James Version 3.1: the SWORD module whose New Testament words carry the
    KJV2003 project's Strong numbers and the places of the Greek words they render.

.DESCRIPTION
    The corpus loads the King James's words from bible4u; this module is read only for what it states
    about them. In the New Testament each tagged element names the Greek words it renders twice over:
    by Strong number and by their places in the verse of Maurice Robinson's Textus Receptus
    (src="4 5" beside lemma.TR:ο lemma.TR:θεος). An empty element is a Greek word the English leaves
    untranslated, and <transChange type="added"> is the translators' italics. The Old Testament's
    numbers are the Bible Foundation's; the corpus takes those from eBible's eng-kjv2006.

    The archive is pinned to the bytes read on 2026-11-13 (Version=3.1, SwordVersionDate 2023-07-19,
    published 2023-07-27), on which the coverage and the hand-read precision of PRB-1332 were measured.
    The grant is re-read before anything is replaced: the configuration's DistributionLicense and the
    two sentences of its About that the decision rests on, and the CrossWire wiki's account of whose
    work the numbers and the tagging are. If any of them no longer says so, nothing is replaced.

    Resources/CrossWireKJV/LICENCE.md quotes the statements and what was decided on them.

.EXAMPLE
    ./scripts/fetch-crosswire-kjv.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Archive = 'https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip/KJV.zip'
$ArchiveSha256 = '873815aa4b4123025616d1f41eae75f412111275f4c3884e36f92d4f46dcba1d'
$Folder = 'CrossWireKJV'
$Module = 'KJV'

$License = 'GPL'
$Grant = @(
    'offer the KJV2003 Project text freely for any purpose',
    'grants a general public license to use this text for any purpose'
)

$Wiki = @{
    File    = 'crosswire-kjv-wiki.html'
    Url     = 'https://wiki.crosswire.org/CrossWire_KJV'
    Phrases = @('Bible Foundation', 'Maurice A. Robinson', 'KJV2003 project')
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "crosswire-kjv-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $zip = Join-Path $staging "$Module.zip"
    Invoke-WebRequest -Uri $Archive -OutFile $zip -Headers @{ 'User-Agent' = 'essenthos' }
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $ArchiveSha256) {
        throw "$Module.zip hashes to $hash, where it hashed to $ArchiveSha256 on 2026-11-13. CrossWire has " +
              "published a different module, so nothing was replaced; read its History lines and its " +
              "DistributionLicense and measure the New Testament's positions again before taking it."
    }

    $unpacked = Join-Path $staging $Module
    Expand-Archive -Path $zip -DestinationPath $unpacked -Force
    $conf = Join-Path $unpacked 'mods.d' 'kjv.conf'
    $lines = Get-Content $conf -Encoding UTF8
    $stated = ($lines | Where-Object { $_ -like 'DistributionLicense=*' }) -replace '^DistributionLicense=', ''
    if ($stated -ne $License) {
        throw "KJV states DistributionLicense=$stated, where it stated $License when it was taken. Nothing was " +
              "replaced: read the new terms and record them in Resources/$Folder/LICENCE.md first."
    }

    $flattened = ($lines -join ' ') -replace '\\par', ' ' -replace '\s+', ' '
    foreach ($phrase in $Grant) {
        if (-not $flattened.Contains($phrase)) {
            throw "kjv.conf no longer says `"$phrase`". The decision to show these numbers was made against " +
                  "that sentence, so nothing was replaced. Read what the configuration says now and record it " +
                  "in Resources/$Folder/LICENCE.md before fetching again."
        }
    }

    Write-Host "Reading $($Wiki.Url)"
    $page = Join-Path $staging $Wiki.File
    Invoke-WebRequest -Uri $Wiki.Url -OutFile $page -UseBasicParsing -Headers @{ 'User-Agent' = 'Mozilla/5.0 (essenthos)' }
    $text = [IO.File]::ReadAllText($page, [Text.Encoding]::UTF8) -replace '\s+', ' '
    foreach ($phrase in $Wiki.Phrases) {
        if (-not $text.Contains($phrase)) {
            throw "$($Wiki.Url) no longer says `"$phrase`". Whose work the numbers and the tagging are was read " +
                  "there, so nothing was replaced. Read the page again and record it in " +
                  "Resources/$Folder/LICENCE.md before fetching again."
        }
    }

    $root = Resolve-Path $ResourcesPath
    $target = Join-Path $root $Folder $Module
    if (Test-Path $target) {
        Remove-Item -Recurse -Force $target
    }

    New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
    Copy-Item -Recurse -Path $unpacked -Destination $target
    Copy-Item -Path $page -Destination (Join-Path $root $Folder) -Force
    $size = (Get-ChildItem (Join-Path $root $Folder) -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N1} MB in {1}" -f $size, (Join-Path $root $Folder))
    Write-Host "Then run: python scripts/corpus-manifest.py --folders $Folder"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
