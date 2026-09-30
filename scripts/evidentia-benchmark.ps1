[CmdletBinding()]
param(
    # A name for this run; the build is snapshotted under it, so a later build does not change it.
    [Parameter(Mandatory)]
    [string] $Label,

    # ind: BSB against the Berean and Clear Bible gold (Genesis 1-10, Ruth, Jonah, Mark 1-4);
    # val: the passages no rule was chosen on (1 Samuel 17, Psalms 1-23, John 1-3);
    # val2: three more of other kinds, law, prophet and epistle (Exodus 21-23, Isaiah 40-42, Romans 5-8);
    # held: nine chapters drawn at random (seed 20260928) from outside every passage measured before,
    # which no rule was chosen or kept on: val and val2 have since been used to keep rules, so held is
    # the one figure still free of that. Measure on it, never tune on it.
    # self: the King James against its own stated links; selfstrong: the same with source Strong allowed.
    # kjv, kjvval: the King James against its own links with the index learned from the Berean instead,
    # so nothing the King James states teaches it (the passages of ind, and of val and val2).
    # bbe, nwt: the Bible in Basic English and the New World Translation, read from their files, over
    # the passages of ind and val. Neither states a link, so there is no gold: what they report is
    # coverage and how far the links agree with the King James's and the Berean's own.
    [string] $Runs = 'ind,val,val2,kjv',

    [string] $Output = (Join-Path ([IO.Path]::GetTempPath()) 'essenthos-evidentia'),

    # How many verses on either side a word's candidates may come from; left out, the Forge's default.
    [int] $NeighbourVerses = -1,
    # Further flags for every passage, e.g. '--entity-anchors', '--entity-names', '<name-consensus words.tsv>',
    # or the second pass: '--confirmed', '<folder of --confirmed-out files>', '--aligner-pairs', '<score --pairs files>'.
    [string[]] $Extra = @()
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
    Copy-Item -Recurse (Join-Path $repository 'Essenthos.Forge\bin\benchmark\Debug\net10.0') $snapshot
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
$fromFiles = $independent + '--source-from-files', '--routes', 'KJV,BSB'
$kjv = '--without-source-strong', '--learn-from', 'BSB'
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
    held       = @(
        @('held-num', 'BSB', 'BHSA', 4, 33, 33, $independent),
        @('held-1chr', 'BSB', 'BHSA', 13, 7, 7, $independent),
        @('held-ezra', 'BSB', 'BHSA', 15, 9, 9, $independent),
        @('held-isa', 'BSB', 'BHSA', 23, 34, 34, $independent),
        @('held-dan10', 'BSB', 'BHSA', 27, 10, 10, $independent),
        @('held-dan12', 'BSB', 'BHSA', 27, 12, 12, $independent),
        @('held-matt', 'BSB', 'NESTLE1904', 40, 25, 25, ($independent + '--learn-from-strong-numbers')),
        @('held-heb', 'BSB', 'NESTLE1904', 58, 10, 10, ($independent + '--learn-from-strong-numbers')),
        @('held-rev', 'BSB', 'NESTLE1904', 66, 17, 17, ($independent + '--learn-from-strong-numbers')))
    val2       = @(
        @('val2-exo', 'BSB', 'BHSA', 2, 21, 23, $independent),
        @('val2-isa', 'BSB', 'BHSA', 23, 40, 42, $independent),
        @('val2-rom', 'BSB', 'NESTLE1904', 45, 5, 8, ($independent + '--learn-from-strong-numbers')))
    self       = @(
        @('self-gen', 'KJV', 'BHSA', 1, 1, 10, @('--without-source-strong')),
        @('self-ruth', 'KJV', 'BHSA', 8, 0, 0, @('--without-source-strong')),
        @('self-jon', 'KJV', 'BHSA', 32, 0, 0, @('--without-source-strong')),
        @('self-mark', 'KJV', 'NESTLE1904', 41, 1, 4, @('--without-source-strong')))
    kjv        = @(
        @('kjv-gen', 'KJV', 'BHSA', 1, 1, 10, $kjv),
        @('kjv-ruth', 'KJV', 'BHSA', 8, 0, 0, $kjv),
        @('kjv-jon', 'KJV', 'BHSA', 32, 0, 0, $kjv),
        @('kjv-mark', 'KJV', 'NESTLE1904', 41, 1, 4, $kjv))
    kjvval     = @(
        @('kjv-1sa', 'KJV', 'BHSA', 9, 17, 17, $kjv),
        @('kjv-ps', 'KJV', 'BHSA', 19, 1, 23, $kjv),
        @('kjv-john', 'KJV', 'NESTLE1904', 43, 1, 3, $kjv),
        @('kjv-exo', 'KJV', 'BHSA', 2, 21, 23, $kjv),
        @('kjv-isa', 'KJV', 'BHSA', 23, 40, 42, $kjv),
        @('kjv-rom', 'KJV', 'NESTLE1904', 45, 5, 8, $kjv))
    bbe        = @(
        @('bbe-gen', 'BBE', 'BHSA', 1, 1, 10, $fromFiles),
        @('bbe-ruth', 'BBE', 'BHSA', 8, 0, 0, $fromFiles),
        @('bbe-jon', 'BBE', 'BHSA', 32, 0, 0, $fromFiles),
        @('bbe-mark', 'BBE', 'NESTLE1904', 41, 1, 4, ($fromFiles + '--learn-from-strong-numbers')),
        @('bbe-1sa', 'BBE', 'BHSA', 9, 17, 17, $fromFiles),
        @('bbe-ps', 'BBE', 'BHSA', 19, 1, 23, $fromFiles),
        @('bbe-john', 'BBE', 'NESTLE1904', 43, 1, 3, ($fromFiles + '--learn-from-strong-numbers')))
    nwt        = @(
        @('nwt-gen', 'NWT2013', 'BHSA', 1, 1, 10, $fromFiles),
        @('nwt-ruth', 'NWT2013', 'BHSA', 8, 0, 0, $fromFiles),
        @('nwt-jon', 'NWT2013', 'BHSA', 32, 0, 0, $fromFiles),
        @('nwt-mark', 'NWT2013', 'NESTLE1904', 41, 1, 4, ($fromFiles + '--learn-from-strong-numbers')),
        @('nwt-1sa', 'NWT2013', 'BHSA', 9, 17, 17, $fromFiles),
        @('nwt-ps', 'NWT2013', 'BHSA', 19, 1, 23, $fromFiles),
        @('nwt-john', 'NWT2013', 'NESTLE1904', 43, 1, 3, ($fromFiles + '--learn-from-strong-numbers')))
    selfstrong = @(
        @('sst-gen', 'KJV', 'BHSA', 1, 1, 10, @()),
        @('sst-ruth', 'KJV', 'BHSA', 8, 0, 0, @()),
        @('sst-jon', 'KJV', 'BHSA', 32, 0, 0, @()),
        @('sst-mark', 'KJV', 'NESTLE1904', 41, 1, 4, @()))
}

