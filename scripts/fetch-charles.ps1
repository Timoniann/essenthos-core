<#
.SYNOPSIS
    Fetches R. H. Charles's translations of 1 Enoch and Jubilees from the Ethiopic, as CrossWire publishes them in
    The Apocrypha and Pseudepigrapha of the Old Testament (Oxford, 1913): the SWORD general books Enoch and Jubilees.

.DESCRIPTION
    Each is a RawGenBook module in OSIS, pinned to the archive read on 2026-11-16 and checked against its hash
    then. Each module's configuration is read before anything is installed; if it no longer says Public Domain,
    nothing is: the terms moved, and that is the owner's to read, not a download's.

    One folder per module under Resources/Charles, as the SWORD reader expects (one configuration under mods.d).
    A module already there is checked against the pinned archive and left alone; a module whose recorded archive
    differs is a stop, never a silent replacement. Resources/Charles/LICENCE.md says what each statement is.

.EXAMPLE
    ./scripts/fetch-charles.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Mirror = 'https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip'
$Folder = 'Charles'
$Licence = 'DistributionLicense=Public Domain'
$Pinned = @(
    @{ Module = 'Enoch'; Sha256 = '13f31fa8360e21ac7ceec6d47eb2af7f8ba743011afabdc47aba4a4d622f8731'; Conf = 'enoch.conf' },
    @{ Module = 'Jubilees'; Sha256 = 'c613b0a23e3d084e8eee89b550ce98ecd051c3cba85d18869f3c44659997a734'; Conf = 'jubilees.conf' }
)

$charles = Join-Path (Resolve-Path $ResourcesPath) $Folder
New-Item -ItemType Directory -Force -Path $charles | Out-Null

foreach ($pin in $Pinned) {
    $module = $pin.Module
    $destination = Join-Path $charles $module
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

    $staging = Join-Path ([IO.Path]::GetTempPath()) "charles-$module-$([guid]::NewGuid().ToString('n'))"
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    try {
        $archive = "$Mirror/$module.zip"
        Write-Host "Fetching $archive"
        $zip = Join-Path $staging "$module.zip"
        Invoke-WebRequest -Uri $archive -OutFile $zip -Headers @{ 'User-Agent' = 'essenthos' }
        $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
        if ($hash -ne $pin.Sha256) {
            throw "$module.zip hashes to $hash, where it hashed to $($pin.Sha256) on 2026-11-16. CrossWire has published " +
                  "a different module, so nothing was installed; read its History lines before taking it."
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
