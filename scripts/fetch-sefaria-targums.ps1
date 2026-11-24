<#
.SYNOPSIS
    Fetches the Aramaic Targums from Sefaria's export, version by version: Onkelos on the Torah, Jonathan on the
    Prophets and the Targums of the Writings, each pinned to its SHA-256 and each held to its own licence field.

.DESCRIPTION
    Sefaria licenses each version separately ("Each text is licensed separately, so there is no overall license for
    this repository"), and the licence of a version is the `license` field inside its JSON. The versions taken are
    those whose field says "Public Domain":

        onkelos/<book>.json    Onkelos/Torah/Onkelos <book>/Hebrew/Onkelos <book>.json (toratemetfreeware.com)  5 books
        jonathan/<book>.json   Targum Jonathan/Prophets/Targum Jonathan on <book>/Hebrew/Mikraot Gedolot.json     21 books
        writings/<book>.json   Aramaic Targum/Writings/Aramaic Targum to <book>/Hebrew/Mikraot Gedolot.json       8 books

    They are installed under names with no spaces, Targums/<work>/<book>.json, because the build's product headers
    refuse a path with a space; the pin of each file names the object it came from.

    Left at the source, and never fetched: the Yemenite Taj vocalisation of Onkelos (CC-BY-SA), the Sifsei Chachomim
    Chumash's Onkelos (CC-BY-NC, a printed edition's copy of the same text), the Targum Sheni on Esther (licence
    "unknown"), Melamed's Yemenite Targum of the Song of Songs (a second text of a book already taken), and
    "Targum Jonathan on <Torah book>", which is Pseudo-Jonathan and another work. Sefaria files Aramaic under
    "Hebrew"; the folder name is the export's, not a claim about the language.

    The pins in scripts/fetch-sefaria-targums.sha256 are the list of files: what is pinned is what is fetched, from
    https://storage.googleapis.com/sefaria-export/json/Tanakh/Targum/<object>. Every file must hash as read on
    2026-11-24 and say "Public Domain" in its licence field, or nothing is installed. A folder already holding every
    pinned file with its hash is current and left alone; a folder that differs stops the run and nothing is
    replaced. Resources/Targums/LICENCE.md quotes what was read.

.EXAMPLE
    ./scripts/fetch-sefaria-targums.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Folder = 'Targums'
$Bucket = 'https://storage.googleapis.com/sefaria-export/json/Tanakh/Targum'
$Allowed = 'Public Domain'

$Pins = [ordered]@{}
$Objects = @{}
foreach ($line in Get-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'fetch-sefaria-targums.sha256')) {
    if ($line -match '^([0-9a-f]{64})\s+(\S+)\s+<-\s+(\S.*)$') { $Pins[$Matches[2]] = $Matches[1]; $Objects[$Matches[2]] = $Matches[3] }
}

function Get-Hash([string] $Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

$resources = Resolve-Path $ResourcesPath
$root = Join-Path $resources $Folder
$pinned = @($Pins.Keys)

$present = @($pinned | Where-Object { Test-Path -LiteralPath (Join-Path $resources $_) })
if ($present.Count -gt 0) {
    $differ = @($pinned | Where-Object {
        $path = Join-Path $resources $_
        -not (Test-Path -LiteralPath $path) -or (Get-Hash $path) -ne $Pins[$_]
    })
    if ($differ.Count -eq 0) {
        Write-Host "$Folder is current ($($pinned.Count) files as pinned)."
        return
    }

    throw "In $root these are missing or differ from the pinned bytes: $($differ -join ', '). Nothing was replaced; " +
          'move the folder aside to fetch again.'
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "targums-$([guid]::NewGuid().ToString('n'))"
$built = Join-Path $staging $Folder
New-Item -ItemType Directory -Force -Path $built | Out-Null

try {
    Write-Host "Fetching $($pinned.Count) Targum versions from Sefaria's export"
    foreach ($pin in $pinned) {
        $relative = $pin.Substring($Folder.Length + 1)
        $target = Join-Path $built $relative
        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        $uri = "$Bucket/" + (($Objects[$pin] -split '/' | ForEach-Object { [uri]::EscapeDataString($_) }) -join '/')
        for ($attempt = 1; $attempt -le 4; $attempt++) {
            curl.exe --silent --show-error --fail --location --max-time 120 --user-agent 'essenthos' --output $target $uri
            if ((Test-Path -LiteralPath $target) -and (Get-Hash $target) -eq $Pins[$pin]) { break }
        }

        if (-not (Test-Path -LiteralPath $target)) { throw "$uri could not be fetched; nothing was installed." }
        $found = Get-Hash $target
        if ($found -ne $Pins[$pin]) {
            throw "$uri hashes to $found, not the $($Pins[$pin]) read on 2026-11-24. Sefaria has changed the version, so " +
                  'nothing was installed; read what changed, and its licence field, before pinning the new bytes.'
        }

        $version = [IO.File]::ReadAllText($target) | ConvertFrom-Json
        if ($version.license -ne $Allowed) {
            throw "$relative says its licence is `"$($version.license)`", not `"$Allowed`". Nothing was installed."
        }
    }

    if (Test-Path $root) { Get-ChildItem -LiteralPath $built | Move-Item -Destination $root }
    else { Move-Item -LiteralPath $built -Destination $root }
    $size = (Get-ChildItem $root -Recurse -File -Filter '*.json' | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("  {0} versions, {1:N1} MB in {2}" -f $pinned.Count, $size, $root)
    Write-Host 'Then run: python scripts/corpus-manifest.py --folders Targums'
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
