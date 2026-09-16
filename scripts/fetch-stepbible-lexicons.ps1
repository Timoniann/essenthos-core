<#
.SYNOPSIS
    Fetches STEPBible's four Strong-indexed lexicon exports for evaluation.

.DESCRIPTION
    The files are pinned to a source commit, retain the publisher's README and carry their own
    headers.  They are downloaded as candidate source material; this script does not load them
    into the corpus.  TBESH explicitly says its definitions need Online Bible's permission before
    they are applied, so its presence on disk is not permission to publish or use those meanings.

.EXAMPLE
    ./scripts/fetch-stepbible-lexicons.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Commit = 'ae39711d7843b2902d54993e432de9c12d6a4b9a'
$Repository = 'https://raw.githubusercontent.com/STEPBible/STEPBible-Data'
$Files = [ordered]@{
    'TBESH.txt' = 'Lexicons/TBESH - Translators Brief lexicon of Extended Strongs for Hebrew - STEPBible.org CC BY.txt'
    'TBESG.txt' = 'Lexicons/TBESG - Translators Brief lexicon of Extended Strongs for Greek - STEPBible.org CC BY.txt'
    'TFLSJ-0-5624.txt' = 'Lexicons/TFLSJ  0-5624 - Translators Formatted full LSJ Bible lexicon - STEPBible.org CC BY.txt'
    'TFLSJ-extra.txt' = 'Lexicons/TFLSJ extra - Translators Formatted full LSJ Bible lexicon - STEPBible.org CC BY.txt'
}
$ExpectedHeader = 'Data created by www.STEPBible.org based on work at'
$PermissionWarning = 'Permission should be gained from Online Bible before these definitions are applied in any project.'

$root = Join-Path (Resolve-Path $ResourcesPath) 'STEPBibleLexicons'
$staging = Join-Path ([IO.Path]::GetTempPath()) "stepbible-lexicons-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $readme = Join-Path $staging 'README-upstream.md'
    Invoke-WebRequest -Uri "$Repository/$Commit/README.md" -OutFile $readme
    if ((Get-Content -Raw $readme) -notmatch 'CC\s*BY\s*4\.0') {
        throw 'The pinned STEPBible README no longer states CC BY 4.0. Read its current terms before acquiring the lexicons.'
    }

    foreach ($entry in $Files.GetEnumerator()) {
        $relative = $entry.Value
        $encoded = [uri]::EscapeDataString($relative).Replace('%2F', '/')
        $destination = Join-Path $staging $entry.Key
        Write-Host "Fetching $relative"
        Invoke-WebRequest -Uri "$Repository/$Commit/$encoded" -OutFile $destination
        $header = (Get-Content -Path $destination -TotalCount 40) -join "`n"
        if ($header -notlike "*$ExpectedHeader*") {
            throw "$relative no longer carries the publisher header this acquisition was reviewed against."
        }
        if ($entry.Key -eq 'TBESH.txt' -and $header -notlike "*$PermissionWarning*") {
            throw 'TBESH no longer carries its Online Bible permission warning. Re-read the source before replacing the candidate copy.'
        }
    }

    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Get-ChildItem -Path $staging -File | Move-Item -Destination $root -Force
    $licence = @"
# STEPBible Strong-indexed lexicons

Fetched from <https://github.com/STEPBible/STEPBible-Data>, commit ``$Commit``, on $(Get-Date -Format 'yyyy-MM-dd').

| stored file | upstream dataset | status |
|---|---|---|
| ``TBESG.txt`` | Translators Brief lexicon of Extended Strongs for Greek | candidate, not loaded |
| ``TBESH.txt`` | Translators Brief lexicon of Extended Strongs for Hebrew | **quarantined**, not loaded |
| ``TFLSJ-0-5624.txt`` | Translators Formatted full LSJ Bible lexicon 0–5624 | candidate, not loaded |
| ``TFLSJ-extra.txt`` | Translators Formatted full LSJ Bible lexicon extra | candidate, not loaded |

## Terms read from the source

The repository README and the headers in all four files say **CC BY 4.0** and ask for credit to
STEP Bible / Tyndale House, Cambridge. ``README-upstream.md`` is the source README copied with
these bytes. The individual file headers also ask readers not to redistribute their copies, while
the README permits mirroring with a link back; the project records the more restrictive practical
condition by keeping the upstream URL and attribution beside every future derivative.

**TBESH has an additional, controlling warning.** Its header says its Abridged BDB-derived
definitions are "for guidance only" and that permission should be gained from Online Bible before
they are applied in any project. This copy is retained solely for evaluation. No TBESH definition,
meaning, or derived data may be loaded or published until that permission is obtained and recorded.

These files describe lemmas and Strong-compatible identifiers. They do not assert an alignment
between individual words in a translation and original-language words.
"@
    Set-Content -Path (Join-Path $root 'LICENCE.md') -Value $licence -Encoding utf8
}
finally {
    Remove-Item -Recurse -Force -Path $staging -ErrorAction SilentlyContinue
}

$total = (Get-ChildItem -Path $root -File | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("{0} lexicon files, {1:N1} MB in {2}" -f $Files.Count, $total, $root)
Write-Warning 'TBESH is quarantined as a candidate: its source requires Online Bible permission before its definitions are applied.'
