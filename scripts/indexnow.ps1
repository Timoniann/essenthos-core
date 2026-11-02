<#
.SYNOPSIS
Tells Bing, Yandex and the other IndexNow engines which of the site's pages are new or changed.

.DESCRIPTION
IndexNow is how a site asks those engines to crawl a page now rather than whenever they next read the
sitemap. One submission reaches every participating engine (Bing, Yandex, Naver, Seznam, Yep and
others; Google does not take part and reads the sitemap). Each engine first fetches
<origin>/<key>.txt to check the key, which the proxy serves from INDEXNOW_KEY in the server's .env.

Send only pages that are new, changed or gone. After the first publication that is every page; after
a later one, only the lists that changed, by name (-Sitemap people,places), or the addresses
themselves (-Url). The addresses are read from the live sitemaps, so they are always the site's own
spellings, and are sent in batches of 10,000, the protocol's limit.

-WhatIf reads the sitemaps and says how many addresses it would send, and sends nothing.

.EXAMPLE
./scripts/indexnow.ps1 -Key <key> -All -WhatIf
./scripts/indexnow.ps1 -Key <key> -All
./scripts/indexnow.ps1 -Key <key> -Sitemap records-people,records-places
./scripts/indexnow.ps1 -Key <key> -Url https://essenthos.org/people/moses,https://essenthos.org/uk/people/moses
#>
[CmdletBinding(SupportsShouldProcess, DefaultParameterSetName = 'Sitemap')]
param(
    # The key INDEXNOW_KEY holds on the server.
    [Parameter(Mandatory)] [ValidatePattern('^[A-Za-z0-9-]{8,128}$')] [string] $Key,
    [string] $Origin = 'https://essenthos.org',
    # Every address of every sitemap the index lists.
    [Parameter(ParameterSetName = 'All', Mandatory)] [switch] $All,
    # Sitemaps by name, as /sitemaps/<name>.xml: pages, chapters, verses-genesis, records-people, lexicon-hebrew…
    [Parameter(ParameterSetName = 'Sitemap', Mandatory)] [string[]] $Sitemap,
    [Parameter(ParameterSetName = 'Url', Mandatory)] [string[]] $Url,
    [string] $Endpoint = 'https://api.indexnow.org/indexnow'
)

$ErrorActionPreference = 'Stop'
$Origin = $Origin.TrimEnd('/')
$batch = 10000

function Get-Locations([string] $address) {
    [xml] $xml = (Invoke-WebRequest -Uri $address -UseBasicParsing).Content
    $xml.DocumentElement.ChildNodes | ForEach-Object { $_.loc } | Where-Object { $_ }
}

$check = Invoke-WebRequest -Uri "$Origin/$Key.txt" -UseBasicParsing -SkipHttpErrorCheck
if ($check.StatusCode -ne 200 -or "$($check.Content)".Trim() -ne $Key) {
    throw "$Origin/$Key.txt does not answer with the key; set INDEXNOW_KEY in the server's .env and restart the proxy first."
}

$addresses = switch ($PSCmdlet.ParameterSetName) {
    'All' {
        Write-Host 'Reading the sitemaps, under a minute for the whole site...'
        Get-Locations "$Origin/sitemap.xml" | ForEach-Object { Get-Locations $_ }
    }
    'Sitemap' { $Sitemap | ForEach-Object { Get-Locations "$Origin/sitemaps/$_.xml" } }
    'Url' { $Url }
}
# Not Select-Object -Unique: it compares each address with all the others, half an hour for the whole site.
$seen = [Collections.Generic.HashSet[string]]::new()
$addresses = @($addresses | Where-Object { $seen.Add($_) })
$foreign = @($addresses | Where-Object { -not $_.StartsWith("$Origin/") })
if ($foreign.Count -gt 0) {
    throw "Only $Origin's own pages can be sent with its key; not: $($foreign | Select-Object -First 3)"
}

$host_ = ([uri] $Origin).Host
Write-Host "$($addresses.Count) addresses in $([math]::Ceiling($addresses.Count / $batch)) batch(es) to $Endpoint"
for ($start = 0; $start -lt $addresses.Count; $start += $batch) {
    $part = $addresses[$start..([math]::Min($start + $batch, $addresses.Count) - 1)]
    if (-not $PSCmdlet.ShouldProcess("$($part.Count) addresses from $($part[0])", 'submit to IndexNow')) {
        continue
    }

    $body = @{ host = $host_; key = $Key; keyLocation = "$Origin/$Key.txt"; urlList = $part } | ConvertTo-Json -Compress
    $answer = Invoke-WebRequest -Uri $Endpoint -Method Post -ContentType 'application/json; charset=utf-8' `
        -Body ([Text.Encoding]::UTF8.GetBytes($body)) -UseBasicParsing -SkipHttpErrorCheck
    # 200 taken; 202 taken, key still being checked; 403 key not found at keyLocation; 422 an address
    # not on the host; 429 too many at once — wait and send the rest later.
    Write-Host "batch from $start`: $($answer.StatusCode)"
    if ($answer.StatusCode -notin 200, 202) {
        throw "IndexNow answered $($answer.StatusCode) $($answer.Content); nothing after address $start was sent."
    }
}
