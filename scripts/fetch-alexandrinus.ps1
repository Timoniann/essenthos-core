<#
.SYNOPSIS
    Fetches Codex Alexandrinus: INTF's transcription of its New Testament, CNTR's transcription as
    the check on it, and the books in which Swete prints it.

.DESCRIPTION
    Each licence is checked, not assumed. INTF's file must still state Creative Commons Attribution
    4.0 in its own header, CNTR's README Attribution-ShareAlike 4.0, and First1KGreek's licence file
    Attribution-ShareAlike 4.0; if any statement changed, this stops and replaces nothing, because a
    licence that changed under us is the owner's decision and not a download.

.EXAMPLE
    ./scripts/fetch-alexandrinus.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # The First1KGreek commit Swete's books are read at.
    [string] $First1KGreek = '8ee111eb44ecef4120c844e10749178d95d1f30c',

    # The CNTR transcriptions commit.
    [string] $Cntr = '4c0e9f94117ec3dc4ae40094aec044bb7a416a53'
)

$ErrorActionPreference = 'Stop'

$Ntvmr = 'https://ntvmr.uni-muenster.de/community/vmr/api/transcript/get/?docID=20002&pageID=ALL&format=teiraw'
$CntrRaw = "https://raw.githubusercontent.com/Center-for-New-Testament-Restoration/transcriptions/$Cntr"
$SweteRaw = "https://raw.githubusercontent.com/OpenGreekAndLatin/First1KGreek/$First1KGreek"

# Genesis, 1-4 Maccabees and the Odes: the books Swete prints from Alexandrinus.
$Works = @('001', '023', '024', '025', '026', '028')

$target = Join-Path $ResourcesPath 'Alexandrinus'
$staging = Join-Path ([IO.Path]::GetTempPath()) "alexandrinus-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'swete') | Out-Null

try {
    Invoke-WebRequest -Uri $Ntvmr -OutFile (Join-Path $staging 'ntvmr-02.xml')
    if ((Get-Content (Join-Path $staging 'ntvmr-02.xml') -Raw) -notmatch 'Creative Commons Attribution 4\.0') {
        throw "INTF's transcription no longer states Creative Commons Attribution 4.0 in its header. Nothing was replaced."
    }

    Invoke-WebRequest -Uri "$CntrRaw/class%201/02.txt" -OutFile (Join-Path $staging 'cntr-02.txt')
    Invoke-WebRequest -Uri "$CntrRaw/README.md" -OutFile (Join-Path $staging 'cntr-README.md')
    if ((Get-Content (Join-Path $staging 'cntr-README.md') -Raw) -notmatch 'CC BY-SA 4\.0') {
        throw "CNTR's README no longer states CC BY-SA 4.0. Nothing was replaced."
    }

    Invoke-WebRequest -Uri "$SweteRaw/license.md" -OutFile (Join-Path $staging 'first1kgreek-license.md')
    if ((Get-Content (Join-Path $staging 'first1kgreek-license.md') -Raw) -notmatch 'Attribution-ShareAlike 4\.0') {
        throw "First1KGreek's license.md is no longer Attribution-ShareAlike 4.0. Nothing was replaced."
    }

    foreach ($work in $Works) {
        $file = "tlg0527.tlg$work.1st1K-grc1.xml"
        Invoke-WebRequest -Uri "$SweteRaw/data/tlg0527/tlg$work/$file" -OutFile (Join-Path $staging "swete\$file")
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $target 'swete') | Out-Null
    Copy-Item (Join-Path $staging 'ntvmr-02.xml'), (Join-Path $staging 'cntr-02.txt'), (Join-Path $staging 'cntr-README.md') $target -Force
    Copy-Item (Join-Path $staging 'swete\*') (Join-Path $target 'swete') -Force
    Write-Host "Codex Alexandrinus fetched into $target (First1KGreek $($First1KGreek.Substring(0, 7)), CNTR $($Cntr.Substring(0, 7)))"
}
finally {
    Remove-Item -Recurse -Force $staging
}
