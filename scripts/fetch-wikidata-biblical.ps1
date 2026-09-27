<#
.SYNOPSIS
    Fetches the Wikidata items for the persons, places and other things the Bible names, with the
    names, descriptions, Wikipedia articles and statements a matcher needs to tie them to this
    project's records.

.DESCRIPTION
    Wikidata is the only source that says which Wikipedia article, in which language, is about the
    Zechariah or the Ramah a record means. It is CC0, so nothing it gives carries a condition onto
    the corpus beside it.

    The items are chosen by three questions to the Wikidata Query Service, each saved beside the
    data as the .rq it ran, and by OpenBible's own list:

      present-in-work       present in work (P1441) the Bible, either Testament, the Tanakh, a book
                            or chapter of the Bible, or a Bible translation
      described-by-source   described by source (P1343) one of those works, or a Bible encyclopedia:
                            Archimandrite Nicephorus's, Easton's, the ISBE, Encyclopaedia Biblica
      class                 an instance of human biblical figure, biblical character or biblical
                            place, or of anything below them
      openbible             every QID Resources/OpenBible/ancient.jsonl and modern.jsonl name

    The Jewish Encyclopedias are deliberately not sources: they describe every Jewish person of
    every century, and would bring in thousands of later persons no record here could match.

    The items themselves are then read whole from the Wikibase API, fifty at a time and ten requests
    a minute (the most Wikimedia allows a client that gives no contact address), and cut down to
    labels, aliases and descriptions in the reader's languages, the six Wikipedias, the statements
    in $KeptProperties and every external identifier. Each item keeps its lastrevid, so what was
    read can be fetched again exactly with Special:EntityData/<id>.json?revision=<lastrevid> however
    Wikidata has changed since. Items those statements point at (a father, a region, a class) are
    read once more for their labels only, so a matcher can read "located in Samaria" without a
    second fetch.

    Everything is written to a temporary folder and moved into place only when it is whole.

.EXAMPLE
    ./scripts/fetch-wikidata-biblical.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$FolderName = 'Wikidata'
$Sparql = 'https://query.wikidata.org/sparql'
$Api = 'https://www.wikidata.org/w/api.php'
# Wikimedia's User-Agent policy asks every client to name itself.
$Agent = 'Essenthos/1.0 (non-commercial Bible research project)'

$Languages = @('en', 'uk', 'de', 'es', 'he', 'el', 'ru', 'mul')
$Wikis = @('enwiki', 'ukwiki', 'dewiki', 'eswiki', 'ruwiki', 'hewiki')

# Statements kept on each item besides its external identifiers.
$KeptProperties = [ordered]@{
    P31 = 'instance of'; P279 = 'subclass of'; P21 = 'sex or gender'
    P22 = 'father'; P25 = 'mother'; P3373 = 'sibling'; P26 = 'spouse'; P40 = 'child'
    P1038 = 'relative'; P53 = 'family'; P106 = 'occupation'; P39 = 'position held'
    P97 = 'noble title'; P1449 = 'nickname'; P735 = 'given name'; P1559 = 'name in native language'
    P1705 = 'native label'; P2561 = 'name'; P4970 = 'alternative name'; P138 = 'named after'
    P569 = 'date of birth'; P570 = 'date of death'; P19 = 'place of birth'; P20 = 'place of death'
    P119 = 'place of burial'; P1441 = 'present in work'; P1343 = 'described by source'
    P1080 = 'from narrative universe'; P973 = 'described at URL'; P460 = 'said to be the same as'
    P1889 = 'different from'; P361 = 'part of'; P527 = 'has part'
    P625 = 'coordinate location'; P131 = 'located in the administrative territorial entity'
    P276 = 'location'; P706 = 'located in/on physical feature'; P17 = 'country'
    P30 = 'continent'; P1082 = 'population'; P571 = 'inception'; P576 = 'dissolved'
    P2348 = 'time period'; P1435 = 'heritage designation'; P1448 = 'official name'
}

# What a present-in-work or described-by-source value may be: the Bible, a Testament, the Tanakh,
# a book, chapter or translation of it, anything part of those, or an edition of them.
$BibleWorks = @'
  { VALUES ?work { wd:Q1845 wd:Q19786 wd:Q18813 wd:Q83367 } }
  UNION { VALUES ?class { wd:Q29154430 wd:Q29154515 wd:Q86860 } ?work wdt:P31/wdt:P279* ?class . }
  UNION { VALUES ?root { wd:Q1845 wd:Q19786 wd:Q18813 wd:Q83367 } ?work wdt:P361+ ?root . }
  UNION { VALUES ?root { wd:Q1845 wd:Q19786 wd:Q18813 wd:Q83367 } ?work wdt:P629 ?root . }
