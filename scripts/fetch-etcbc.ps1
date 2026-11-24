<#
.SYNOPSIS
    Fetches two Text-Fabric datasets of the ETCBC, each pinned to a commit and every file to its SHA-256: the
    Peshitta's Old Testament and the Dead Sea Scrolls.

.DESCRIPTION
        Peshitta         github.com/ETCBC/peshitta  tf/0.2    the Syriac Old Testament, 65 books, 426,835 words
        DeadSeaScrolls   github.com/ETCBC/dss       tf/2.0.5  the scrolls from the Judaean Desert, 1,001 scrolls,
                                                              biblical and non-biblical, with Abegg's morphology

    The data of both is CC BY-NC 4.0; the conversion programs are MIT. The statements, and the one disagreement
    inside the Peshitta's repository about where its text comes from, are quoted in Resources/<folder>/LICENCE.md.
    Before anything is installed the statements are read again in the files they stand in (the repository's
    README, docs/about.md and LICENSE, kept beside the data under upstream/), and if one no longer says what it
    said, nothing is installed: the terms moved, and that is the owner's to read.

    Every file is held to the hash read on 2026-11-24 in scripts/fetch-etcbc.sha256, and the pins are the list of
    files: what is pinned is what is fetched. A folder already holding every pinned file with its hash is current
    and left alone. A folder that differs stops the run, naming the files, and nothing is replaced; move it aside
    to fetch again. A new folder is assembled in staging and moved into place only when whole.

.EXAMPLE
    ./scripts/fetch-etcbc.ps1

.EXAMPLE
    ./scripts/fetch-etcbc.ps1 -Only DeadSeaScrolls
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # One folder name, where only one dataset is wanted. Both by default.
    [string] $Only
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Datasets = @(
    [pscustomobject]@{
        Folder = 'Peshitta'; Repository = 'ETCBC/peshitta'; Commit = '9850f5addade26f681334aa475570bef9b0b440a'
        Data = 'tf/0.2'
        Nodes = [ordered]@{ word = 426835; book = 65; chapter = 1269; verse = 31341 }
        Statements = @(
            @{ File = 'upstream/about.md'; Phrases = @(
                'is subject to the [CC-BY-NC license](https://creativecommons.org/licenses/by-nc/4.0/)',
                'The conversion program itself it subject the liberal [MIT license]',
                'That is copyrighted by Brill and not included in this repo.') }
            @{ File = 'upstream/LICENSE'; Phrases = @('MIT License', 'Copyright (c) 2019 Dirk Roorda') }
        )
        # The feature files carry no licence line at all; their headers name SEDRA as the source.
        HeaderPhrase = '@source=SEDRA'
    }
    [pscustomobject]@{
        Folder = 'DeadSeaScrolls'; Repository = 'ETCBC/dss'; Commit = '0b7774a72b6d0e91712aa2156d2a1d354f1f5e6d'
        Data = 'tf/2.0.5'
        Nodes = [ordered]@{ sign = 1430241; word = 500995; scroll = 1001 }
        Statements = @(
            @{ File = 'upstream/README.md'; Phrases = @(
                'This dataset is licensed under a [Creative Commons Attribution-NonCommercial 4.0 International License (CC BY-NC 4.0)]') }
            @{ File = 'upstream/about.md'; Phrases = @(
                'Martin Abegg graciously gave permission to Jarod Jacobs to use his data and to distribute the results under a CC-BY-NC license.') }
            @{ File = 'upstream/LICENSE'; Phrases = @('Attribution-NonCommercial 4.0 International') }
        )
        HeaderPhrase = '@license=Creative Commons Attribution-NonCommercial 4.0 International License'
    }
)

# Where each file kept under upstream/ stands in the repository.
$Upstream = @{ 'upstream/README.md' = 'README.md'; 'upstream/about.md' = 'docs/about.md'; 'upstream/LICENSE' = 'LICENSE' }

