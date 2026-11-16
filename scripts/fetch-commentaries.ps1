<#
.SYNOPSIS
    Fetches the commentaries the corpus serves beside the verse: Matthew Henry's Complete Commentary
    (MHC) and Carl Heinrich Rieger's Betrachtungen (Rieger) as CrossWire publishes them, and the
    Explanatory Bible of Lopukhin and his successors (Lopukhin) as the Christian Classics Ethereal
    Library's Russian collection holds it.

.DESCRIPTION
    Each is a SWORD commentary module, pinned to the archive read on 2026-11-16 and checked against
    its hash then. Each module's configuration is read before anything is installed; if it no longer
    says Public Domain, nothing is: the terms moved, and that is the owner's to read, not a download's.

    One folder per module under Resources/Commentaries, as the SWORD reader expects (one
    configuration under mods.d). A module already there is checked against the pinned archive and
    left alone; a module whose recorded archive differs is a stop, never a silent replacement.

    Lopukhin is 54 FictionBook archives (2004-2007, one per book of the Old Testament, four for the
    New), kept as they are downloaded in Resources/Commentaries/Lopukhin, each pinned by its hash.
    They state no licence: the work is public domain by age and the transcription carries none
    (Resources/Commentaries/LICENCE.md says why), so there is no licence line to re-read; a folder
    whose archives differ from the pinned ones is a stop, as above.

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

