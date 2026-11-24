<#
.SYNOPSIS
    Fetches TAHOT and TAGNT, STEPBible's tagged Hebrew Old Testament and Greek New Testament: the
    prefix boundaries the corpus was reconstructing by adjacency, and which printed editions hold
    each Greek word.

.DESCRIPTION
    TAHOT segments every word of the Leningrad codex into morphemes and gives each one a
    disambiguated Strong number and an English gloss. The Open Hebrew Bible mapping the corpus
    already loads numbers a prefixed mem H4480 -- the same number as the free-standing preposition
    -- so nothing in the corpus could tell a prefix from a word. TAHOT says which is which.

    Licence: CC BY-NC 3.0, from the publisher's own licence page. The README says CC BY 4.0 and the
    file header says CC BY 4.0 beside "Please do not redistribute it yourself"; all three statements
    are quoted in Resources/STEPBible/LICENCE.md with where each was read, and the licence page is
    the one that grants redistribution outright. The owner accepted NonCommercial on 2026-09-03.

    TAGNT is the Greek half, in the same folder, under the same header block: every word of the
    Greek editions with the editions that print it (NA27, NA28, Tyndale House, SBL, Westcott-Hort,
    Tregelles, TR, Byzantine) and the variants that change a translation.

    Six files, about 100 MB, split by the publisher "because a single file too large for Github":
    four TAHOT volumes and two TAGNT volumes. Every file is held to the SHA-256 read in
    scripts/fetch-stepbible.sha256; a file already on disk with its hash is left alone, one that
    differs stops the run and nothing is replaced.

    Pinned to a commit rather than to master. The header block is the licence, so a file whose
    header no longer says what LICENCE.md quotes is a decision and not a download: the fetch checks
    for the sentences it was read under and stops without replacing anything if they are gone.

.EXAMPLE
    ./scripts/fetch-stepbible.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # The commit LICENCE.md quotes. Move it deliberately, and re-read the header when you do.
    [string] $Commit = '89ece29525e3c51d61850b28b4d4cf27ef9cd321'
)

$ErrorActionPreference = 'Stop'

$folder = 'Translators Amalgamated OT+NT'
$hebrew = 'tagged with disambiguated Strongs extended for BDB including tags for prefixes & suffixes'
$greek = 'All the words from standard Greek editions (NA27/28, TR, SBL, TH, Byz, WH, Treg)'
$volumes = @(
    @{ Name = 'TAHOT Gen-Deu - Translators Amalgamated Hebrew OT - STEPBible.org CC BY.txt'; Description = $hebrew }
    @{ Name = 'TAHOT Jos-Est - Translators Amalgamated Hebrew OT - STEPBible.org CC BY.txt'; Description = $hebrew }
    @{ Name = 'TAHOT Job-Sng - Translators Amalgamated Hebrew OT - STEPBible.org CC BY.txt'; Description = $hebrew }
    @{ Name = 'TAHOT Isa-Mal - Translators Amalgamated Hebrew OT - STEPBible.org CC BY.txt'; Description = $hebrew }
    @{ Name = 'TAGNT Mat-Jhn - Translators Amalgamated Greek NT - STEPBible.org CC-BY.txt'; Description = $greek }
    @{ Name = 'TAGNT Act-Rev - Translators Amalgamated Greek NT - STEPBible.org CC-BY.txt'; Description = $greek }
)

# The sentence LICENCE.md was written against, in the header block of every volume.
$attribution = 'Data created by www.STEPBible.org based on work at Tyndale House Cambridge'

$pins = @{}
foreach ($line in Get-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'fetch-stepbible.sha256')) {
    if ($line -match '^([0-9a-f]{64})\s+(\S.*)$') { $pins[$Matches[2]] = $Matches[1] }
}

function Get-Hash([string] $Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

$root = Join-Path (Resolve-Path $ResourcesPath) 'STEPBible'
New-Item -ItemType Directory -Force -Path $root | Out-Null

$staging = Join-Path ([System.IO.Path]::GetTempPath()) "stepbible-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $wanted = @()
    foreach ($volume in $volumes) {
        $name = $volume.Name
        if (-not $pins.ContainsKey($name)) { throw "fetch-stepbible.sha256 pins no hash for $name." }
        $installed = Join-Path $root $name
        if (Test-Path -LiteralPath $installed) {
            if ((Get-Hash $installed) -eq $pins[$name]) { continue }
            throw "$installed differs from the bytes pinned in fetch-stepbible.sha256. Nothing was replaced; " +
                  'move it aside to fetch again.'
        }

        $uri = "https://raw.githubusercontent.com/STEPBible/STEPBible-Data/$Commit/" +
               "$([uri]::EscapeDataString($folder))/$([uri]::EscapeDataString($name))"
        Write-Host "Fetching $($name.Substring(0, 13))"
        $staged = Join-Path $staging $name
        Invoke-WebRequest -Uri $uri -OutFile $staged
        if ((Get-Hash $staged) -ne $pins[$name]) {
            throw "$uri does not hash to the bytes pinned in fetch-stepbible.sha256. The commit is pinned, so the " +
                  'download is broken; nothing was replaced.'
        }

        # Read the header rather than the whole file: the licence lives in the first few lines and
        # the file is 20 MB.
        $header = (Get-Content -Path $staged -TotalCount 40) -join "`n"
        if ($header -notlike "*$attribution*") {
            throw "$name no longer carries the attribution line LICENCE.md quotes " +
                  "(`"$attribution`"). A licence statement that changed under us is a decision, " +
                  "not a download: read the header, update Resources/STEPBible/LICENCE.md, and " +
                  "only then move the commit this script is pinned to."
        }
        if ($header -notlike "*$($volume.Description)*") {
            throw "$name no longer describes itself as $($volume.Description). Check what the file has " +
                  "become before replacing the copy on disk."
        }

        $wanted += $name
    }

    foreach ($name in $wanted) {
        Move-Item -LiteralPath (Join-Path $staging $name) -Destination (Join-Path $root $name)
    }
}
finally {
    Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
}

$total = (Get-ChildItem $root -Filter 'TA*.txt' | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("{0} volumes, {1:N0} MB in {2}" -f $volumes.Count, $total, $root)
Write-Host 'Then run: python scripts/corpus-manifest.py --folders STEPBible'
