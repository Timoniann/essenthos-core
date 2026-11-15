<#
.SYNOPSIS
    Fetches the Wikipedia articles, in English, Ukrainian, German and Spanish, of every Wikidata item
    the world history layer was drawn from.

.DESCRIPTION
    The world events and periods come from three Wikidata queries saved in Resources/WorldHistory
    (wikidata-events.csv, wikidata-inception.csv, wikidata-spans.csv). Each row names its item, but
    not the item's articles, so a reader of a world event had no way out to the article about it.

    Every item those files name is read from the Wikibase API (wbgetentities, props=info|sitelinks),
    fifty at a time and ten requests a minute (the most Wikimedia allows a client that gives no
    contact address), and its article titles on enwiki, ukwiki, dewiki and eswiki are written to
    Resources/WorldHistory/wikidata-sitelinks.tsv, one line per item asked for, sorted by its number:

        item  lastrevid  enwiki  ukwiki  dewiki  eswiki

    An empty title is a Wikipedia with no article on the item. lastrevid is the revision read:
    Special:EntityData/<item>.json?revision=<lastrevid> returns exactly that item however Wikidata
    changes. A merged item is written under the id asked for, which is the id the events carry, with
    the articles of the item it became. Wikidata is CC0.

    The file is written beside the CSVs and moved into place only when it is whole. The forge's
    records-derive reads it to make records/facts/event-wikipedia.

.EXAMPLE
    ./scripts/fetch-wikidata-sitelinks.ps1
#>
[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Folder = Join-Path $ResourcesPath 'WorldHistory'
$Sources = @('wikidata-events.csv', 'wikidata-inception.csv', 'wikidata-spans.csv')
$Target = Join-Path $Folder 'wikidata-sitelinks.tsv'
$Api = 'https://www.wikidata.org/w/api.php'
# Wikimedia's User-Agent policy asks every client to name itself.
$Agent = 'Essenthos/1.0 (non-commercial Bible research project)'
$Wikis = @('enwiki', 'ukwiki', 'dewiki', 'eswiki')
$BatchSize = 50
# Wikimedia allows a client whose User-Agent carries no contact address ten API requests a minute.
$ApiPauseMilliseconds = 6500

$ids = [System.Collections.Generic.SortedSet[int]]::new()
foreach ($source in $Sources) {
    $path = Join-Path $Folder $source
    if (-not (Test-Path $path)) { throw "The world history query result $path is missing; it is in git, restore it." }
    foreach ($row in Import-Csv $path) {
        if ($row.e -match '/entity/Q(\d+)$') { [void]$ids.Add([int]$Matches[1]) }
    }
}
$asked = @($ids | ForEach-Object { "Q$_" })
Write-Host "$($asked.Count) items named by $($Sources -join ', ')"

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add((@('item', 'lastrevid') + $Wikis) -join "`t")
$withArticle = 0
for ($i = 0; $i -lt $asked.Count; $i += $BatchSize) {
    $batch = $asked[$i..([Math]::Min($i + $BatchSize, $asked.Count) - 1)]
    $failures = 0
    while ($true) {
        try {
            $response = Invoke-WebRequest -Uri $Api -Method Post -UseBasicParsing -TimeoutSec 180 `
                -Headers @{ 'User-Agent' = $Agent; 'Accept' = 'application/json' } -Body @{
                    action = 'wbgetentities'; ids = ($batch -join '|'); props = 'info|sitelinks'
                    sitefilter = ($Wikis -join '|'); format = 'json'; formatversion = '2'
                }
            $content = $response.Content
            if ($content -is [byte[]]) { $content = [Text.Encoding]::UTF8.GetString($content) }
            $parsed = $content | ConvertFrom-Json -AsHashtable
            if ($parsed.error) { throw "Wikidata refused the request: $($parsed.error.code): $($parsed.error.info)" }
            break
        }
        catch {
            if (++$failures -ge 6) { throw "Six attempts at $Api failed; the last said: $($_.Exception.Message). Try again later." }
            $wait = [Math]::Pow(2, $failures)
            Write-Host "  retrying in $wait s: $($_.Exception.Message)"
            Start-Sleep -Seconds $wait
        }
    }

    foreach ($id in $batch) {
        $entity = $parsed.entities[$id]
        if (-not $entity -or $entity.missing) { continue }
        $titles = foreach ($wiki in $Wikis) {
            $link = $entity.sitelinks[$wiki]
            if ($link) { ($link.title -replace "[`t`r`n]", ' ') } else { '' }
        }
        if (($titles | Where-Object { $_ }).Count -gt 0) { $withArticle++ }
        $lines.Add((@($id, $entity.lastrevid) + $titles) -join "`t")
    }
    Write-Host "  $([Math]::Min($i + $BatchSize, $asked.Count)) of $($asked.Count)"
    if ($i + $BatchSize -lt $asked.Count) { Start-Sleep -Milliseconds $ApiPauseMilliseconds }
}

$temporary = "$Target.partial"
[IO.File]::WriteAllText($temporary, (($lines -join "`n") + "`n"), [Text.UTF8Encoding]::new($false))
Move-Item -Force $temporary $Target
Write-Host "$($lines.Count - 1) items written to $Target, $withArticle with an article in at least one of $($Wikis -join ', ')"
