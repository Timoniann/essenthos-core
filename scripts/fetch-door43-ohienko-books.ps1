<#
.SYNOPSIS
    Fetches the books of the Ohienko that people aligned word by word in translationCore projects of
    their own on Door43, outside unfoldingWord's Ukrainian interlinear, into
    Resources/Door43/uk_ubio_books.

.DESCRIPTION
    uk_ts/uk_ubio aligns parts of eight New Testament books of the Ohienko. Four personal projects
    align Hebrews, Romans, Revelation and Philippians nearly whole, and three add verses of John, Luke
    and 1 Peter. They are the same USFM 3 alignment over the same base text, read by the same reader
    (TSK-1905; Resources/Door43/LICENCE.md says whose each is and what its terms oblige).

    The projects are declared in Resources/Essenthos/sources/door43-projects.tsv, one row each, and
    all of them are fetched; the producer reads only the rows whose read is t. Hebrews, Luke and John
    are held back, each row says why, and are kept so that the measurement can be repeated on the
    same bytes.

    Each project is pinned to a commit, and each file taken from it to the SHA-256 it had then: the
    aligned book translationCore exported on upload (<repository name>.usfm, not the unaligned
    <book>.usfm beside it), the project's LICENSE.md and its manifest.json. The licence and manifest
    are kept beside the book as LICENSE-<book>.md and manifest-<book>.json. The licence each states was
    read before the decision to take it, so a file whose hash moved stops the fetch.

    A folder already holding every file with its pinned hash is left alone. A file there with another
    hash is not replaced: say so and stop, since replacing it is a decision, not a repair.

.EXAMPLE
    ./scripts/fetch-door43-ohienko-books.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Host43 = 'https://git.door43.org'
$Folder = 'uk_ubio_books'

# One row per project in the declared file: the commit, the SHA-256 of each file taken at it, and the alignment milestones
# the book holds. The file name carries the Paratext book number the producer reads the book from, as uk_ubio's do.
$Declared = Join-Path (Resolve-Path $ResourcesPath) 'Essenthos' 'sources' 'door43-projects.tsv'
$Projects = @(Get-Content -LiteralPath $Declared -Encoding utf8 | Where-Object { $_ -and -not $_.StartsWith('#') } |
    ConvertFrom-Csv -Delimiter "`t" | ForEach-Object {
        @{ Name = [IO.Path]::GetFileNameWithoutExtension($_.file); Repository = $_.repository; Commit = $_.commit
           Book = $_.book_sha256; Licence = $_.licence_sha256; Manifest = $_.manifest_sha256; Milestones = [int] $_.milestones }
    })
if ($Projects.Count -eq 0) { throw "$Declared declares no project." }

function Get-Files($project) {
    $repositoryName = $project.Repository.Split('/')[1]
    @(
        @{ Source = "$repositoryName.usfm"; Target = "$($project.Name).usfm"; Sha256 = $project.Book }
        @{ Source = 'LICENSE.md'; Target = "LICENSE-$($project.Name).md"; Sha256 = $project.Licence }
        @{ Source = 'manifest.json'; Target = "manifest-$($project.Name).json"; Sha256 = $project.Manifest }
    )
}

function Get-Sha256([string] $path) { (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant() }

$targetFolder = Join-Path (Resolve-Path $ResourcesPath) 'Door43' $Folder

# What is already there either is what was pinned or stops everything: a half-replaced folder would
# mix two states of somebody's work under one set of hashes.
$present = 0
$expected = 0
foreach ($project in $Projects) {
    foreach ($file in Get-Files $project) {
        $expected++
        $target = Join-Path $targetFolder $file.Target
        if (-not (Test-Path $target)) { continue }
        $hash = Get-Sha256 $target
        if ($hash -ne $file.Sha256) {
            throw "$target hashes to $hash, not to the $($file.Sha256) pinned for $($project.Repository) at " +
                  "$($project.Commit). Nothing was replaced: find out where it came from before removing it."
        }
        $present++
    }
}

if ($present -eq $expected) {
    Write-Host "$targetFolder already holds every pinned file."
    return
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "door43-ohienko-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    foreach ($project in $Projects) {
        Write-Host "Fetching $($project.Repository) at $($project.Commit)"
        foreach ($file in Get-Files $project) {
            $downloaded = Join-Path $staging $file.Target
            Invoke-WebRequest -Uri "$Host43/$($project.Repository)/raw/commit/$($project.Commit)/$($file.Source)" `
                -OutFile $downloaded -Headers @{ 'User-Agent' = 'essenthos' }
            $hash = Get-Sha256 $downloaded
            if ($hash -ne $file.Sha256) {
                throw "$($project.Repository)/$($file.Source) hashes to $hash, where it hashed to $($file.Sha256) at " +
                      "commit $($project.Commit). Nothing was replaced; a pinned commit should not change, so look at what was served."
            }
        }

        $usfm = [IO.File]::ReadAllText((Join-Path $staging "$($project.Name).usfm"))
        $milestones = ([regex]::Matches($usfm, 'zaln-s')).Count
        if ($milestones -ne $project.Milestones) {
            throw "$($project.Name).usfm holds $milestones alignment milestones, not $($project.Milestones). Nothing was replaced."
        }
    }

    New-Item -ItemType Directory -Force -Path $targetFolder | Out-Null
    foreach ($project in $Projects) {
        foreach ($file in Get-Files $project) {
            $target = Join-Path $targetFolder $file.Target
            if (-not (Test-Path $target)) {
                Copy-Item -Path (Join-Path $staging $file.Target) -Destination $target
            }
        }
    }

    Write-Host "$($Projects.Count) projects, $(($Projects | Measure-Object -Property Milestones -Sum).Sum) alignment milestones in $targetFolder"
    Write-Host "Then run: python scripts/corpus-manifest.py --folders Door43"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
