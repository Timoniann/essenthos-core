<#
.SYNOPSIS
    Fetches Dillmann's Lexicon Linguae Aethiopicae as Beta maṣāḥǝft digitised it: 13,727 entries, one
    TEI file each, which give the Ge'ez words their own meanings in Latin and the Greek Dillmann set
    beside them.

.DESCRIPTION
    August Dillmann published the lexicon in Leipzig in 1865 and it is long out of copyright. The
    digital edition is the Hiob-Ludolf-Zentrum's (University of Hamburg), made in the TraCES project
    and kept at github.com/BetaMasaheft/DillmannData. Its licence is not the book's: every entry's
    header states CC BY-NC-SA 4.0, which binds what the corpus derives from it — the lemma a Ge'ez word
    is matched to and anything rendered from Dillmann's Latin. The owner accepted that ShareAlike on
    the Ge'ez meaning layer only on 2026-09-25. The repository's repo.xml says GNU-LGPL and it has no
    licence file at its root; the most restrictive statement is the one taken, and
    Resources/Dillmann/LICENCE.md records all of them.

    One archive of the repository at a pinned commit, of which only the entry folders are kept. Each
    entry's header is checked for the licence before anything replaces what is there, and the count
    and total size of the entries are checked against the commit, so a partial or changed download is
    refused rather than loaded.

.EXAMPLE
    ./scripts/fetch-dillmann.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# The commit Resources/Dillmann/LICENCE.md was read at. Move it only after reading the headers again.
$Commit = '44f2da86568c4094917d8374b9fe61288715e606'

$Repository = 'https://github.com/BetaMasaheft/DillmannData'
$Archive = "https://codeload.github.com/BetaMasaheft/DillmannData/zip/$Commit"

$ExpectedLicence = 'Creative Commons Attribution-ShareAlike Non Commercial 4.0'

# The folders the repository keeps its entries in: eleven of Dillmann's and two of TraCES additions.
$Folders = @('1', '2', '3', '4', '5', '6', '7', '8', '9', '10', '11', 'new', 'new1')

# What those folders hold at the commit.
$ExpectedEntries = 13727
$ExpectedBytes = 76300139

# Kept beside the entries because each makes a statement about the terms: the package descriptor
# names a different licence, and the README says where the data comes from and who it is for.
$Statements = @('README.md', 'repo.xml')

$staging = Join-Path ([IO.Path]::GetTempPath()) "dillmann-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Fetching $Repository at $($Commit.Substring(0, 7))"
    $zip = Join-Path $staging 'DillmannData.zip'
    Invoke-WebRequest -Uri $Archive -OutFile $zip -Headers @{ 'User-Agent' = 'essenthos' }
    Expand-Archive -Path $zip -DestinationPath $staging
    $unpacked = Join-Path $staging "DillmannData-$Commit"

    $entries = @($Folders | ForEach-Object { Get-ChildItem (Join-Path $unpacked $_) -File -Filter '*.xml' })
    $bytes = ($entries | Measure-Object -Property Length -Sum).Sum
    if ($entries.Count -ne $ExpectedEntries -or $bytes -ne $ExpectedBytes) {
        throw "The archive holds $($entries.Count) entries in $bytes bytes where the commit holds " +
              "$ExpectedEntries in $ExpectedBytes. A pinned commit cannot change, so the download is " +
              "partial or not the one read; nothing was replaced."
    }

    foreach ($entry in $entries) {
        # The <licence> element runs over line breaks, so the header's whitespace is folded first.
        $header = ((Get-Content $entry.FullName -Raw -Encoding UTF8) -split '</teiHeader>')[0] -replace '\s+', ' '
        if ($header -notmatch [regex]::Escape($ExpectedLicence)) {
            throw "$($entry.Directory.Name)/$($entry.Name) no longer states `"$ExpectedLicence`" in its " +
                  "header. That statement is the permission the lexicon is held under, so nothing was " +
                  "replaced. Read what it says now and record it in Resources/Dillmann/LICENCE.md before " +
                  "fetching again."
        }
    }

    $target = Join-Path (Resolve-Path $ResourcesPath) 'Dillmann'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    foreach ($folder in $Folders) {
        $into = Join-Path $target $folder
        if (Test-Path $into) {
            Remove-Item -Recurse -Force $into
        }
        Move-Item -Path (Join-Path $unpacked $folder) -Destination $into
    }
    foreach ($statement in $Statements) {
        Copy-Item -Path (Join-Path $unpacked $statement) -Destination $target -Force
    }

    Write-Host ("{0:N0} entries, {1:N1} MiB, every header CC BY-NC-SA 4.0, in {2}" -f $ExpectedEntries, ($bytes / 1MB), $target)
    Write-Host "Taken from $Repository at $Commit"
    Write-Host "Then run: python scripts/corpus-manifest.py --folders Dillmann, and commit the manifest."
    Write-Host "Load it into a corpus already loaded with: dotnet run --project Essenthos.Forge -c Release -- dillmann"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