'@

$Queries = [ordered]@{
    'present-in-work' = @"
SELECT DISTINCT ?item WHERE {
$BibleWorks
  ?item wdt:P1441 ?work .
}
"@
    'described-by-source' = @"
SELECT DISTINCT ?item WHERE {
  {
    # Nicephorus, Easton's (the work and its 1897 edition), the ISBE, both Encyclopaedias Biblica
    VALUES ?work { wd:Q4086271 wd:Q889391 wd:Q48606171 wd:Q2756537 wd:Q5375654 wd:Q5375655 }
  } UNION {
$BibleWorks
  }
  ?item wdt:P1343 ?work .
}
"@
    'class' = @'
SELECT DISTINCT ?item WHERE {
  hint:Query hint:optimizer "None" .
  # human biblical figure, biblical character, biblical place
  VALUES ?root { wd:Q20643955 wd:Q12405827 wd:Q12404340 }
  ?class wdt:P279* ?root .
  ?item wdt:P31 ?class .
}
'@
}

# The classes that make an item a person or a place, for the kind written on each item. It is a
# convenience for the matcher and no more; the item's own P31 is kept beside it.
$PersonClasses = @('Q5', 'Q20643955', 'Q12405827', 'Q15632617', 'Q21070568', 'Q3236487', 'Q621421', 'Q8031550')
$PlaceClassRoot = 'Q12404340'

$PageSize = 10000
$BatchSize = 50
$MinimumItems = 4000
$PauseMilliseconds = 250
# Wikimedia allows a client whose User-Agent carries no contact address ten API requests a minute.
# The pace is the whole of the politeness: maxlag is not sent, because on Wikidata it reports the
# Query Service's replication lag, which is meant to slow editors and would stall a reader for as
# long as that lag lasts.
$ApiPauseMilliseconds = 6500