if ($Only) {
    $Datasets = @($Datasets | Where-Object { $_.Folder -eq $Only })
    if ($Datasets.Count -eq 0) { throw "There is no dataset called `"$Only`" here. It is one of: Peshitta, DeadSeaScrolls." }
}

$Pins = @{}
foreach ($line in Get-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'fetch-etcbc.sha256')) {
    if ($line -match '^([0-9a-f]{64})\s+(\S.*)$') { $Pins[$Matches[2]] = $Matches[1] }
}

function Get-Hash([string] $Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

$resources = Resolve-Path $ResourcesPath
$staging = Join-Path ([IO.Path]::GetTempPath()) "etcbc-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    foreach ($set in $Datasets) {
        $root = Join-Path $resources $set.Folder
        $pinned = @($Pins.Keys | Where-Object { $_.StartsWith("$($set.Folder)/") } | Sort-Object)
        if ($pinned.Count -eq 0) { throw "fetch-etcbc.sha256 pins no file of $($set.Folder)." }

        $present = @($pinned | Where-Object { Test-Path -LiteralPath (Join-Path $resources $_) })
        if ($present.Count -gt 0) {
            $differ = @($pinned | Where-Object {
                $path = Join-Path $resources $_
                -not (Test-Path -LiteralPath $path) -or (Get-Hash $path) -ne $Pins[$_]
            })
            if ($differ.Count -eq 0) {
                Write-Host "$($set.Folder) is current ($($pinned.Count) files as pinned)."
                continue
            }

            throw "In $root these are missing or differ from the pinned bytes: $($differ -join ', '). Nothing was replaced; " +
                  'move the folder aside to fetch again.'
        }

        $built = Join-Path $staging $set.Folder
        New-Item -ItemType Directory -Force -Path $built, (Join-Path $built 'upstream') | Out-Null
        $raw = "https://raw.githubusercontent.com/$($set.Repository)/$($set.Commit)"
        Write-Host "$($set.Folder): fetching $($pinned.Count) files of github.com/$($set.Repository) at $($set.Commit.Substring(0, 8))"

        foreach ($pin in $pinned) {
            $relative = $pin.Substring($set.Folder.Length + 1)
            $source = if ($Upstream.ContainsKey($relative)) { $Upstream[$relative] } else { "$($set.Data)/$relative" }
            $target = Join-Path $built $relative
            for ($attempt = 1; $attempt -le 4; $attempt++) {
                curl.exe --silent --show-error --fail --location --max-time 300 --user-agent 'essenthos' --output $target "$raw/$source"
                if ((Test-Path $target) -and (Get-Hash $target) -eq $Pins[$pin]) { break }
            }

            if (-not (Test-Path $target) -or (Get-Hash $target) -ne $Pins[$pin]) {
                throw "$raw/$source does not hash to the $($Pins[$pin]) read on 2026-11-24. The commit is pinned, so the bytes " +
                      'cannot have moved; the download is broken or incomplete. Nothing was installed.'
            }
        }

        foreach ($statement in $set.Statements) {
            $flat = ([IO.File]::ReadAllText((Join-Path $built $statement.File))) -replace '\s+', ' '
            foreach ($phrase in $statement.Phrases) {
                if (-not $flat.Contains($phrase)) {
                    throw "$($statement.File) no longer says `"$phrase`". Nothing was installed; read what it says now and " +
                          "record it in Resources/$($set.Folder)/LICENCE.md before pinning new bytes."
                }
            }
        }

        $silent = @(Get-ChildItem $built -Filter '*.tf' | Where-Object {
            -not ((Get-Content $_.FullName -TotalCount 30 -Encoding UTF8) -contains $set.HeaderPhrase)
        })
        if ($silent.Count -gt 0) {
            throw "These feature files no longer carry `"$($set.HeaderPhrase)`": $($silent.Name -join ', '). Nothing was installed."
        }

        foreach ($type in $set.Nodes.Keys) {
            $line = Select-String -Path (Join-Path $built 'otype.tf') -Pattern "^(\d+)-(\d+)\t$type$" | Select-Object -First 1
            $count = if ($line) { [int]$line.Matches[0].Groups[2].Value - [int]$line.Matches[0].Groups[1].Value + 1 } else { 0 }
            if ($count -ne $set.Nodes[$type]) {
                throw "otype.tf holds $count $type nodes where the dataset read on 2026-11-24 has $($set.Nodes[$type]). Nothing was installed."
            }
        }

        if (Test-Path $root) { Get-ChildItem -LiteralPath $built | Move-Item -Destination $root }
        else { Move-Item -LiteralPath $built -Destination $root }
        $size = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
        Write-Host ("  {0} files, {1:N1} MB in {2}" -f $pinned.Count, $size, $root)
    }

    Write-Host 'Then run: python scripts/corpus-manifest.py --folders Peshitta,DeadSeaScrolls'
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
