[CmdletBinding()]
param(
    # A name for this run; the build is snapshotted under it, so a later build does not change it.
    [Parameter(Mandatory)]
    [string] $Label,

    # ind: BSB against the Berean and Clear Bible gold (Genesis 1-10, Ruth, Jonah, Mark 1-4);
    # val: the passages no rule was chosen on (1 Samuel 17, Psalms 1-23, John 1-3);
    # val2: three more of other kinds, law, prophet and epistle (Exodus 21-23, Isaiah 40-42, Romans 5-8);
    # self: the King James against its own stated links; selfstrong: the same with source Strong allowed.
    [string] $Runs = 'ind,val,val2,self',

    [string] $Output = (Join-Path ([IO.Path]::GetTempPath()) 'essenthos-evidentia')
)

# Reads essenthos_core and writes nothing to it. Passages run one at a time: the owner works on this
# machine, and four at once took it over.
$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$directory = Join-Path $Output $Label
$snapshot = Join-Path $directory 'bin'
if (-not (Test-Path -LiteralPath $snapshot)) {
    # Not bin/Debug: a running core or forge holds that folder.
    & dotnet build (Join-Path $repository 'Essenthos.Forge') -p:BaseOutputPath=bin/benchmark/ -v q -nologo
    if ($LASTEXITCODE -ne 0) { throw "The Forge build failed ($LASTEXITCODE); fix it before measuring." }
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    Copy-Item -Recurse (Join-Path $repository 'Essenthos.Forge\bin\benchmark\Debug\net9.0') $snapshot
}

# A worktree holds only the licences of the corpus; the main checkout's is the one the database was
# loaded from, and the one with the parser in it.
$checkout = if ((Split-Path -Leaf (Split-Path -Parent $repository)) -eq '.worktrees') {
    Split-Path -Parent (Split-Path -Parent $repository)
} else {
    $repository
}
$env:Dataset__ResourcesPath = Join-Path $checkout 'Resources'
$env:DOTNET_ENVIRONMENT = 'Production'

$independent = '--without-source-strong', '--learn-from', 'KJV'
$passages = @{
    ind        = @(
        @('ind-gen', 'BSB', 'BHSA', 1, 1, 10, $independent),
        @('ind-ruth', 'BSB', 'BHSA', 8, 0, 0, $independent),
        @('ind-jon', 'BSB', 'BHSA', 32, 0, 0, $independent),
        @('ind-mark', 'BSB', 'NESTLE1904', 41, 1, 4, ($independent + '--learn-from-strong-numbers')))
    val        = @(
        @('val-1sa', 'BSB', 'BHSA', 9, 17, 17, $independent),
        @('val-ps', 'BSB', 'BHSA', 19, 1, 23, $independent),
        @('val-john', 'BSB', 'NESTLE1904', 43, 1, 3, ($independent + '--learn-from-strong-numbers')))
    val2       = @(
        @('val2-exo', 'BSB', 'BHSA', 2, 21, 23, $independent),
        @('val2-isa', 'BSB', 'BHSA', 23, 40, 42, $independent),
        @('val2-rom', 'BSB', 'NESTLE1904', 45, 5, 8, ($independent + '--learn-from-strong-numbers')))
    self       = @(
        @('self-gen', 'KJV', 'BHSA', 1, 1, 10, @('--without-source-strong')),
        @('self-ruth', 'KJV', 'BHSA', 8, 0, 0, @('--without-source-strong')),
        @('self-jon', 'KJV', 'BHSA', 32, 0, 0, @('--without-source-strong')),
        @('self-mark', 'KJV', 'NESTLE1904', 41, 1, 4, @('--without-source-strong')))
    selfstrong = @(
        @('sst-gen', 'KJV', 'BHSA', 1, 1, 10, @()),
        @('sst-ruth', 'KJV', 'BHSA', 8, 0, 0, @()),
        @('sst-jon', 'KJV', 'BHSA', 32, 0, 0, @()),
        @('sst-mark', 'KJV', 'NESTLE1904', 41, 1, 4, @()))
}

$final = 'global review + syntax-gated target gloss + attached grammatical words'
$line = '^(?<tier>.+?): (?<correct>[\d,]+)/[\d,]+ \([^)]*\) counting every proposal; [\d,]+/(?<covered>[\d,]+) \([^)]*\) over gold-covered words .*gold recall: [\d,]+/(?<gold>[\d,]+)'
function Number([string] $text) { [int]($text -replace ',', '') }

Push-Location $snapshot
try {
    foreach ($set in $Runs -split ',') {
        if (-not $passages.ContainsKey($set)) { throw "Unknown run '$set'; use ind, val, val2, self or selfstrong." }
        $total = @{ Correct = 0; Covered = 0; Gold = 0; SafeCorrect = 0; SafeCovered = 0 }
        foreach ($passage in $passages[$set]) {
            $name, $from, $to, $book, $first, $last, $flags = $passage
            $prefix = Join-Path $directory $name
            $arguments = @('Essenthos.Forge.dll', 'evidentia-measure-book', $from, $to, $book) + $flags
            if ($first -gt 0) { $arguments += '--from-chapter', $first, '--to-chapter', $last }
            $arguments += '--disagreements', "$prefix.disagreements.json", '--words', "$prefix.words.json"
            & dotnet @arguments *> "$prefix.report.txt"
            if ($LASTEXITCODE -ne 0) { throw "$name failed; see $prefix.report.txt" }
            foreach ($match in (Select-String -LiteralPath "$prefix.report.txt" -Pattern $line)) {
                $tier = $match.Matches[0].Groups['tier'].Value.Trim()
                $correct = Number $match.Matches[0].Groups['correct'].Value
                $covered = Number $match.Matches[0].Groups['covered'].Value
                if ($tier -eq $final) {
                    $gold = Number $match.Matches[0].Groups['gold'].Value
                    $total.Correct += $correct; $total.Covered += $covered; $total.Gold += $gold
                    '{0,-10} precision {1,5}/{2,-5} = {3:P2}   recall {1,5}/{4,-5} = {5:P2}' -f $name, $correct, $covered,
                        ($correct / [Math]::Max(1, $covered)), $gold, ($correct / [Math]::Max(1, $gold))
                }
                elseif ($tier -eq 'safe tier') {
                    $total.SafeCorrect += $correct; $total.SafeCovered += $covered
                }
            }
        }
        '{0,-10} precision {1,5}/{2,-5} = {3:P2}   recall {1,5}/{4,-5} = {5:P2}   safe tier {6}/{7} = {8:P2}' -f "$set all",
            $total.Correct, $total.Covered, ($total.Correct / [Math]::Max(1, $total.Covered)), $total.Gold,
            ($total.Correct / [Math]::Max(1, $total.Gold)), $total.SafeCorrect, $total.SafeCovered,
            ($total.SafeCorrect / [Math]::Max(1, $total.SafeCovered))
    }
}
finally {
    Pop-Location
}
"Reports, disagreements and per-word outcomes: $directory"