function Invoke-Polite {
    param([string] $Uri, [hashtable] $Body, [string] $Accept = 'application/json', [string] $CachePath)

    if ($CachePath -and (Test-Path $CachePath)) { return Get-Content $CachePath -Raw | ConvertFrom-Json -AsHashtable }

    $failures = 0
    while ($true) {
        try {
            $response = Invoke-WebRequest -Uri $Uri -Method Post -Body $Body -UseBasicParsing -TimeoutSec 180 `
                -Headers @{ 'User-Agent' = $Agent; 'Accept' = $Accept }
            # SPARQL results come back as bytes: PowerShell does not take their media type for text.
            $content = $response.Content
            if ($content -is [byte[]]) { $content = [Text.Encoding]::UTF8.GetString($content) }
            $parsed = $content | ConvertFrom-Json -AsHashtable
            if ($parsed.error) {
                throw "Wikidata refused the request: $($parsed.error.code): $($parsed.error.info)"
            }
            if ($CachePath) { [IO.File]::WriteAllText($CachePath, $content, [Text.UTF8Encoding]::new($false)) }
            return $parsed
        }
        catch {
            if (++$failures -ge 6) {
                throw "Six attempts at $Uri failed; the last said: $($_.Exception.Message). Try again later; the batches already read are kept and a re-run starts after them."
            }
            $wait = [Math]::Pow(2, $failures)
            $retryAfter = $_.Exception.Response.Headers.RetryAfter.Delta
            if ($retryAfter) { $wait = [Math]::Max($wait, $retryAfter.TotalSeconds) }
            Write-Host "  retrying in $wait s: $($_.Exception.Message)"
            Start-Sleep -Seconds $wait
        }
    }
}

function Get-SparqlItems {
    param([string] $Query)

    $items = [System.Collections.Generic.List[string]]::new()
    for ($offset = 0; ; $offset += $PageSize) {
        $paged = "$Query`nORDER BY ?item`nLIMIT $PageSize OFFSET $offset"
        $result = Invoke-Polite -Uri $Sparql -Body @{ query = $paged } -Accept 'application/sparql-results+json'
        $rows = $result.results.bindings
        foreach ($row in $rows) { $items.Add(($row.item.value -replace '^.*/entity/', '')) }
        if ($rows.Count -lt $PageSize) { return $items }
        Start-Sleep -Milliseconds $PauseMilliseconds
    }
}

function Get-Entities {
    param([string[]] $Ids, [string] $Props)

    # Keyed by the id asked for: a merged item answers under its old id with the item it became.
    $entities = [ordered]@{}
    for ($i = 0; $i -lt $Ids.Count; $i += $BatchSize) {
        $batch = $Ids[$i..([Math]::Min($i + $BatchSize, $Ids.Count) - 1)]
        $key = [Convert]::ToHexString([Security.Cryptography.SHA1]::HashData([Text.Encoding]::UTF8.GetBytes("$Props $batch")))
        $cachePath = Join-Path $Cache "$key.json"
        $cached = Test-Path $cachePath
        $result = Invoke-Polite -Uri $Api -CachePath $cachePath -Body @{
            action = 'wbgetentities'; ids = ($batch -join '|'); props = $Props
            languages = ($Languages -join '|'); sitefilter = ($Wikis -join '|')
            format = 'json'; formatversion = '2'
        }
        foreach ($asked in $result.entities.Keys) {
            $entity = $result.entities[$asked]
            if (-not $entity.missing) { $entities[$asked] = $entity }
        }
        if (($i / $BatchSize) % 20 -eq 0) { Write-Host "  $([Math]::Min($i + $BatchSize, $Ids.Count)) of $($Ids.Count)" }
        if (-not $cached) { Start-Sleep -Milliseconds $ApiPauseMilliseconds }
    }
    return $entities
}

function ConvertTo-Map {
    param($Dictionary, [scriptblock] $Select)

    # The API sends an empty JSON array, not an object, where an item has no labels or sitelinks.
    $map = [ordered]@{}
    if ($Dictionary -is [System.Collections.IDictionary]) {
        foreach ($key in $Dictionary.Keys | Sort-Object) { $map[$key] = & $Select $Dictionary[$key] }
    }
    return $map
}

function ConvertTo-Value {
    param($Snak)

    if ($Snak.snaktype -ne 'value') { return $Snak.snaktype }
    $value = $Snak.datavalue.value
    switch ($Snak.datavalue.type) {
        'wikibase-entityid' { return $value.id }
        'globecoordinate' { return [ordered]@{ lat = $value.latitude; lon = $value.longitude; precision = $value.precision } }
        'time' { return [ordered]@{ time = $value.time; precision = $value.precision } }
        'monolingualtext' { return [ordered]@{ text = $value.text; language = $value.language } }
        'quantity' { return $value.amount }
        default { return $value }
    }
}

function ConvertTo-Claims {
    param($Claims)

    $kept = [ordered]@{}
    $identifiers = [ordered]@{}
    foreach ($property in $Claims.Keys) {
        $statements = @($Claims[$property] | Where-Object { $_.rank -ne 'deprecated' })
        if ($statements.Count -eq 0) { continue }
        $isIdentifier = $statements[0].mainsnak.datatype -eq 'external-id'
        if (-not $isIdentifier -and -not $KeptProperties.Contains($property)) { continue }

        $values = foreach ($statement in $statements) {
            $value = ConvertTo-Value $statement.mainsnak
            if ($isIdentifier) { $value; continue }
            $entry = [ordered]@{ value = $value }
            if ($statement.rank -eq 'preferred') { $entry.rank = 'preferred' }
            if ($statement.qualifiers) {
                $qualifiers = [ordered]@{}
                foreach ($qualifier in $statement.qualifiers.Keys) {
                    $qualifiers[$qualifier] = @($statement.qualifiers[$qualifier] | ForEach-Object { ConvertTo-Value $_ })
                }
                $entry.qualifiers = $qualifiers
            }
            $entry
        }
        if ($isIdentifier) { $identifiers[$property] = @($values) } else { $kept[$property] = @($values) }
    }
    return $kept, $identifiers
}

function Get-Kind {
    param($Classes, $Claims, [string[]] $Seeds, [hashtable] $PlaceClasses)

    if ($Claims.Contains('P21') -or $Claims.Contains('P22') -or $Claims.Contains('P25') -or
        ($Classes | Where-Object { $PersonClasses -contains $_ })) { return 'person' }
    if ($Claims.Contains('P625') -or $Seeds -contains 'openbible' -or
        ($Classes | Where-Object { $PlaceClasses.ContainsKey($_) })) { return 'place' }
    return 'other'
}

$resources = Resolve-Path $ResourcesPath
$root = Join-Path $resources $FolderName
$staging = Join-Path ([IO.Path]::GetTempPath()) "wikidata-biblical-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null
$fetched = Get-Date -Format 'yyyy-MM-dd'
# The API answers kept until the fetch completes, so a run stopped by Wikidata's rate limit
# resumes where it stopped instead of reading five thousand items again. Kept for a day at most.
$Cache = Join-Path ([IO.Path]::GetTempPath()) 'essenthos-wikidata-biblical'
if ((Test-Path $Cache) -and (Get-Item $Cache).CreationTime -lt (Get-Date).AddDays(-1)) { Remove-Item -Recurse -Force $Cache }
New-Item -ItemType Directory -Force -Path $Cache | Out-Null

try {
    $seeds = @{}
    foreach ($name in $Queries.Keys) {
        Write-Host "Asking the Query Service: $name"
        Set-Content -Path (Join-Path $staging "$name.rq") -Value $Queries[$name] -Encoding utf8
        foreach ($id in Get-SparqlItems $Queries[$name]) {
            if (-not $seeds.ContainsKey($id)) { $seeds[$id] = [System.Collections.Generic.List[string]]::new() }
            $seeds[$id].Add($name)
        }
        Start-Sleep -Milliseconds $PauseMilliseconds
    }

    $openBible = Join-Path $resources 'OpenBible'
    foreach ($file in 'ancient.jsonl', 'modern.jsonl') {
        $path = Join-Path $openBible $file
        if (-not (Test-Path $path)) { throw "$path is missing; run scripts/fetch-openbible.ps1 first, since its QIDs are part of what this fetches." }
        foreach ($match in [regex]::Matches((Get-Content $path -Raw), '"(Q\d+)"')) {
            $id = $match.Groups[1].Value
            if (-not $seeds.ContainsKey($id)) { $seeds[$id] = [System.Collections.Generic.List[string]]::new() }
            if (-not $seeds[$id].Contains('openbible')) { $seeds[$id].Add('openbible') }
        }
    }

    Write-Host 'Reading the classes below biblical place'
    $placeClasses = @{}
    $placeQuery = "SELECT DISTINCT ?item WHERE { ?item wdt:P279* wd:$PlaceClassRoot . }"
    foreach ($id in Get-SparqlItems $placeQuery) { $placeClasses[$id] = $true }

    $ids = @($seeds.Keys | Sort-Object { [long]$_.Substring(1) })
    if ($ids.Count -lt $MinimumItems) {
        throw "The Query Service returned $($ids.Count) items, below the $MinimumItems a complete answer gives; a query probably timed out. Nothing was replaced."
    }

    Write-Host "Reading $($ids.Count) items"
    $entities = Get-Entities $ids 'info|labels|aliases|descriptions|sitelinks|claims'
    $read = [ordered]@{}
    foreach ($asked in $entities.Keys) {
        $entity = $entities[$asked]
        if (-not $read.Contains($entity.id)) {
            $read[$entity.id] = @{ entity = $entity; seeds = [System.Collections.Generic.List[string]]::new(); redirects = @() }
        }
        foreach ($seed in $seeds[$asked]) { if (-not $read[$entity.id].seeds.Contains($seed)) { $read[$entity.id].seeds.Add($seed) } }
        if ($asked -ne $entity.id) { $read[$entity.id].redirects += $asked }
    }

    $referenced = [System.Collections.Generic.HashSet[string]]::new()
    $items = [System.Collections.Generic.List[object]]::new()
    foreach ($id in $read.Keys | Sort-Object { [long]$_.Substring(1) }) {
        $entity = $read[$id].entity
        $claims, $identifiers = ConvertTo-Claims $entity.claims
        $classes = @($claims.P31 | Where-Object { $_ } | ForEach-Object { $_.value } | Where-Object { $_ -is [string] })
        $itemSeeds = @($read[$id].seeds)
        foreach ($property in $claims.Keys) {
            foreach ($entry in $claims[$property]) {
                if ($entry.value -is [string] -and $entry.value -match '^Q\d+$') { [void]$referenced.Add($entry.value) }
            }
        }
        $item = [ordered]@{
            id = $id
            lastrevid = $entity.lastrevid
            modified = $entity.modified
            kind = Get-Kind $classes $claims $itemSeeds $placeClasses
            seeds = $itemSeeds
            labels = ConvertTo-Map $entity.labels { param($l) $l.value }
            descriptions = ConvertTo-Map $entity.descriptions { param($d) $d.value }
            aliases = ConvertTo-Map $entity.aliases { param($a) @($a | ForEach-Object { $_.value }) }
            sitelinks = ConvertTo-Map $entity.sitelinks { param($s) $s.title }
            claims = $claims
            identifiers = $identifiers
        }
        if ($read[$id].redirects) { $item.redirectedFrom = @($read[$id].redirects) }
        $items.Add($item)
    }
    $missing = @($ids | Where-Object { -not $entities.Contains($_) })

    foreach ($id in $read.Keys) { [void]$referenced.Remove($id) }
    $labelIds = @($referenced | Sort-Object { [long]$_.Substring(1) })
    Write-Host "Reading the labels of $($labelIds.Count) items they point at"
    $pointedAt = Get-Entities $labelIds 'labels|descriptions'
    $labels = foreach ($asked in $pointedAt.Keys) {
        [ordered]@{
            id = $asked
            labels = ConvertTo-Map $pointedAt[$asked].labels { param($l) $l.value }
            descriptions = ConvertTo-Map $pointedAt[$asked].descriptions { param($d) $d.value }
        }
    }

    $utf8 = [Text.UTF8Encoding]::new($false)
    [IO.File]::WriteAllLines((Join-Path $staging 'items.jsonl'),
        [string[]]@($items | ForEach-Object { $_ | ConvertTo-Json -Depth 12 -Compress }), $utf8)
    [IO.File]::WriteAllLines((Join-Path $staging 'referenced.jsonl'),
        [string[]]@($labels | ForEach-Object { $_ | ConvertTo-Json -Depth 6 -Compress }), $utf8)

    $summary = [ordered]@{
        fetched = $fetched
        items = $items.Count
        missing = $missing
        redirected = @($items | Where-Object { $_.Contains('redirectedFrom') }).Count
        referenced = @($labels).Count
        kinds = [ordered]@{}
        seeds = [ordered]@{}
        sitelinks = [ordered]@{}
    }
    foreach ($kind in 'person', 'place', 'other') {
        $ofKind = @($items | Where-Object { $_.kind -eq $kind })
        $summary.kinds[$kind] = $ofKind.Count
        $summary.sitelinks[$kind] = [ordered]@{}
        foreach ($wiki in $Wikis) {
            $summary.sitelinks[$kind][$wiki] = @($ofKind | Where-Object { $_.sitelinks.Contains($wiki) }).Count
        }
    }
    foreach ($name in @($Queries.Keys) + 'openbible') {
        $summary.seeds[$name] = @($items | Where-Object { $_.seeds -contains $name }).Count
    }
    $summary | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $staging 'summary.json') -Encoding utf8

    $licence = @"
# Wikidata: the Bible's persons, places and things

Source: the Wikidata Query Service <$Sparql> and the Wikibase API <$Api>,
fetched on $fetched by ``scripts/fetch-wikidata-biblical.ps1``. The three queries that chose the
items, besides the QIDs OpenBible names, are the ``.rq`` files beside this one; each item in ``items.jsonl`` records the ``lastrevid`` it was read at, so
<https://www.wikidata.org/wiki/Special:EntityData/Q1.json?revision=N> returns exactly what was read.

## Licence: CC0 1.0

Wikidata's licensing policy (read 2026-09-27) places all structured data in the main, Property and
Lexeme namespaces under **Creative Commons CC0 1.0 Universal**: no rights reserved, no attribution required. The
item data here (labels, aliases, descriptions, statements, sitelink titles) is that structured data.
It is attributed anyway, because a reader should be able to see where a link came from.

- <https://www.wikidata.org/wiki/Wikidata:Licensing>
- <https://creativecommons.org/publicdomain/zero/1.0/>

Text in Wikidata's other namespaces, and the Wikipedia articles the sitelinks name, are CC BY-SA
4.0 and are **not** covered. None of it is in this folder: an article is only ever linked to.
"@
    Set-Content -Path (Join-Path $staging 'LICENCE.md') -Value $licence -Encoding utf8

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    foreach ($file in Get-ChildItem $staging -File) {
        Move-Item -Path $file.FullName -Destination (Join-Path $root $file.Name) -Force
    }
    Remove-Item -Recurse -Force -Path $Cache
}
finally {
    Remove-Item -Recurse -Force -Path $staging -ErrorAction SilentlyContinue
}

Write-Host "Saved $($items.Count) items ($($summary.kinds.person) persons, $($summary.kinds.place) places, $($summary.kinds.other) other) and $($summary.referenced) referenced labels to $root"
