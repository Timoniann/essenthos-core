<#
.SYNOPSIS
    Runs the test areas a change can break, and only those.

.DESCRIPTION
    The suite is sorted into areas: a test class lives in Essenthos.Core.Tests/<Area>/ under the
    namespace Essenthos.Core.Tests.<Area>, so `dotnet test --filter` selects an area by its
    namespace. scripts/test-areas.json says which areas each changed path can break; a path no rule
    names, shared code (the schema, the test helpers, the project files) or a rule saying "all"
    runs the whole suite.

    The files compared are what the branch changed since it left -Base, plus whatever is not
    committed yet (staged, unstaged and untracked). -Uncommitted takes only the latter. -Areas names
    the areas outright and compares nothing.

    It builds into bin/<Output>/ like the registered core actions, so the running API's
    bin/Debug is never touched, and in a worktree it reads the corpus from the main checkout's
    Resources/ unless Dataset__ResourcesPath already says otherwise.

    Areas are for iterating. The whole non-corpus suite is still the check a merge needs.

.PARAMETER Base
    The ref the branch is compared with. Default main.

.PARAMETER Uncommitted
    Compare only what is not committed yet.

.PARAMETER Areas
    Run these areas, whatever changed. "all" runs the whole suite.

.PARAMETER Corpus
    Include the tests that read the corpus on disk (Category=Corpus), which are left out by default.

.PARAMETER Output
    The build output label, bin/<Output>/.

.PARAMETER List
    Say which areas would run and why, and run nothing.
#>
[CmdletBinding()]
param(
    [string]$Base = 'main',
    [switch]$Uncommitted,
    [string[]]$Areas,
    [switch]$Corpus,
    [string]$Output = 'affected',
    [switch]$List
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$map = Get-Content -Raw -Encoding utf8 (Join-Path $PSScriptRoot 'test-areas.json') | ConvertFrom-Json
$known = @($map.areas.PSObject.Properties.Name)
$testsFolder = 'Essenthos.Core.Tests'

function ConvertTo-Pattern([string]$glob) {
    $builder = [System.Text.StringBuilder]::new('^')
    for ($i = 0; $i -lt $glob.Length; $i++) {
        $c = $glob[$i]
        if ($c -eq '*' -and $i + 1 -lt $glob.Length -and $glob[$i + 1] -eq '*') {
            if ($i + 2 -lt $glob.Length -and $glob[$i + 2] -eq '/') {
                [void]$builder.Append('(?:.*/)?')
                $i += 2
            }
            else {
                [void]$builder.Append('.*')
                $i++
            }
        }
        elseif ($c -eq '*') { [void]$builder.Append('[^/]*') }
        else { [void]$builder.Append([regex]::Escape([string]$c)) }
    }
    [void]$builder.Append('$')
    [regex]::new($builder.ToString(), 'IgnoreCase')
}

$rules = @($map.rules | ForEach-Object {
        [pscustomobject]@{ Path = $_.path; Pattern = ConvertTo-Pattern $_.path; Areas = @($_.areas) }
    })

function Get-AreasOf([string]$path) {
    $parts = $path -split '/'
    if ($parts.Length -ge 3 -and $parts[0] -eq $testsFolder -and $known -contains $parts[1]) {
        return [pscustomobject]@{ Areas = @($parts[1]); Rule = "$testsFolder/$($parts[1])/" }
    }
    foreach ($rule in $rules) {
        if ($rule.Pattern.IsMatch($path)) {
            return [pscustomobject]@{ Areas = $rule.Areas; Rule = $rule.Path }
        }
    }
    [pscustomobject]@{ Areas = @('all'); Rule = '(no rule names it)' }
}

function Invoke-Git([string[]]$arguments) {
    $lines = & git -C $root @arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($arguments -join ' ') failed with exit code $LASTEXITCODE." }
    @($lines | Where-Object { $_ })
}

$chosen = [System.Collections.Generic.SortedSet[string]]::new()
$reasons = [ordered]@{}
if ($Areas) {
    foreach ($area in $Areas -split ',' | ForEach-Object { $_.Trim() } | Where-Object { $_ }) {
        if ($area -ne 'all' -and $known -notcontains $area) {
            throw "There is no test area '$area'. The areas are: $($known -join ', '), or all."
        }
        [void]$chosen.Add($area)
        $reasons[$area] = @('named with -Areas')
    }
}
else {
    $changed = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)
    if (-not $Uncommitted) {
        $fork = (Invoke-Git @('merge-base', $Base, 'HEAD')) | Select-Object -First 1
        Invoke-Git @('diff', '--name-only', '--no-renames', $fork, 'HEAD') | ForEach-Object { [void]$changed.Add($_) }
    }
    Invoke-Git @('diff', '--name-only', '--no-renames', 'HEAD') | ForEach-Object { [void]$changed.Add($_) }
    Invoke-Git @('ls-files', '--others', '--exclude-standard') | ForEach-Object { [void]$changed.Add($_) }

    foreach ($path in $changed) {
        $found = Get-AreasOf $path
        foreach ($area in $found.Areas) {
            [void]$chosen.Add($area)
            if (-not $reasons.Contains($area)) { $reasons[$area] = [System.Collections.Generic.List[string]]::new() }
            $reasons[$area].Add("$path  ($($found.Rule))")
        }
    }

    Write-Host "$($changed.Count) changed file(s) against $(if ($Uncommitted) { 'HEAD' } else { $Base })."
}

foreach ($area in $reasons.Keys) {
    $why = @($reasons[$area])
    Write-Host ("{0,-10} {1}" -f $area, $why[0])
    $why | Select-Object -Skip 1 -First 4 | ForEach-Object { Write-Host ("{0,-10} {1}" -f '', $_) }
    if ($why.Count -gt 5) { Write-Host ("{0,-10} ... and {1} more" -f '', ($why.Count - 5)) }
}

$whole = $chosen.Contains('all')
if (-not $whole -and $chosen.Count -eq 0) {
    Write-Host 'No test area is affected.'
    exit 0
}

$conditions = @()
if (-not $whole) {
    $conditions += '(' + (($chosen | ForEach-Object { "FullyQualifiedName~$testsFolder.$_." }) -join '|') + ')'
}
if (-not $Corpus) { $conditions += 'Category!=Corpus' }
$filter = $conditions -join '&'
Write-Host "Running: $(if ($whole) { 'the whole suite' } else { $chosen -join ', ' })$(if ($filter) { "  [--filter $filter]" })"
if ($List) { exit 0 }

if (-not $env:Dataset__ResourcesPath) {
    $marker = [IO.Path]::DirectorySeparatorChar + '.worktrees' + [IO.Path]::DirectorySeparatorChar
    $at = $root.IndexOf($marker, [StringComparison]::OrdinalIgnoreCase)
    if ($at -ge 0) {
        $main = Join-Path $root.Substring(0, $at) 'Resources'
        if (Test-Path $main) {
            $env:Dataset__ResourcesPath = $main
            Write-Host "Reading the corpus from $main."
        }
    }
}

$arguments = @('test', (Join-Path $root $testsFolder), '--nologo', "-p:BaseOutputPath=bin/$Output/")
if ($filter) { $arguments += @('--filter', $filter) }
$clock = [Diagnostics.Stopwatch]::StartNew()
& dotnet @arguments
$code = $LASTEXITCODE
Write-Host ("Took {0:n0} s, build included." -f $clock.Elapsed.TotalSeconds)
exit $code
