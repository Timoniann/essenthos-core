<#
.SYNOPSIS
    Fetches the three great codices of the Greek Bible as INTF transcribed them: Codex Alexandrinus
    with the books in which Swete prints it, Codex Sinaiticus and Codex Vaticanus, each with CNTR's
    transcription of the same manuscript as the check on its verse labels.

.DESCRIPTION
    Each licence is checked, not assumed. Every INTF file must still state Creative Commons
    Attribution 4.0 in its own header, CNTR's README Attribution-ShareAlike 4.0, and First1KGreek's
    licence file Attribution-ShareAlike 4.0; if any statement changed, this stops and replaces
    nothing, because a licence that changed under us is the owner's decision and not a download.

    A manuscript whose folder already holds its files is left as it is: the transcriptions are
    served fresh on every request rather than as releases, so fetching one again could change the
    bytes the corpus was loaded from. Pass -Replace to fetch it again on purpose.

.EXAMPLE
    ./scripts/fetch-alexandrinus.ps1
    ./scripts/fetch-alexandrinus.ps1 -Manuscripts 01,03
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # Gregory-Aland numbers of the manuscripts to fetch.
    [string[]] $Manuscripts = @('02', '01', '03'),

    # Fetch a manuscript again even where its folder already holds it.
    [switch] $Replace,

    # The First1KGreek commit Swete's books are read at.
    [string] $First1KGreek = '8ee111eb44ecef4120c844e10749178d95d1f30c',

    # The CNTR transcriptions commit.
    [string] $Cntr = '4c0e9f94117ec3dc4ae40094aec044bb7a416a53'
)

$ErrorActionPreference = 'Stop'

$CntrRaw = "https://raw.githubusercontent.com/Center-for-New-Testament-Restoration/transcriptions/$Cntr"
$SweteRaw = "https://raw.githubusercontent.com/OpenGreekAndLatin/First1KGreek/$First1KGreek"

# Each manuscript: its folder, and the Virtual Manuscript Room's document number for it.
$Codices = @{
    '01' = @{ Folder = 'Sinaiticus'; Document = 20001 }
    '02' = @{ Folder = 'Alexandrinus'; Document = 20002 }
    '03' = @{ Folder = 'Vaticanus'; Document = 20003 }
}

# Genesis, 1-4 Maccabees and the Odes: the books Swete prints from Alexandrinus.
$Works = @('001', '023', '024', '025', '026', '028')

foreach ($requested in $Manuscripts) {
    # PowerShell reads an unquoted 01 on the command line as the number 1.
    $number = '{0:D2}' -f [int]$requested
    $codex = $Codices[$number]
    if ($null -eq $codex) {
        throw "No manuscript $number is known here; the ones that are: $(($Codices.Keys | Sort-Object) -join ', ')."
    }

    $target = Join-Path $ResourcesPath $codex.Folder
    $transcriptionName = "ntvmr-$number.xml"
    if (-not $Replace -and (Test-Path (Join-Path $target $transcriptionName)) -and (Test-Path (Join-Path $target "cntr-$number.txt"))) {
        Write-Host "$($codex.Folder) is already in $target; pass -Replace to fetch it again."
        continue
    }

    $staging = Join-Path ([IO.Path]::GetTempPath()) "codex-$number-$([guid]::NewGuid().ToString('n'))"
    New-Item -ItemType Directory -Force -Path (Join-Path $staging 'swete') | Out-Null

    try {
        $ntvmr = "https://ntvmr.uni-muenster.de/community/vmr/api/transcript/get/?docID=$($codex.Document)&pageID=ALL&format=teiraw"
        Invoke-WebRequest -Uri $ntvmr -OutFile (Join-Path $staging $transcriptionName)
        if ((Get-Content (Join-Path $staging $transcriptionName) -Raw) -notmatch 'Creative Commons Attribution 4\.0') {
            throw "INTF's transcription of $number no longer states Creative Commons Attribution 4.0 in its header. Nothing was replaced."
        }

        Invoke-WebRequest -Uri "$CntrRaw/class%201/$number.txt" -OutFile (Join-Path $staging "cntr-$number.txt")
        Invoke-WebRequest -Uri "$CntrRaw/README.md" -OutFile (Join-Path $staging 'cntr-README.md')
        if ((Get-Content (Join-Path $staging 'cntr-README.md') -Raw) -notmatch 'CC BY-SA 4\.0') {
            throw "CNTR's README no longer states CC BY-SA 4.0. Nothing was replaced."
        }

        $files = @($transcriptionName, "cntr-$number.txt", 'cntr-README.md')
        if ($number -eq '02') {
            Invoke-WebRequest -Uri "$SweteRaw/license.md" -OutFile (Join-Path $staging 'first1kgreek-license.md')
            if ((Get-Content (Join-Path $staging 'first1kgreek-license.md') -Raw) -notmatch 'Attribution-ShareAlike 4\.0') {
                throw "First1KGreek's license.md is no longer Attribution-ShareAlike 4.0. Nothing was replaced."
            }

            foreach ($work in $Works) {
                $file = "tlg0527.tlg$work.1st1K-grc1.xml"
                Invoke-WebRequest -Uri "$SweteRaw/data/tlg0527/tlg$work/$file" -OutFile (Join-Path $staging "swete\$file")
            }
        }

        New-Item -ItemType Directory -Force -Path $target | Out-Null
        Copy-Item ($files | ForEach-Object { Join-Path $staging $_ }) $target -Force
        if ($number -eq '02') {
            New-Item -ItemType Directory -Force -Path (Join-Path $target 'swete') | Out-Null
            Copy-Item (Join-Path $staging 'swete\*') (Join-Path $target 'swete') -Force
        }

        Write-Host "$($codex.Folder) fetched into $target (CNTR $($Cntr.Substring(0, 7)))"
    }
    finally {
        Remove-Item -Recurse -Force $staging
    }
}
