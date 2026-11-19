<#
.SYNOPSIS
    Fetches Wade Maxfield's Strong numbers on the American Standard Version (Cross Word Project) into
    Resources/AmericanStandard1901Strong.

.DESCRIPTION
    CWPfiles/Bibles/ASV.bbl in Maxfield's repository wmaxfield/CWP-Desktop-QT is the ASV of 1901 as one
    SQLite database, a row per English word with the Strong number of the word it renders. The corpus
    loads the ASV's words from eBible and lays these numbers onto them as a supporting layer (TSK-1890).

    The file is pinned to commit efd6ffc8 and checked by hash: the decision to take it and the numbers
    measured on it were made against those bytes. Its terms are in the database itself (table
    copyright, "released by him for any purpose"); the Forge's reader refuses a file that no longer
    states them, and Resources/AmericanStandard1901Strong/LICENCE.md quotes them. The repository has no
    licence file of its own.

    A file already in place with the pinned hash is left alone. One with another hash is not replaced:
    say so and stop, since replacing it is a decision, not a repair.

.EXAMPLE
    ./scripts/fetch-asv-strong.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Commit = 'efd6ffc8264aca410c203bf5dc175b3d01863cda'
$Source = "https://raw.githubusercontent.com/wmaxfield/CWP-Desktop-QT/$Commit/CWPfiles/Bibles/ASV.bbl"
$Folder = 'AmericanStandard1901Strong'
$FileName = 'ASV.bbl'
$Sha256 = 'd997dd6c6533a12fcb84eb052d02774452c145bc3e404cf10a66ca418e6e7e88'

$targetFolder = Join-Path (Resolve-Path $ResourcesPath) $Folder
$targetFile = Join-Path $targetFolder $FileName

if (Test-Path $targetFile) {
    $present = (Get-FileHash -Algorithm SHA256 $targetFile).Hash.ToLowerInvariant()
    if ($present -eq $Sha256) {
        Write-Host "$targetFile is already the file of commit $Commit."
        return
    }

    throw "$targetFile hashes to $present, not to the pinned $Sha256. Nothing was replaced: " +
          "find out where it came from before removing it."
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "asv-strong-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    $downloaded = Join-Path $staging $FileName
    Invoke-WebRequest -Uri $Source -OutFile $downloaded -Headers @{ 'User-Agent' = 'essenthos' }
    $hash = (Get-FileHash -Algorithm SHA256 $downloaded).Hash.ToLowerInvariant()
    if ($hash -ne $Sha256) {
        throw "$FileName hashes to $hash, where it hashed to $Sha256 at commit $Commit. " +
              "Nothing was replaced; a pinned commit should not change, so look at what was served."
    }

    New-Item -ItemType Directory -Force -Path $targetFolder | Out-Null
    Copy-Item -Path $downloaded -Destination $targetFile
    Write-Host ("{0:N1} MB in {1}" -f ((Get-Item $targetFile).Length / 1MB), $targetFolder)
    Write-Host "Then run: python scripts/corpus-manifest.py --folders $Folder"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