$final = 'global review + syntax-gated target gloss + attached grammatical words'
$line = '^(?<tier>.+?): (?<correct>[\d,]+)/[\d,]+ \([^)]*\) counting every proposal; [\d,]+/(?<covered>[\d,]+) \([^)]*\) over gold-covered words .*gold recall: [\d,]+/(?<gold>[\d,]+)'
function Number([string] $text) { [int]($text -replace ',', '') }

# The by-word lines, each a run of part/whole counts in a fixed order.
$byWord = [ordered]@{
    coverage = 'Linked', 'Words', 'LinkedOrSupplied', 'Words2', 'Rendered', 'Originals', 'RenderedOrUnrendered', 'Originals2'
    links    = 'ByPair', 'Links', 'Paired', 'Links2'
    absences = 'SuppliedRight', 'SuppliedJudged', 'UnrenderedRight', 'UnrenderedJudged'
    right    = 'SourceRight', 'SourceScope', 'OriginalRight', 'OriginalScope'
}
function Counts([string] $report) {
    $counts = @{}
    foreach ($name in $byWord.Keys) {
        $found = Select-String -LiteralPath $report -Pattern "^by word, ${name}:" | Select-Object -First 1
        if (-not $found) { continue }
        $numbers = [regex]::Matches($found.Line, '([\d,]+)/([\d,]+)') | ForEach-Object { Number $_.Groups[1].Value; Number $_.Groups[2].Value }
        $fields = $byWord[$name]
        for ($i = 0; $i -lt $fields.Count; $i++) { $counts[$fields[$i]] = $numbers[$i] }
    }
    $counts
}
# How far the final links agree with a route text's own, where the route reaches them.
$routeLine = '^route agreement with (?<route>\S+): (?<agreed>[\d,]+)/(?<compared>[\d,]+) .*; [\d,]+/(?<links>[\d,]+) links reached'
function Routes([string] $report) {
    $routes = [ordered]@{}
    foreach ($match in (Select-String -LiteralPath $report -Pattern $routeLine)) {
        $groups = $match.Matches[0].Groups
        $routes[$groups['route'].Value] = @{
            Agreed = Number $groups['agreed'].Value; Compared = Number $groups['compared'].Value; Links = Number $groups['links'].Value
        }
    }
    $routes
}
function RouteSummary([string] $name, $routes) {
    foreach ($route in $routes.Keys) {
        $r = $routes[$route]
        '{0,-10} route {1,-4}: agrees {2}; reaches {3} of the links' -f $name, $route,
            (Ratio $r.Agreed $r.Compared), (Ratio $r.Compared $r.Links)
    }
}
function Ratio($part, $whole) { '{0:N0}/{1:N0} = {2:P2}' -f $part, $whole, ($part / [Math]::Max(1, $whole)) }
function ByWord([string] $name, $c) {
    '{0,-10} by word: coverage {1} -> {2}, original {10} -> {11}; links by pair {3}, paired {4}; supplied {5}; unrendered {6}; right source {7}, original {8}, both {9}' -f $name,
        (Ratio $c.Linked $c.Words), (Ratio $c.LinkedOrSupplied $c.Words), (Ratio $c.ByPair $c.Links), (Ratio $c.Paired $c.Links),
        (Ratio $c.SuppliedRight $c.SuppliedJudged), (Ratio $c.UnrenderedRight $c.UnrenderedJudged),
        (Ratio $c.SourceRight $c.SourceScope), (Ratio $c.OriginalRight $c.OriginalScope),
        (Ratio ($c.SourceRight + $c.OriginalRight) ($c.SourceScope + $c.OriginalScope)),
        (Ratio $c.Rendered $c.Originals), (Ratio $c.RenderedOrUnrendered $c.Originals)
}

