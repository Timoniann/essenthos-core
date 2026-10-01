<#
.SYNOPSIS
    Fetches the Strong-numbered Bibles taken from CrossWire on 2026-10-01: four SWORD modules, each
    checked against the terms its own configuration states.

.DESCRIPTION
    FreSegond1910 is Louis Segond 1910 with the Strong numbers "Concordances et Traductions de la
    Bible" (concordance.bible) assigned in 2026. Its words are the Segond the corpus already loads
    from eBible, so it is read only for its numbers, laid onto that text for one run.
    FreJND is J. N. Darby's French Bible in the 2024 revision of Bibles et Publications Chrétiennes,
    numbered by the same people. GerSch is the Schlachter Bible of 1951. RLT is Michael W. Jones's
    Revised Literal Translation of the King James, with the King James's numbers (KJV2003 project).

    Each archive is pinned to the bytes read on 2026-10-01 and checked against their hash: the
    decision to take the module and the numbers measured on it were made against those bytes. The
    terms are the configuration's own DistributionLicense line, which is re-read from the archive
    before anything is replaced; if it no longer says what the decision rested on, nothing is.

    RusVZh and ABP were read on the same day and are not fetched: RusVZh is provided "strictly for use in
    the SWORD Project", and ABP is copyrighted with permission to distribute granted to CrossWire alone.

.EXAMPLE
    ./scripts/fetch-crosswire-strong.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Packages = 'https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip'

# Each module: the folder it goes into, its archive's hash on 2026-10-01, and the terms it stated then.
$Modules = [ordered]@{
    'FreSegond1910' = @{
        Folder  = 'Segond1910Strong'
        Sha256  = 'fa6b94c5355353bc3b76971947d0053ccf027e7cf58828c165df1098533c2d43'
        License = 'Copyrighted; Permission to distribute granted to CrossWire'
    }
    'FreJND'        = @{
        Folder  = 'DarbyFrench'
        Sha256  = '0c08f4ca5e06247c77359bcc3a27a21552fa4d2c338a802e3935b774db676b7b'
        License = 'Public Domain'
    }
    'GerSch'        = @{
        Folder  = 'Schlachter1951'
        Sha256  = 'af612223a93f67f4c17c1638982310378e82520dcfb046e92027356e4fc6a9e7'
        License = 'Copyrighted; Free non-commercial distribution'
    }
    'RLT'           = @{
        Folder  = 'RevisedLiteral'
        Sha256  = '958924773e81162f753e671c9fa505fc64a247f9910451e51f9543fc74e80f63'
        License = 'GPL'
    }
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "crosswire-strong-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    foreach ($module in $Modules.Keys) {
        $archive = Join-Path $staging "$module.zip"
        Invoke-WebRequest -Uri "$Packages/$module.zip" -OutFile $archive -Headers @{ 'User-Agent' = 'essenthos' }
        $hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
        if ($hash -ne $Modules[$module].Sha256) {
            throw "$module.zip hashes to $hash, where it hashed to $($Modules[$module].Sha256) on 2026-10-01. " +
                  "CrossWire has published a different module, so nothing was replaced; read its History " +
                  "lines and its DistributionLicense and compare the text before taking it."
        }

        $unpacked = Join-Path $staging $module
        Expand-Archive -Path $archive -DestinationPath $unpacked -Force
        $conf = Get-ChildItem (Join-Path $unpacked 'mods.d') -Filter '*.conf' | Select-Object -First 1
        $stated = (Get-Content $conf.FullName -Encoding UTF8 | Where-Object { $_ -like 'DistributionLicense=*' }) -replace '^DistributionLicense=', ''
        if ($stated -ne $Modules[$module].License) {
            throw "$module states DistributionLicense=$stated, where it stated $($Modules[$module].License) when it " +
                  "was taken. Nothing was replaced: read the new terms and record them in its LICENCE.md first."
        }
    }

    $root = Resolve-Path $ResourcesPath
    foreach ($module in $Modules.Keys) {
        $target = Join-Path $root $Modules[$module].Folder $module
        if (Test-Path $target) {
            Remove-Item -Recurse -Force $target
        }

        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -Recurse -Path (Join-Path $staging $module) -Destination $target
        $size = (Get-ChildItem $target -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
        Write-Host ("{0:N1} MB in {1}" -f $size, $target)
    }

    $folders = ($Modules.Values.Folder | Select-Object -Unique) -join ','
    Write-Host "Then run: python scripts/corpus-manifest.py --folders $folders"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
