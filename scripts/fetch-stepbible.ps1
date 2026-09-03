<#
.SYNOPSIS
    Fetches TAHOT, STEPBible's tagged Hebrew Old Testament, which states the prefix boundaries the
    corpus was reconstructing by adjacency.

.DESCRIPTION
    TAHOT segments every word of the Leningrad codex into morphemes and gives each one a
    disambiguated Strong number and an English gloss. The Open Hebrew Bible mapping the corpus
    already loads numbers a prefixed mem H4480 -- the same number as the free-standing preposition
    -- so nothing in the corpus could tell a prefix from a word. TAHOT says which is which.

    Licence: CC BY-NC 3.0, from the publisher's own licence page. The README says CC BY 4.0 and the
    file header says CC BY 4.0 beside "Please do not redistribute it yourself"; all three statements
    are quoted in Resources/STEPBible/LICENCE.md with where each was read, and the licence page is
    the one that grants redistribution outright. The owner accepted NonCommercial on 2026-09-03.

    Four files, about 70 MB, split by the publisher "because a single file too large for Github".
    They are one dataset and are fetched together; the loader reads whichever are present.

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
$volumes = @('Gen-Deu', 'Jos-Est', 'Job-Sng', 'Isa-Mal')
$suffix = 'Translators Amalgamated Hebrew OT - STEPBible.org CC BY.txt'

# The sentences LICENCE.md was written against. Both are in the header block of every volume.
$attribution = 'Data created by www.STEPBible.org based on work at Tyndale House Cambridge'
$description = 'tagged with disambiguated Strongs extended for BDB including tags for prefixes & suffixes'

$root = Join-Path (Resolve-Path $ResourcesPath) 'STEPBible'
New-Item -ItemType Directory -Force -Path $root | Out-Null

$staging = Join-Path ([System.IO.Path]::GetTempPath()) "stepbible-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    foreach ($volume in $volumes) {
        $name = "TAHOT $volume - $suffix"
        $encoded = [uri]::EscapeDataString($name)
        $uri = "https://raw.githubusercontent.com/STEPBible/STEPBible-Data/$Commit/" +
               "$([uri]::EscapeDataString($folder))/$encoded"

        Write-Host "Fetching TAHOT $volume"
        $staged = Join-Path $staging $name
        Invoke-WebRequest -Uri $uri -OutFile $staged

        # Read the header rather than the whole file: the licence lives in the first few lines and
        # the file is 20 MB.
        $header = (Get-Content -Path $staged -TotalCount 40) -join "`n"
        if ($header -notlike "*$attribution*") {
            throw "TAHOT $volume no longer carries the attribution line LICENCE.md quotes " +
                  "(`"$attribution`"). A licence statement that changed under us is a decision, " +
                  "not a download: read the header, update Resources/STEPBible/LICENCE.md, and " +
                  "only then move the commit this script is pinned to."
        }
        if ($header -notlike "*$description*") {
            throw "TAHOT $volume no longer describes itself as $description. The prefix and suffix " +
                  "tags are the whole reason this dataset is loaded; check what the file has " +
                  "become before replacing the copy on disk."
        }
    }

    foreach ($volume in $volumes) {
        $name = "TAHOT $volume - $suffix"
        Move-Item -Path (Join-Path $staging $name) -Destination (Join-Path $root $name) -Force
    }
}
finally {
    Remove-Item -Path $staging -Recurse -Force -ErrorAction SilentlyContinue
}

$total = (Get-ChildItem $root -Filter 'TAHOT *.txt' | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("{0} volumes, {1:N0} MB in {2}" -f $volumes.Count, $total, $root)
Write-Host ("A reload is not automatic: the Old Testament link loader does nothing when links " +
            "between the King James and BHSA already exist.")