$LopukhinMirror = 'https://www.ccel.org/contrib/ru/xml'
$LopukhinArchives = @(
    @{ File = 'Lopuhin_NewTastement_01.zip'; Sha256 = '8b47f191d186d698e88fdc8e591f6a0380b8c33bdd3b88a0de960431cb4db222' },
    @{ File = 'Lopuhin_NewTastement_02.zip'; Sha256 = '525dad52e0097706bb402e8f5deb4802b7a0ca299f0ad4271d1e3d0d8db2a0ef' },
    @{ File = 'Lopuhin_NewTastement_03.zip'; Sha256 = '2818a16f9926a05a0c53747153abfd1c82046d5338d632d78cc121eec8508253' },
    @{ File = 'Lopuhin_NewTastement_04.zip'; Sha256 = 'cd11b2b9170cfe51833fb53ed793ee89ff0d1d9d1cb9c0b52dbf5a9f061d463d' },
    @{ File = 'Lopuhin_Old_Tastement_01-Gen.zip'; Sha256 = '2343dfa4b8539f81bd1455418986536e7fef8b2b18690279144636b29c413e0e' },
    @{ File = 'Lopuhin_Old_Tastement_02-Ex.zip'; Sha256 = 'dd6356e18e967fcf13916d8ae230cae0093e5a078f33de40ac035022d94d6d80' },
    @{ File = 'Lopuhin_Old_Tastement_03-Lev.zip'; Sha256 = '1ff601b46701798af8c20867e1b4f6b97c732759965f6afaf91721c0f4b9b64f' },
    @{ File = 'Lopuhin_Old_Tastement_04-Num.zip'; Sha256 = 'd8c72c1bc4e0c73bddea23243d5ffbc12ecc37973aec0117580baef043f189f9' },
    @{ File = 'Lopuhin_Old_Tastement_05-Deu.zip'; Sha256 = '1f02638a40d294334fddc4efdc38b59f96a5e7988dd729a71b1d393ba23898d7' },
    @{ File = 'Lopuhin_Old_Tastement_06-Jos.zip'; Sha256 = '35c449ed35bda571b3618e0d6014ed740eb5e1689bc8827969518337e054993f' },
    @{ File = 'Lopuhin_Old_Tastement_07-Judg.zip'; Sha256 = '582a1654febc0b02c398415758ae9610f835f0af1cc8c21b635980874f8ef835' },
    @{ File = 'Lopuhin_Old_Tastement_08-Ruth.zip'; Sha256 = '0d41567c51799e6f42c54acf57e8b856f58f158614f90141749eb3612e3e5243' },
    @{ File = 'Lopuhin_Old_Tastement_09-1Kings.zip'; Sha256 = 'd5b7d65fbfbafaaf1f911da0a4b0c29b53dd8b1cc272ccdd53b7c2f7a74819bb' },
    @{ File = 'Lopuhin_Old_Tastement_10-2Kings.zip'; Sha256 = '02a309055f4acb9a7020afd925d908856936093288ba07fb81368d31c43553f6' },
    @{ File = 'Lopuhin_Old_Tastement_11-3Kings.zip'; Sha256 = 'fb798ec64f5d5f500992afe64c6e10e1bf2e2690641c96aa0f53af672c94e7e2' },
    @{ File = 'Lopuhin_Old_Tastement_12-4Kings.zip'; Sha256 = '22eee5939cf8751d9a983b81945dc6da2c0e5852854f57427aadea9605805a90' },
    @{ File = 'Lopuhin_Old_Tastement_13-1Par.zip'; Sha256 = 'cb86e2a39bf520e11897d97a673d17fdae9e0cde5688ec015212472db11f04ac' },
    @{ File = 'Lopuhin_Old_Tastement_14-2Par.zip'; Sha256 = '53480cbd76837def923f7f7af1263de7139ebc2371aeee74d523855a24348681' },
    @{ File = 'Lopuhin_Old_Tastement_15-1Ezd.zip'; Sha256 = '9d3012be25e76de504b098f45dfb63683a600ad7853f7f2876c0240de52fb752' },
    @{ File = 'Lopuhin_Old_Tastement_16-Neh.zip'; Sha256 = '4d817efcea475ef9adab92ca0e071c802d9b104912faaa7262a3d4ce0cacd71e' },
    @{ File = 'Lopuhin_Old_Tastement_17-Esth.zip'; Sha256 = '2c0cd81c657f4554f7786d3c1ac5a1d4182f5b13f652235d11aa354bb4c8d860' },
    @{ File = 'Lopuhin_Old_Tastement_18-Job.zip'; Sha256 = '2db5f292a25245367e1fdd65351e860bbdcdb3c0d596e1462af842bc95d19070' },
    @{ File = 'Lopuhin_Old_Tastement_19-Psaltyr.zip'; Sha256 = '4f05a7d4685ea273a5ba03fb619f6b2b5a4e6b4d3173abb2f3b93b22943fb972' },
    @{ File = 'Lopuhin_Old_Tastement_20-Prov.zip'; Sha256 = 'dadb4cf5cfb96deab5c89c3407adfb1dfaf67fd685ebedd9c2ea0b21ba9a2b2f' },
    @{ File = 'Lopuhin_Old_Tastement_21-Eccl.zip'; Sha256 = 'b0b2c16a1ede2a87c2df95481fd936049d25bd693e8a2a139848c7d6c10ea40d' },
    @{ File = 'Lopuhin_Old_Tastement_22-Song.zip'; Sha256 = 'ebe58b031291838391bbe6cc17650e05161e8dd83c854ac076f7887df2559186' },
    @{ File = 'Lopuhin_Old_Tastement_23-Isa.zip'; Sha256 = '27b8adf254e849789cc745713cd5c7cb89d00a83d6cd171097019cf5f2496771' },
    @{ File = 'Lopuhin_Old_Tastement_24-Jer.zip'; Sha256 = '7edc443e65e6b94ad97aad4857b1960acf0927a5e4311b36f9d59984eca8dbbd' },
    @{ File = 'Lopuhin_Old_Tastement_25-Lam.zip'; Sha256 = '0b158f743c75ac284983626261ab8885aa61a5e7d2d0e5e938900d62ac56136a' },
    @{ File = 'Lopuhin_Old_Tastement_26-Eze.zip'; Sha256 = '74878879b6ad01c08de4b449a52f73fe4c10fd76b71223817c5843b51ac548bc' },
    @{ File = 'Lopuhin_Old_Tastement_27-Dan.zip'; Sha256 = '886ea19ac8d52eab01fc03817ccbca7789a64a6f3cd1119f1e763761b26e582b' },
    @{ File = 'Lopuhin_Old_Tastement_28-Hos.zip'; Sha256 = '7ac110239618db1d3068085064420e9bfa5a184b04c38d7a6457d74e56f340cc' },
    @{ File = 'Lopuhin_Old_Tastement_29-Joe.zip'; Sha256 = '1b8f889e95c0b73e999e824b1e65af3c41e8e1363e24e7d1504319ea70c736fa' },
    @{ File = 'Lopuhin_Old_Tastement_30-Amo.zip'; Sha256 = 'd5fa6486f5b101b7ad827dc2b850e90ca495c6ca28950fde8c70b99ff50942f0' },
    @{ File = 'Lopuhin_Old_Tastement_31-Oba.zip'; Sha256 = '17bef7e3bd6e0e8b83b506be6270229ce8e89b16bb033eb250f1636aa81d97e5' },
    @{ File = 'Lopuhin_Old_Tastement_32-Jona.zip'; Sha256 = 'e198764b6194beb943c01cd033ffb3f0db201c4b90b433c084c791825d7856c1' },
    @{ File = 'Lopuhin_Old_Tastement_33-Mic.zip'; Sha256 = '6be45753e7d85c97139a8150fb2476382ecc30af11487d1c1f6ce97ca2a93a01' },
    @{ File = 'Lopuhin_Old_Tastement_34-Nah.zip'; Sha256 = 'f341f2d0f27b8577e30e678e46293c80c8a57a7c472484dbd40d0c7204605a8b' },
    @{ File = 'Lopuhin_Old_Tastement_35-Hab.zip'; Sha256 = 'f3391ab8ccdb2ca5d66da65341d60ef1bf249f0005fa01ba6f147bc5bc10713f' },
    @{ File = 'Lopuhin_Old_Tastement_36-Zep.zip'; Sha256 = 'f77f680923754c2e1439e374f95df1135926970014c367509ace2be0b1ab113a' },
    @{ File = 'Lopuhin_Old_Tastement_37-Hag.zip'; Sha256 = '8ecff6db56092359a1cc74b58292b6dafc63e825dc498302a4418748bfc0b219' },
    @{ File = 'Lopuhin_Old_Tastement_38-Zec.zip'; Sha256 = '2405c953c862f5f4d31a92df68d0a439d8a58bb8a4eb7dea22da3c3308de0ae7' },
    @{ File = 'Lopuhin_Old_Tastement_39-Mal.zip'; Sha256 = '091a9408e8685407ede91b8ddbc1a07f23f2d3a56a936e8002d7f04d439e9153' },
    @{ File = 'Lopuhin_Old_Tastement_40-2Ezd.zip'; Sha256 = 'f8c5270418711054363459db469f24ebfb30eae94da290680ab324f8d2d76673' },
    @{ File = 'Lopuhin_Old_Tastement_41-Tob.zip'; Sha256 = '2daf429ee3b9c08b4a6c1b47b2971536b374f050b804b3cc27758d4a91eb5c76' },
    @{ File = 'Lopuhin_Old_Tastement_42-Jud.zip'; Sha256 = 'f25b7bdec4d4b1a065ae38290740ae7b3374296fb652f75c525795010a60907b' },
    @{ File = 'Lopuhin_Old_Tastement_43-Wis.zip'; Sha256 = 'bd28673b37a19680cc0ec8ec6a8c5dc5f44e6d471b4473075d1e78c7526b1e92' },
    @{ File = 'Lopuhin_Old_Tastement_44-Sir.zip'; Sha256 = '4bf06ec330baaa292f6e453f86e9b16791aa99fcf1527b1c4764d885ddcd98d2' },
    @{ File = 'Lopuhin_Old_Tastement_45-JerEp.zip'; Sha256 = 'd6d2f54a8383950221a6a990906c2c208fb3c5f7575b66f52d9b8f8c48692e50' },
    @{ File = 'Lopuhin_Old_Tastement_46-Bar.zip'; Sha256 = '4ee4ca7086af0631be705487fe63f9defa8f6787295c0b76eed5e3122d5254a8' },
    @{ File = 'Lopuhin_Old_Tastement_47-1Mak.zip'; Sha256 = '5571dfc7d9d794dbfc2b658eef6c016a1551eb41cc72341d437d57725b86e544' },
    @{ File = 'Lopuhin_Old_Tastement_48-2Mak.zip'; Sha256 = '585e5331aa372c4a20a96da8c2808ec8fcb889a606eb5c054d1abdfc94a83fcc' },
    @{ File = 'Lopuhin_Old_Tastement_49-3Mak.zip'; Sha256 = '05cb153a0d285fbbd0979fa60c6f81243377482750f19daa4e9111f13889c37d' },
    @{ File = 'Lopuhin_Old_Tastement_50-3Ezd.zip'; Sha256 = 'ef1d3e9229aed17c86bb75f980976056cd851639b412203bbbe6179cc9d4b9f8' }
)

