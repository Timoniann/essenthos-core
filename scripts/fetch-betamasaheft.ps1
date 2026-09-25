<#
.SYNOPSIS
    Fetches the Ethiopic (Ge'ez) Bible, all 81 books of the Ethiopian Orthodox canon, as the TEI
    files Beta maṣāḥǝft publishes one work at a time.

.DESCRIPTION
    Beta maṣāḥǝft (Akademie der Wissenschaften in Hamburg) keeps every Ethiopic work it describes
    as one TEI file in github.com/BetaMasaheft/Works. For the books of the Bible most of those
    files carry the text of the Ethiopian Orthodox Church's printed Bible, typed in 2025; twenty
    carry older digitisations instead: Ran HaCohen's of Dillmann's Octateuch and Books of Kingdoms
    and of Ludolf's Psalter, Michal Jerabek's of Dillmann's Enoch, Wisdom and 4 Baruch, and
    HaCohen's and his helpers' of Sirach and three of the Twelve. Which edition each book is read
    from is decided in GeezTextSource and recorded in Resources/BetaMasaheft/LICENCE.md.

    Eighty-one single files and nothing else: the repository holds six thousand other works, and a
    clone of it is not what was approved. There is no licence file at the repository's root; the
    licence is stated in each file's own header, so each header is checked here before its file is
    kept, and the files themselves are the copy of the licence kept beside the data.

    Pinned to the commit Resources/BetaMasaheft/LICENCE.md was read at, with each file's size at
    that commit, because an unpinned fetch of a share-alike source is an obligation whose terms
    could have moved since anybody read them.

.EXAMPLE
    ./scripts/fetch-betamasaheft.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

# The commit Resources/BetaMasaheft/LICENCE.md was read at. Move it only after reading the headers again.
$Commit = '1d96713016d8c11becc50a3287c955b4cb67a5d1'

$Repository = 'https://github.com/BetaMasaheft/Works'
$Raw = "https://raw.githubusercontent.com/BetaMasaheft/Works/$Commit"

$ExpectedLicence = 'Creative Commons Attribution-ShareAlike 4.0'

# Every file, under the folder the repository keeps it in, with its size in bytes at the commit.
$Files = [ordered]@{
    '1001-2000/LIT1546Genesi.xml' = 579835
    '1001-2000/LIT1367Exodus.xml' = 462806
    '1001-2000/LIT1793Leviti.xml' = 350446
    '2001-3000/LIT2075Number.xml' = 494818
    '2001-3000/LIT2637Deuteronomy.xml' = 407776
    '1001-2000/LIT1696Joshua.xml' = 288862
    '1001-2000/LIT1700Judges.xml' = 273991
    '2001-3000/LIT2229RuthBo.xml' = 39193
    '2001-3000/LIT2697Sam.xml' = 343778
    '2001-3000/LIT2698Sam.xml' = 287074
    '2001-3000/LIT2699Kings.xml' = 324666
    '2001-3000/LIT2700Kings.xml' = 304745
    '3001-4000/LIT3499Chroni.xml' = 333120
    '3001-4000/LIT3500Chroni.xml' = 387712
    '1001-2000/LIT1697Jubilees.xml' = 716497
    '1001-2000/LIT1340EnochE.xml' = 713504
    '3001-4000/LIT3581Bookof.xml' = 99264
    '1001-2000/LIT1374Bookof.xml' = 101265
    '1001-2000/LIT1376Apocal.xml' = 107902
    '1001-2000/LIT1377Bookof.xml' = 138010
    '2001-3000/LIT2473TobitB.xml' = 67610
    '1001-2000/LIT1701Judith.xml' = 104992
    '1001-2000/LIT1362Esther.xml' = 104055
    '1001-2000/LIT1819Maccab.xml' = 222131
    '5001-6000/LIT5840SecondEthioMaccabees.xml' = 127749
    '5001-6000/LIT5839ThirdEthioMaccabees.xml' = 68731
    '1001-2000/LIT1688Job.xml' = 297583
    '1001-2000/LIT2000Mazmur.xml' = 653058
    '3001-4000/LIT3927Messale.xml' = 212903
    '2001-3000/LIT2396Tagsas.xml' = 33854
    '2001-3000/LIT2516Wisdom.xml' = 220030
    '1001-2000/LIT1320Eccles.xml' = 152281
    '2001-3000/LIT2362Songof.xml' = 36209
    '2001-3000/LIT2358Sirach.xml' = 255141
    '1001-2000/LIT1672Isaiah.xml' = 446217
    '1001-2000/LIT1685Bookof.xml' = 512007
    '1001-2000/LIT1202Bookof.xml' = 33996
    '1001-2000/LIT1753Lament.xml' = 52910
    '1001-2000/LIT1686Epistl.xml' = 10293
    '2001-3000/LIT2167Parali.xml' = 49263
    '5001-6000/LIT5802EzekII.xml' = 478997
    '3001-4000/LIT3529Daniel.xml' = 169133
    '3001-4000/LIT3144Hosea.xml' = 74949
    '3001-4000/LIT3145Amos.xml' = 59508
    '3001-4000/LIT3146Micah.xml' = 44428
    '1001-2000/LIT1689Joel.xml' = 30063
    '3001-4000/LIT3147Obadiah.xml' = 11110
    '1001-2000/LIT1694Jonah.xml' = 21266
    '2001-3000/LIT2057Bookof.xml' = 20345
    '1001-2000/LIT1567Bookof.xml' = 23747
    '3001-4000/LIT3148Zephan.xml' = 24149
    '3001-4000/LIT3149Haggai.xml' = 19108
    '3001-4000/LIT3150Zechar.xml' = 82670
    '3001-4000/LIT3151Malachi.xml' = 28128
    '2001-3000/LIT2709Matthew.xml' = 355381
    '2001-3000/LIT2711Mark.xml' = 217681
    '2001-3000/LIT2713Luke.xml' = 376523
    '2001-3000/LIT2715John.xml' = 296697
    '1001-2000/LIT1019Actsof.xml' = 360137
    '3001-4000/LIT3515Epistle.xml' = 149994
    '3001-4000/LIT3516Epistle.xml' = 148119
    '3001-4000/LIT3517Epistle.xml' = 94067
    '3001-4000/LIT3518Epistle.xml' = 51566
    '3001-4000/LIT3519Epistle.xml' = 51363
    '3001-4000/LIT3520Epistle.xml' = 42091
    '3001-4000/LIT3521Epistle.xml' = 38140
    '3001-4000/LIT3522Epistle.xml' = 36654
    '3001-4000/LIT3523Epistle.xml' = 21753
    '3001-4000/LIT3524Epistle.xml' = 104669
    '3001-4000/LIT3525Epistle.xml' = 45666
    '3001-4000/LIT3526Epistle.xml' = 33804
    '3001-4000/LIT3527Epistle.xml' = 21146
    '3001-4000/LIT3528Epistle.xml' = 12600
    '3001-4000/LIT3507Epistle.xml' = 44700
    '3001-4000/LIT3508Epistle.xml' = 28371
    '3001-4000/LIT3509Epistle.xml' = 45115
    '3001-4000/LIT3510Epistle.xml' = 8261
    '3001-4000/LIT3511Epistle.xml' = 8493
    '3001-4000/LIT3512Epistle.xml' = 42509
    '3001-4000/LIT3513Epistle.xml' = 13383
    '3001-4000/LIT3179Revela.xml' = 161820
}

$ExpectedFiles = 81

if ($Files.Count -ne $ExpectedFiles) {
    throw "This script lists $($Files.Count) files where the Ethiopian canon has $ExpectedFiles books, one file " +
          "each. Fix the list rather than fetching part of a Bible as though it were the whole of one."
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "betamasaheft-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Write-Host "Fetching $ExpectedFiles works from $Repository at $($Commit.Substring(0, 7))"

    foreach ($entry in $Files.GetEnumerator()) {
        $name = Split-Path $entry.Key -Leaf
        $staged = Join-Path $staging $name
        Invoke-WebRequest -Uri "$Raw/$($entry.Key)" -OutFile $staged -Headers @{ 'User-Agent' = 'essenthos' }

        $bytes = (Get-Item $staged).Length
        if ($bytes -ne $entry.Value) {
            throw "$($entry.Key) is $bytes bytes where it was $($entry.Value) at $Commit. A pinned file cannot " +
                  "change size, so the download is partial or not the file; nothing was replaced."
        }

        # The <licence> element runs over line breaks, so the header's whitespace is folded first.
        $header = ((Get-Content $staged -Raw -Encoding UTF8) -split '</teiHeader>')[0] -replace '\s+', ' '
        if ($header -notmatch [regex]::Escape($ExpectedLicence)) {
            throw "$($entry.Key) no longer states `"$ExpectedLicence`" in its header. That statement is the " +
                  "permission the text is held under, so nothing was replaced. Read what it says now and " +
                  "record it in Resources/BetaMasaheft/LICENCE.md before fetching again."
        }
    }

    $target = Join-Path (Resolve-Path $ResourcesPath) 'BetaMasaheft'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Get-ChildItem $target -File -Filter '*.xml' | Remove-Item -Force
    Move-Item -Path (Join-Path $staging '*.xml') -Destination $target -Force

    $size = (Get-ChildItem $target -File -Filter '*.xml' | Measure-Object -Property Length -Sum).Sum / 1MB
    Write-Host ("{0} files, {1:N1} MiB, every header CC BY-SA 4.0, in {2}" -f $ExpectedFiles, $size, $target)
    Write-Host "Taken from $Repository at $Commit"
    Write-Host "Then run: python scripts/corpus-manifest.py --folders BetaMasaheft, and commit the manifest."
    Write-Host ("A reload is not automatic: the corpus loader returns early for a text whose slug " +
                "is already in the text table, so a restart alone picks nothing up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
