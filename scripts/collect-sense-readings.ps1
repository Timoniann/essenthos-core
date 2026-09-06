<#
.SYNOPSIS
    Put the model's readings where the loader looks for them.

.DESCRIPTION
    A run of the sense harness leaves its prompts, its batches and its answers under .sense/, which
    is gitignored and is most of a megabyte of prompt text nobody needs twice. Only the answers are
    data: one JSON object per line, each carrying its own word, referent, confidence, prompt version,
    model and run date.

    This copies those answer files into Resources/SenseReadings, one folder per run, which is where
    SenseReadingLoader reads them from — under the corpus sources like every other large by-product,
    and out of the repository for the same reason. Nothing else from a run is taken: the batches are
    the questions and the corpus already holds the verses they were built from.

    It copies rather than moves, and it is safe to run again: an answers file already there is
    replaced only if the source is newer.

.PARAMETER From
    The run directory to collect. Defaults to .sense beside this checkout.

.PARAMETER To
    Where to put them. Defaults to Resources/SenseReadings beside this checkout.
#>
[CmdletBinding()]
param(
    [string]$From,
    [string]$To
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
if (-not $From) { $From = Join-Path $root '.sense' }
if (-not $To) { $To = Join-Path $root 'Resources/SenseReadings' }

if (-not (Test-Path $From)) {
    throw "There are no readings at $From. Point -From at the directory a sense run wrote, which holds one folder per run with an answers.jsonl in each."
}

$answers = Get-ChildItem -Path $From -Filter 'answers.jsonl' -Recurse -File
if ($answers.Count -eq 0) {
    throw "There is no answers.jsonl anywhere under $From. A run that produced no answers has nothing to load; check the run's ask.log before copying."
}

$rows = 0
foreach ($file in $answers) {
    $run = Split-Path -Leaf (Split-Path -Parent $file.FullName)
    $destination = Join-Path $To $run
    New-Item -ItemType Directory -Force -Path $destination | Out-Null

    $target = Join-Path $destination 'answers.jsonl'
    if ((-not (Test-Path $target)) -or ($file.LastWriteTimeUtc -gt (Get-Item $target).LastWriteTimeUtc)) {
        Copy-Item -Path $file.FullName -Destination $target -Force
    }

    # The manifest is copied beside the answers for a person reading the folder later. The loader
    # does not read it: every field it would supply is already on each answer, and provenance that
    # lives in a sibling file is provenance that goes missing the first time the two are separated.
    $manifest = Join-Path (Split-Path -Parent $file.FullName) 'manifest.json'
    if (Test-Path $manifest) {
        Copy-Item -Path $manifest -Destination (Join-Path $destination 'manifest.json') -Force
    }

    $rows += (Get-Content $target | Measure-Object -Line).Lines
}

Write-Host "Collected $($answers.Count) runs, $rows answers, into $To"
