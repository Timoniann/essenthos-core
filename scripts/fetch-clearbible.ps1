<#
.SYNOPSIS
    Fetches Clear Bible's hand-made word alignments.

.DESCRIPTION
    Alignments between Hebrew and Greek source texts and translations, made by people rather than by
    a model. The English set aligns the Berean Standard Bible — a text this corpus already holds with
    its publisher's own tables — so the two can be compared, which is the only calibration the New
    Testament has. FTR-0186.

    Licence: CC BY 4.0 per alignment set, stated in each set's TOML and nowhere else — the repository
    carries no licence file at all. LICENCE.md beside the data quotes them. RUL-0105, RUL-0181.

    The Russian set is in this download and must not be loaded: its records do not correspond to the
    token file shipped beside them. PRB-0185.

.EXAMPLE
    ./scripts/fetch-clearbible.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' '..' 'essenthos-api' 'Resources'),
    # eng holds the Berean and Young's Literal; rus is kept for the day PRB-0185 is fixed upstream.
    [string[]] $Languages = @('eng', 'rus')
)

$ErrorActionPreference = 'Stop'
$root = Join-Path (Resolve-Path $ResourcesPath) 'ClearBible'
New-Item -ItemType Directory -Force -Path $root | Out-Null

foreach ($language in $Languages) {
    $zip = Join-Path $root "alignments-$language.zip"
    Write-Host "Fetching alignments-$language.zip"
    Invoke-WebRequest -OutFile $zip -Uri `
        "https://github.com/Clear-Bible/Alignments/releases/download/data-latest/alignments-$language.zip"
    Expand-Archive -Path $zip -DestinationPath $root -Force
}

# Each set states its own terms and they are not all the same — one of them records
# `process = "transfer from Spanish RVR09"` rather than manual. So the licences are read back rather
# than assumed, and anything that is not CC BY 4.0 is named for a person to look at.
$tomls = Get-ChildItem -Path (Join-Path $root 'data') -Recurse -Filter *.toml
$licences = $tomls | ForEach-Object {
    $text = Get-Content $_.FullName -Raw
    if ($text -match 'license\s*=\s*"([^"]+)"') { $Matches[1] } else { 'unstated' }
} | Sort-Object -Unique

Write-Host ("{0} alignment sets, licences: {1}" -f $tomls.Count, ($licences -join ', '))
if ($licences | Where-Object { $_ -notmatch 'CC-?BY' }) {
    Write-Warning "A set states terms that are not CC BY. Read its TOML before loading it."
}
