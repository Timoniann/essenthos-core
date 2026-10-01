<#
.SYNOPSIS
    Fetches the Bibles on Door43 whose words are tied by hand to the Hebrew or Greek word each
    renders, one pinned release at a time, into Resources/Door43/<folder>.

.DESCRIPTION
    Every release listed here is a translation in USFM 3 with its alignment woven into the text:
    each word stands inside a \zaln-s milestone naming the word of unfoldingWord's Hebrew Bible or
    Greek New Testament it renders. The same files are the text the corpus loads, or, where the
    corpus already holds the edition from elsewhere, the source its links are drawn from.

    Each release is pinned to the SHA-256 its archive had when the decision to take it was made, so a
    moved tag is a question for a person and not a download. Its terms are read at the source before
    anything is replaced: LICENSE.md and manifest.yaml must still say what they said then.
    Resources/Door43/LICENCE.md says what each obliges and what was decided about it.

.PARAMETER Only
    One folder, or several separated by commas. Without it every release is fetched.

.EXAMPLE
    ./scripts/fetch-door43-aligned.ps1 -Only ar_avd
#>

[CmdletBinding()]
param(
    [string] $Only,

    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$ShareAlike = 'Creative Commons Attribution-ShareAlike 4.0 International'

# Folder, repository and tag, the archive's hash on the day it was read, what its licence and its
# manifest have to keep saying, and what the release holds: its books and the alignment milestones
# across them. A different count is a different release, or a partial download.
$Releases = @(
    @{
        Folder = 'ar_avd'; Repository = 'BSOJ/ar_avd'; Tag = 'v6.9'
        Sha256 = '17d9a3a0f921b649bfb9faae802bbd5f62c9f1b124f266068f129055fb95d944'
        Licence = $ShareAlike; Rights = 'rights: CC BY-SA 4.0'
        # The release ships Second Chronicles twice, as 13-2CH.usfm and 14-2CH.usfm, byte for byte.
        Drop = @('13-2CH.usfm'); Books = 66; Milestones = 448187
    },
    @{
        Folder = 'hi_irv'; Repository = 'Door43-Catalog/hi_irv'; Tag = 'v12'
        Sha256 = '7668204fa80ca72323b3be8c5ea13b24cfba0571a35ae348a4a4ddc83db7c931'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 66; Milestones = 430532
    },
    @{
        Folder = 'bn_irv'; Repository = 'Door43-Catalog/bn_irv'; Tag = 'v5'
        Sha256 = '98ab617112e05416f17e7a7a269fa83160d25d6923a205c5fd873e6df5eca4ac'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 66; Milestones = 101374
    },
    @{
        Folder = 'as_irv'; Repository = 'Door43-Catalog/as_irv'; Tag = 'v3'
        Sha256 = '017d4634b1459adf2088a717dfc23c99ccb98906068543c3136fa4f7dde0b3ca'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 66; Milestones = 118110
    },
    @{
        Folder = 'gu_irv'; Repository = 'Door43-Catalog/gu_irv'; Tag = 'v4'
        Sha256 = 'aba5949bc5c59a77c8bfd204b1da176bf871e64662c3fc602d9411c655f249cd'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 27; Milestones = 108616
    },
    @{
        Folder = 'kn_irv'; Repository = 'Door43-Catalog/kn_irv'; Tag = 'v5'
        Sha256 = '74037257ec8801a852a37b3e77fc58f95fea032036474610d8d093319e1342d8'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 27; Milestones = 93859
    },
    @{
        Folder = 'ml_irv'; Repository = 'Door43-Catalog/ml_irv'; Tag = 'v5'
        Sha256 = 'ef2b4f702ebab5970f06c3621e7c196325aeaa792f7289e1fff39c55ac75bbef'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 66; Milestones = 79528
    },
    @{
        Folder = 'mr_irv'; Repository = 'Door43-Catalog/mr_irv'; Tag = 'v4'
        Sha256 = '1446aaf6262a2aea46cd43ad6f5312727e77b721fd0efc219d6632435e7b832b'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 27; Milestones = 113022
    },
    @{
        Folder = 'pa_irv'; Repository = 'Door43-Catalog/pa_irv'; Tag = 'v3'
        Sha256 = 'e14a6fa3aa084195fc3f36fe65bdf3e87f7cafe97c5071bba4b23f24dba3a38e'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 27; Milestones = 128991
    },
    @{
        Folder = 'ta_irv'; Repository = 'Door43-Catalog/ta_irv'; Tag = 'v3'
        Sha256 = '61ab8aaa43020343d06faee82e0c656a0f92385a4f9b128da7a19c8d29224c9b'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 66; Milestones = 102597
    },
    @{
        Folder = 'te_irv'; Repository = 'Door43-Catalog/te_irv'; Tag = 'v2'
        Sha256 = 'b8be7a8e6483da9ff4b541cc7b09e6402c559c68ab8bc323ff9ccd43e29226bd'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 27; Milestones = 96367
    },
    @{
        Folder = 'ur-deva_irv'; Repository = 'Door43-Catalog/ur-deva_irv'; Tag = 'v2'
        Sha256 = '1c54f758117f89a60811c5ba443a8eafb34c6fddb00007f67490f93505b11920'
        Licence = $ShareAlike; Rights = "rights: 'CC BY-SA 4.0'"
        Drop = @(); Books = 66; Milestones = 119570
    },
    @{
        Folder = 'en_ust'; Repository = 'unfoldingWord/en_ust'; Tag = 'v91'
        Sha256 = '0c7c3f9747f58a08a9901d1acfc26cd1356aebdb161671a3becfc403dca3d28c'
        Licence = $ShareAlike; Rights = 'rights: CC BY-SA 4.0'
        Drop = @(); Books = 59; Milestones = 397226
    },
    @{
        Folder = 'vi_glt'; Repository = 'vi_gl/vi_glt'; Tag = 'v1'
        Sha256 = 'a3c056905e1d7a17285479e336425c196e628200bebb1d9f76277127f1444a4f'
        Licence = $ShareAlike; Rights = 'rights: CC BY-SA 4.0'
        Drop = @(); Books = 28; Milestones = 123933
    },
    @{
        # A Scripture Burrito rather than a resource container: the books, without the number in front of
        # their names, and the licence are under ingredients/, and metadata.json names the licence file.
        Folder = 'fr_lsg'; Repository = 'fr_gl/fr-textTranslation-FR_LSG'; Tag = 'v1'
        Sha256 = '9cda1da7f077c1072aed3375309c12a3785c4782edd6cfd1d3717c8ab2ae850d'
        Licence = 'Creative Commons Attribution-ShareAlike 4.0 International Public License'
        LicenceFile = 'ingredients/license.md'; Manifest = 'metadata.json'
        Rights = '"licenses":[{"ingredient":"license.md"}]'; Under = 'ingredients'
        Drop = @(); Books = 27; Milestones = 116124
    }
)

$wanted = if ($Only) { $Only.Split(',') | ForEach-Object { $_.Trim() } } else { $Releases.Folder }
$unknown = $wanted | Where-Object { $_ -notin $Releases.Folder }
if ($unknown) {
    throw "No Door43 release is pinned for $($unknown -join ', '). The folders known are $($Releases.Folder -join ', ')."
}

foreach ($release in $Releases | Where-Object { $_.Folder -in $wanted }) {
    $name = $release.Repository
    $archive = "https://git.door43.org/$name/archive/$($release.Tag).zip"
    $staging = Join-Path ([IO.Path]::GetTempPath()) "door43-$($release.Folder)-$([guid]::NewGuid().ToString('n'))"
    New-Item -ItemType Directory -Force -Path $staging | Out-Null

    try {
        $zip = Join-Path $staging "$($release.Folder)-$($release.Tag).zip"
        Write-Host "Fetching $archive"
        curl.exe --silent --show-error --location --max-time 900 --user-agent 'essenthos' --output $zip $archive
        if ($LASTEXITCODE -ne 0) {
            throw "$archive could not be fetched (curl exit $LASTEXITCODE). Nothing was replaced."
        }

        $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
        if ($hash -ne $release.Sha256) {
            throw "$name $($release.Tag) hashes to $hash, where it hashed to $($release.Sha256) on 2026-10-01. " +
                  'A release tag should not move; read what changed before taking it. Nothing was replaced.'
        }

        Expand-Archive -Path $zip -DestinationPath $staging
        $unpacked = Get-ChildItem $staging -Directory | Select-Object -First 1

        $licenceFile = if ($release.LicenceFile) { $release.LicenceFile } else { 'LICENSE.md' }
        $manifestFile = if ($release.Manifest) { $release.Manifest } else { 'manifest.yaml' }
        $books = if ($release.Under) { Join-Path $unpacked.FullName $release.Under } else { $unpacked.FullName }

        $licence = Get-Content (Join-Path $unpacked.FullName $licenceFile) -Raw -Encoding UTF8
        if ($licence -notmatch [regex]::Escape($release.Licence)) {
            throw "$licenceFile in $name $($release.Tag) no longer says `"$($release.Licence)`". The decision to " +
                  'take it was made against that statement; nothing was replaced.'
        }

        $manifest = Get-Content (Join-Path $unpacked.FullName $manifestFile) -Raw -Encoding UTF8
        if ($manifest -notmatch [regex]::Escape($release.Rights)) {
            throw "$manifestFile in $name $($release.Tag) no longer states `"$($release.Rights)`". Nothing was replaced."
        }

        $usfm = Get-ChildItem $books -Filter '*.usfm' |
            Where-Object { $_.Name -notlike 'A0-*' -and $_.Name -notin $release.Drop }
        if ($usfm.Count -ne $release.Books) {
            throw "$name $($release.Tag) holds $($usfm.Count) books where it held $($release.Books). Nothing was replaced."
        }

        $spans = ($usfm | ForEach-Object { ([regex]::Matches((Get-Content $_.FullName -Raw), '\\zaln-s')).Count } |
            Measure-Object -Sum).Sum
        if ($spans -ne $release.Milestones) {
            throw "$name $($release.Tag) holds $spans alignment milestones where it held $($release.Milestones). " +
                  'Nothing was replaced.'
        }

        $root = Join-Path (Resolve-Path $ResourcesPath) 'Door43' $release.Folder
        New-Item -ItemType Directory -Force -Path $root | Out-Null
        Get-ChildItem $root -File | Remove-Item -Force
        $usfm | Copy-Item -Destination $root -Force
        Copy-Item -Path (Join-Path $unpacked.FullName $licenceFile), (Join-Path $unpacked.FullName $manifestFile) `
            -Destination $root -Force

        Write-Host ("{0} {1}: {2} books, {3:N0} alignment milestones, in {4}" -f $name, $release.Tag, $usfm.Count, $spans, $root)
    }
    finally {
        Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
    }
}

Write-Host 'Then run: python scripts/corpus-manifest.py --folders Door43, and commit the manifest.'
