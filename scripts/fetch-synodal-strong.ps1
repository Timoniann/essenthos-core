<#
.SYNOPSIS
    Fetches the Strong-tagged Russian Synodal from swmail/RST, which the mapping reads and the corpus
    never loads.

.DESCRIPTION
    One OSIS file, the whole Synodal with Bob Jones University's 1996 Pierce-Strong numbering on
    every word, and the SWORD module configuration beside it. The numbering's only statement of terms
    permits use of the work unmodified and for a purpose this project does not claim; the owner
    decided on 2026-09-13 to use it for the mapping alone, so the file is an input to the
    `synodal-strong` batch command and nothing else. Resources/SynodalStrong/LICENCE.md has the notice
    in full and what that decision means for what is stored.

    The commit is pinned and both files are checked against the hashes they had when that decision
    was taken: a different file is a different input, and the measurements recorded against this one
    would no longer describe it.

    The notice is re-read at its source before anything is replaced. If the page no longer carries
    it, the terms under which this was decided have moved, and that is the owner's to read, not a
    download's.

.EXAMPLE
    ./scripts/fetch-synodal-strong.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Commit = '78d6bbf343de07e06d07cf8bd32b7a38d85aea36'
$Raw = "https://raw.githubusercontent.com/swmail/RST/$Commit"

# What each file was on 2026-09-13, when the mapping was measured against it.
$Expected = [ordered]@{
    'RSTE_verse_words.xml' = 'e82a07241baecf08e7d94628a858c17c7cb675b2c712b65318f56d64513e13ca'
    'rsthcs.conf'          = '7757641a5d0c59aeca367612f09a6b96b360be6a2a9348b04ff75f2a78b2008c'
}

# The page that prints Bob Jones University's notice against this very edition, entry 40.
$NoticePage = 'http://www.clavmon.cz/ultranet/bw/bwpopisVerzi.htm'
$NoticePhrases = @('Bob', 'Jones University', 'in no way modified', 'Pierce-Strong')

$staging = Join-Path ([IO.Path]::GetTempPath()) "synodal-strong-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Reading the notice at $NoticePage"
    $notice = [Text.Encoding]::GetEncoding('windows-1250').GetString(
        (Invoke-WebRequest -Uri $NoticePage -UseBasicParsing -Headers @{ 'User-Agent' = 'essenthos' }).RawContentStream.ToArray())
    $flattened = $notice -replace '\s+', ' '
    foreach ($phrase in $NoticePhrases) {
        if ($flattened -notmatch [regex]::Escape($phrase)) {
            throw "$NoticePage no longer carries `"$phrase`". The notice is the only statement of " +
                  "terms for this numbering and the owner's decision to use it was taken against it, so " +
                  "nothing was replaced. Read what the page says now and record it in " +
                  "Resources/SynodalStrong/LICENCE.md before fetching again."
        }
    }

    foreach ($file in $Expected.Keys) {
        $path = Join-Path $staging $file
        Invoke-WebRequest -Uri "$Raw/$file" -OutFile $path -Headers @{ 'User-Agent' = 'essenthos' }
        $hash = (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant()
        if ($hash -ne $Expected[$file]) {
            throw "$file at swmail/RST $Commit hashes to $hash, where it hashed to $($Expected[$file]). " +
                  "The mapping was measured against the second, so nothing was replaced; find out " +
                  "what changed before using the file."
        }
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'SynodalStrong'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    foreach ($file in $Expected.Keys) {
        Copy-Item -Path (Join-Path $staging $file) -Destination $root -Force
    }

    $size = (Get-ChildItem $root -File | Where-Object Name -in $Expected.Keys |
        Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0:N1} MB in {1}, from swmail/RST {2}" -f $size, $root, $Commit)
    Write-Host "Then run: python scripts/corpus-manifest.py --check"
    Write-Host ('Nothing is loaded by this. The links are drawn by "dotnet run --project Essenthos.Forge -- ' +
                'synodal-strong", and the edition itself is never a text of the corpus.')
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
