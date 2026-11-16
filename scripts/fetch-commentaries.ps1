<#
.SYNOPSIS
    Fetches the commentaries the corpus serves beside the verse, as CrossWire publishes them: Matthew
    Henry's Complete Commentary (MHC) and Carl Heinrich Rieger's Betrachtungen (Rieger).

.DESCRIPTION
    Each is a SWORD commentary module, pinned to the archive read on 2026-11-16 and checked against
    its hash then. Each module's configuration is read before anything is installed; if it no longer
    says Public Domain, nothing is: the terms moved, and that is the owner's to read, not a download's.

    One folder per module under Resources/Commentaries, as the SWORD reader expects (one
    configuration under mods.d). A module already there is checked against the pinned archive and
    left alone; a module whose recorded archive differs is a stop, never a silent replacement.

.EXAMPLE
    ./scripts/fetch-commentaries.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Mirror = 'https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip'
$Folder = 'Commentaries'
$Licence = 'DistributionLicense=Public Domain'
$Pinned = @(
    @{ Module = 'MHC'; Sha256 = '6bcb936873ca144e317805e5c1677940fd86e2403f7c14517752e44f25c8882b'; Conf = 'mhc.conf' },
    @{ Module = 'Rieger'; Sha256 = 'b7bacef4f009a9f7c13a2ea7b5181f7f442be4b498125b92d477e4a0198af119'; Conf = 'rieger.conf' }
)

$commentaries = Join-Path (Resolve-Path $ResourcesPath) $Folder
New-Item -ItemType Directory -Force -Path $commentaries | Out-Null

foreach ($pin in $Pinned) {
    $module = $pin.Module
    $destination = Join-Path $commentaries $module
    $record = Join-Path $destination 'archive.sha256'
    if (Test-Path $destination) {
        $recorded = if (Test-Path $record) { (Get-Content $record -Raw).Trim() } else { '' }
        if ($recorded -eq $pin.Sha256 -and (Test-Path (Join-Path $destination 'mods.d' $pin.Conf))) {
            Write-Host "$module is installed from the pinned archive; left as it is."
            continue
        }

        throw "$destination is there but was not installed from the pinned archive ($($pin.Sha256)); it records " +
              "'$recorded'. Nothing was changed. Compare it with the pinned module and remove it by hand if it is to be replaced."
    }

    $staging = Join-Path ([IO.Path]::GetTempPath()) "commentary-$module-$([guid]::NewGuid().ToString('n'))"
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    try {
        $archive = "$Mirror/$module.zip"
        Write-Host "Fetching $archive"
        $zip = Join-Path $staging "$module.zip"
        Invoke-WebRequest -Uri $archive -OutFile $zip -Headers @{ 'User-Agent' = 'essenthos' }
        $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
        if ($hash -ne $pin.Sha256) {
            throw "$module.zip hashes to $hash, where it hashed to $($pin.Sha256) on 2026-11-16. CrossWire has published " +
                  "a different module, so nothing was installed; read its History lines and the comments before taking it."
        }

        $unpacked = Join-Path $staging $module
        Expand-Archive -Path $zip -DestinationPath $unpacked -Force
        $configuration = Get-Content (Join-Path $unpacked 'mods.d' $pin.Conf) -Raw
        if (-not $configuration.Contains($Licence)) {
            throw "The $module configuration no longer says $Licence, while the archive hashes as pinned. Nothing was " +
                  "installed; read mods.d/$($pin.Conf) and record what it says in Resources/$Folder/LICENCE.md first."
        }

        Set-Content -Path (Join-Path $unpacked 'archive.sha256') -Value $pin.Sha256 -NoNewline
        Move-Item -Path $unpacked -Destination $destination
        $size = (Get-ChildItem $destination -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
        Write-Host ("{0}: {1:N1} MB in {2}" -f $module, $size, $destination)
    }
    finally {
        Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
    }
}

Write-Host "Then run: python scripts/corpus-manifest.py --folders $Folder"
