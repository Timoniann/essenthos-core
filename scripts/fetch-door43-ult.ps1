<#
.SYNOPSIS
    Fetches the unfoldingWord Literal Text: an English translation with every word tied by hand to
    the Hebrew or Greek word it renders, as unfoldingWord releases it on Door43.

.DESCRIPTION
    The ULT is the only English text besides the Berean whose words are aligned to the originals by
    people, and the alignment is in the file itself: each English word stands inside a \zaln-s
    milestone naming the Hebrew Bible (UHB) or Greek New Testament (UGNT) word it renders, with
    that word's Strong number, lemma and morphology. The same USFM is both the text the corpus
    loads and the statement its links are drawn from.

    It takes a tagged release, not the working branch. Release v90 of 2026-08-17 carries the books
    unfoldingWord has finished checking — 56 of the 66; Numbers, 1 and 2 Chronicles, Ecclesiastes,
    Isaiah, Jeremiah, Ezekiel, Daniel, Amos and Zechariah are still being worked on in the
    repository and are not in it. The archive is pinned to the hash it had when the decision to take
    it was made, so a moved tag is a question for a person and not a download.

    The terms are read at the source before anything is replaced: LICENSE.md must still say CC BY-SA
    4.0 and the manifest must still say the same. Resources/Door43/LICENCE.md says what the licence
    obliges and what was decided about it.

.EXAMPLE
    ./scripts/fetch-door43-ult.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Release = 'v90'
$Archive = "https://git.door43.org/unfoldingWord/en_ult/archive/$Release.zip"
$Sha256 = 'a89587587717d1cb30d1f9d4727a206e2ee5488291a3c2425cc0529ceecad509'

$ExpectedLicence = 'Creative Commons Attribution-ShareAlike 4.0 International License'
$ExpectedRights = 'rights: CC BY-SA 4.0'

# What release v90 holds: its scripture books and the alignment milestones across them. A different
# count is a different release, or a partial download.
$Books = 56
$Milestones = 357632

$staging = Join-Path ([IO.Path]::GetTempPath()) "en_ult-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $zip = Join-Path $staging "en_ult-$Release.zip"
    Write-Host "Fetching $Archive"
    curl.exe --silent --show-error --location --max-time 300 --user-agent 'essenthos' --output $zip $Archive
    if ($LASTEXITCODE -ne 0) {
        throw "$Archive could not be fetched (curl exit $LASTEXITCODE). Nothing was replaced."
    }

    $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256) {
        throw "en_ult $Release hashes to $hash, where it hashed to $Sha256 on 2026-09-25. A release tag " +
              'should not move; read what changed before taking it. Nothing was replaced.'
    }

    Expand-Archive -Path $zip -DestinationPath $staging
    $unpacked = Join-Path $staging 'en_ult'

    $licence = Get-Content (Join-Path $unpacked 'LICENSE.md') -Raw -Encoding UTF8
    if ($licence -notmatch [regex]::Escape($ExpectedLicence)) {
        throw "LICENSE.md in $Release no longer says `"$ExpectedLicence`". The decision to take this " +
              'text and its alignment was made against that statement; nothing was replaced.'
    }

    $manifest = Get-Content (Join-Path $unpacked 'manifest.yaml') -Raw -Encoding UTF8
    if ($manifest -notmatch [regex]::Escape($ExpectedRights)) {
        throw "manifest.yaml in $Release no longer states `"$ExpectedRights`". Nothing was replaced."
    }

    # The front matter is unfoldingWord's introduction to the translation, not scripture.
    $usfm = Get-ChildItem $unpacked -Filter '*.usfm' | Where-Object { $_.Name -notlike 'A0-*' }
    if ($usfm.Count -ne $Books) {
        throw "$Release holds $($usfm.Count) books where it held $Books. Nothing was replaced."
    }

    $spans = ($usfm | ForEach-Object { ([regex]::Matches((Get-Content $_.FullName -Raw), '\\zaln-s')).Count } |
        Measure-Object -Sum).Sum
    if ($spans -ne $Milestones) {
        throw "$Release holds $spans alignment milestones where it held $Milestones. Nothing was replaced."
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'Door43' 'en_ult'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Get-ChildItem $root -File | Remove-Item -Force
    $usfm | Copy-Item -Destination $root -Force
    Copy-Item -Path (Join-Path $unpacked 'LICENSE.md'), (Join-Path $unpacked 'manifest.yaml') -Destination $root -Force

    Write-Host ("{0} books, {1:N0} alignment milestones, in {2}" -f $usfm.Count, $spans, $root)
    Write-Host "Taken from $Archive under CC BY-SA 4.0."
    Write-Host 'Then run: python scripts/corpus-manifest.py, and commit the manifest.'
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