Push-Location $snapshot
try {
    foreach ($set in $Runs -split ',') {
        if (-not $passages.ContainsKey($set)) { throw "Unknown run '$set'; use ind, val, val2, held, kjv, kjvval, self, selfstrong, bbe or nwt." }
        $total = @{ Correct = 0; Covered = 0; Gold = 0; SafeCorrect = 0; SafeCovered = 0; Seconds = 0.0 }
        $words = @{}
        $routes = [ordered]@{}
        foreach ($passage in $passages[$set]) {
            $name, $from, $to, $book, $first, $last, $flags = $passage
            $prefix = Join-Path $directory $name
            $arguments = @('Essenthos.Forge.dll', 'evidentia-measure-book', $from, $to, $book) + $flags
            if ($first -gt 0) { $arguments += '--from-chapter', $first, '--to-chapter', $last }
            if ($NeighbourVerses -ge 0) { $arguments += '--neighbour-verses', $NeighbourVerses }
            $arguments += $Extra
            $arguments += '--disagreements', "$prefix.disagreements.json", '--words', "$prefix.words.json", '--absences', "$prefix.absences.json"
            $clock = [Diagnostics.Stopwatch]::StartNew()
            & dotnet @arguments *> "$prefix.report.txt"
            if ($LASTEXITCODE -ne 0) { throw "$name failed; see $prefix.report.txt" }
            $total.Seconds += $clock.Elapsed.TotalSeconds
            '{0,-10} wall {1:N1}s' -f $name, $clock.Elapsed.TotalSeconds
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
            $counts = Counts "$prefix.report.txt"
            ByWord $name $counts
            $passageRoutes = Routes "$prefix.report.txt"
            RouteSummary $name $passageRoutes
            foreach ($route in $passageRoutes.Keys) {
                if (-not $routes.Contains($route)) { $routes[$route] = @{ Agreed = 0; Compared = 0; Links = 0 } }
                foreach ($field in 'Agreed', 'Compared', 'Links') { $routes[$route][$field] += $passageRoutes[$route][$field] }
            }
            foreach ($key in $counts.Keys) { $words[$key] = [int]$words[$key] + $counts[$key] }
        }
        '{0,-10} precision {1,5}/{2,-5} = {3:P2}   recall {1,5}/{4,-5} = {5:P2}   safe tier {6}/{7} = {8:P2}' -f "$set all",
            $total.Correct, $total.Covered, ($total.Correct / [Math]::Max(1, $total.Covered)), $total.Gold,
            ($total.Correct / [Math]::Max(1, $total.Gold)), $total.SafeCorrect, $total.SafeCovered,
            ($total.SafeCorrect / [Math]::Max(1, $total.SafeCovered))
        ByWord "$set all" $words
        RouteSummary "$set all" $routes
        '{0,-10} wall {1:N1}s' -f "$set all", $total.Seconds
    }
}
finally {
    Pop-Location
}
"Reports, disagreements and per-word outcomes: $directory"