$lopukhinFolder = Join-Path $commentaries 'Lopukhin'
if (Test-Path $lopukhinFolder) {
    $present = @(Get-ChildItem $lopukhinFolder -File -Filter '*.zip' | ForEach-Object { $_.Name })
    $differing = @()
    foreach ($pin in $LopukhinArchives) {
        $held = Join-Path $lopukhinFolder $pin.File
        if (-not (Test-Path $held) -or (Get-FileHash -Algorithm SHA256 $held).Hash.ToLowerInvariant() -ne $pin.Sha256) {
            $differing += $pin.File
        }
    }

    $extra = @($present | Where-Object { $_ -notin ($LopukhinArchives | ForEach-Object { $_.File }) })
    if ($differing.Count -gt 0 -or $extra.Count -gt 0) {
        throw "$lopukhinFolder is there but does not hold the pinned archives (missing or different: $($differing -join ', '); " +
              "not pinned: $($extra -join ', ')). Nothing was changed. Remove it by hand if it is to be fetched again."
    }

    Write-Host "Lopukhin holds the $($LopukhinArchives.Count) pinned archives; left as it is."
}
else {
    $staging = Join-Path ([IO.Path]::GetTempPath()) "commentary-Lopukhin-$([guid]::NewGuid().ToString('n'))"
    $unpacked = Join-Path $staging 'Lopukhin'
    New-Item -ItemType Directory -Force -Path $unpacked | Out-Null
    try {
        foreach ($pin in $LopukhinArchives) {
            $archive = "$LopukhinMirror/$($pin.File)"
            $zip = Join-Path $unpacked $pin.File
            Invoke-WebRequest -Uri $archive -OutFile $zip -Headers @{ 'User-Agent' = 'essenthos' }
            $hash = (Get-FileHash -Algorithm SHA256 $zip).Hash.ToLowerInvariant()
            if ($hash -ne $pin.Sha256) {
                throw "$($pin.File) hashes to $hash, where it hashed to $($pin.Sha256) on 2026-11-16. The library has published " +
                      "a different file, so nothing was installed; compare it with the pinned one before taking it."
            }
        }

        Move-Item -Path $unpacked -Destination $lopukhinFolder
        $size = (Get-ChildItem $lopukhinFolder -File | Measure-Object -Property Length -Sum).Sum / 1MB
        Write-Host ("Lopukhin: {0} archives, {1:N1} MB in {2}" -f $LopukhinArchives.Count, $size, $lopukhinFolder)
    }
    finally {
        Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
    }
}

Write-Host "Then run: python scripts/corpus-manifest.py --folders $Folder"
