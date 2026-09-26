<#
.SYNOPSIS
    Fetches OpenBible.info's cross references: some 344,800 pairs of verses, each with the votes
    readers of the site have given it.

.DESCRIPTION
    The reader's default set of cross references. Stephen Smith built it from the Treasury of
    Scripture Knowledge and from his own Topical Bible and Twitter Bible Search, and readers have
    voted on each pair since, which is what ranks them: the few a verse is best read beside come
    first, and a pair readers voted down sinks.

    The file is published as one zip that is rebuilt as votes arrive, and has no version of its own.
    So it is pinned by its hash: a different hash means a different set of votes, and nothing is
    replaced until someone decides to take them — pass -Sha256 with the new hash, then record it and
    the date in Resources/OpenBibleCrossReferences/LICENCE.md.

    Two statements carry the terms, and both are re-read before anything is replaced: the file's own
    header line, which says CC-BY, and the page the file is offered on, which says the site's content
    is under a Creative Commons Attribution License. If either stops saying so, this stops.

.EXAMPLE
    ./scripts/fetch-openbible-cross-references.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # The archive as it was read on 2026-09-27, stamped 2026-09-21 in its own header.
    [string] $Sha256 = '83e9db0a08054ed99848531512729f0190dbbac85416f408f2362b5dc36d421d'
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Archive = 'https://a.openbible.info/data/cross-references.zip'
$Page = 'https://www.openbible.info/labs/cross-references/'
$Data = 'cross_references.txt'
$Statement = 'labs-cross-references.html'
$Folder = 'OpenBibleCrossReferences'

$HeaderLicence = 'CC-BY'
$PageLicence = 'Creative Commons Attribution License'

# The release measured 344,799 pairs; far fewer is a truncated download, not a smaller dataset.
$FewestPairs = 300000

$staging = Join-Path ([IO.Path]::GetTempPath()) "openbible-xref-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Reading $Page"
    $pagePath = Join-Path $staging $Statement
    Invoke-WebRequest -Uri $Page -OutFile $pagePath -UseBasicParsing -Headers @{ 'User-Agent' = 'essenthos' }
    $flattened = [IO.File]::ReadAllText($pagePath, [Text.Encoding]::UTF8) -replace '<[^>]+>', ' ' -replace '\s+', ' '
    if (-not $flattened.Contains($PageLicence)) {
        throw "$Page no longer says `"$PageLicence`". The set was taken on that statement, so nothing was " +
              "replaced. Read what the page says now and record it in Resources/$Folder/LICENCE.md before " +
              "fetching again."
    }

    Write-Host "Fetching $Archive"
    $zip = Join-Path $staging 'cross-references.zip'
    Invoke-WebRequest -Uri $Archive -OutFile $zip -Headers @{ 'User-Agent' = 'essenthos' }
    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256) {
        throw "cross-references.zip hashes to $hash, where the pinned archive hashed to $Sha256. OpenBible " +
              "has rebuilt the file with newer votes, so nothing was replaced. To take it, run again with " +
              "-Sha256 $hash and record the hash and the date in Resources/$Folder/LICENCE.md."
    }

    Expand-Archive -Path $zip -DestinationPath $staging -Force
    $header = Get-Content (Join-Path $staging $Data) -TotalCount 1
    if ($header -notmatch [regex]::Escape($HeaderLicence)) {
        throw "The first line of $Data no longer says $HeaderLicence (it reads `"$header`"), while the archive " +
              "hashes as pinned. Two statements about the same bytes that disagree have to be read by a " +
              "person; nothing was replaced."
    }

    $pairs = (Get-Content (Join-Path $staging $Data) | Measure-Object -Line).Lines - 1
    if ($pairs -lt $FewestPairs) {
        throw "$Data holds $pairs pairs where the release measured 344,799. This is a partial download; " +
              "nothing was replaced."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) $Folder
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Copy-Item -Path (Join-Path $staging $Data) -Destination (Join-Path $root $Data) -Force
    Copy-Item -Path $pagePath -Destination (Join-Path $root $Statement) -Force

    $size = (Get-ChildItem $root -File | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N0} pairs, {1:N1} MB in {2}" -f $pairs, $size, $root)
    Write-Host "Header: $header"
    Write-Host "Then run: python scripts/corpus-manifest.py --folders $Folder"
    Write-Host ("The cross references load does nothing while the set's rows are there; run the " +
                "cross-references verb of the Forge to load it into a corpus that has none.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
