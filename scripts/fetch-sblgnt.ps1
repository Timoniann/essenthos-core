<#
.SYNOPSIS
    Fetches the text of the SBL Greek New Testament, and nothing of MorphGNT's parsing of it.

.DESCRIPTION
    The SBLGNT has been Creative Commons Attribution 4.0 since version 1.1 of 19 December 2022, as
    its repository's LICENSE and README both say. Both are fetched with the text and read before
    anything is installed: if either no longer states Attribution 4.0, this stops and replaces
    nothing, because a licence that changed is the owner's decision and not a download.

    The files are pinned to one commit and each is checked against the SHA-256 it had when the
    licence was read. A folder already holding every file with its expected hash is left alone.

.EXAMPLE
    ./scripts/fetch-sblgnt.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # The Faithlife/SBLGNT commit the hashes below were taken at (version 1.2, 2023).
    [string] $Commit = 'c4d241a9c1c479a55b989ba35a4976c1d0b8052c'
)

$ErrorActionPreference = 'Stop'

$Raw = "https://raw.githubusercontent.com/Faithlife/SBLGNT/$Commit"

# Every file, by its path in the repository, with its SHA-256 at the pinned commit.
$Expected = [ordered]@{
    'README.md' = '7ae1dc2622a8dafe55cf8b17d4935c3e39a70c46636277dd0fc46fe8b7464721'
    'LICENSE' = '7e7170e3cebf88a9f60c7b8421418323c09304da1af4d5e90f4da1dc1c8a2661'
    'About.md' = '4c7198fb57ce5f3b6572e856bd8b9439c9793c9d6b678e0e1ac123c887591765'
    'data/sblgnt/text/Matt.txt' = '4fa718540cbc6330591b233c99c68bbf52b85afc90016e4188efe5cd9719eeb0'
    'data/sblgnt/text/Mark.txt' = 'fe1b5a4c15db71e7e30f33fb67966ca93baffae377085ece0b88f66316729638'
    'data/sblgnt/text/Luke.txt' = '102d65ec7a91271777115d1a605688776adfda6a1188680ccf593e27e3e876d6'
    'data/sblgnt/text/John.txt' = 'ada9643154a990ae2725ab847c6cfb576ce2c3bcd4945d47606e8a90c8d065f5'
    'data/sblgnt/text/Acts.txt' = '199867a92d9a1ef73abe85986e4489d4799bb187bdd22845f3c168c93cafa9c8'
    'data/sblgnt/text/Rom.txt' = 'a919e44983ec633d541ec1084da79cfba1c1aa7f0eac6470c1ac7ec66cf774e8'
    'data/sblgnt/text/1Cor.txt' = '2a706bac29f1e34568bf6862cbc1616471511e9b915d7292adba3e93fb72c946'
    'data/sblgnt/text/2Cor.txt' = 'af6a60d5f851762c7ee22be6f15975e706a55cf24bd0b890d6ee9ab2ad74bd18'
    'data/sblgnt/text/Gal.txt' = 'cea30996850efb7a24d5be03d5d1ea052f2200e292f8bdd190c57298bd8c2fb1'
    'data/sblgnt/text/Eph.txt' = 'c15837d245dc849384a0be9f6c76de65590ffd496474f48c16822696ca024f6a'
    'data/sblgnt/text/Phil.txt' = 'c99e25ea7856fd7a3edb1055716869117c4cba4c15f07567ea22c0269e211b85'
    'data/sblgnt/text/Col.txt' = '665ef40dbc19bacf2b2642aa0bfc265e3cfab3f37b5757ea1ff69bdaf968bd08'
    'data/sblgnt/text/1Thess.txt' = '58b57d9b87ee994757a6bd425cfb8a12cecee9b3c7d0206286fdf2b089b44ccf'
    'data/sblgnt/text/2Thess.txt' = 'c2a6922591a0282f5c07fe9fa47b8f38cd1aaf15b56eb9364aac91f5546f5822'
    'data/sblgnt/text/1Tim.txt' = 'e4813e23b26b8b4763771b252cb41982d3af66abeead62f014e5f5f0a5323dcb'
    'data/sblgnt/text/2Tim.txt' = 'eecea7f17f3493bb3e0d2d5922acf2ab10dae876156e0cbc395355c05f66d2bf'
    'data/sblgnt/text/Titus.txt' = '8a7ff3511b7cbaaba481fe92b9aa716b8fd2abcc9d54829939c2fa3f836a2267'
    'data/sblgnt/text/Phlm.txt' = '0b74d129888819be274cdcf239672f4688691ebab77c627521b5fcb185bea011'
    'data/sblgnt/text/Heb.txt' = 'a89fa2a3a8f04e8bf6a9c71404a9c45fa0e32cff5b6942043e663d8c1f40cc93'
    'data/sblgnt/text/Jas.txt' = '1343ea4125c64585c738a2f8bf54a077947d0a6229b2642b39a1ad845f40e153'
    'data/sblgnt/text/1Pet.txt' = '9c847143fbecfb2c5d0fc35820f973efb749c04a9badae99eb8d9ac0cf3d2a6d'
    'data/sblgnt/text/2Pet.txt' = '7afd9df2ea451cc6b3d55984d12fcf8d8d159b5f3d40489b4ebd375dbac8aea0'
    'data/sblgnt/text/1John.txt' = '074201796600c23d79c0abd26621ed0046290d5f227f4aaea67ac425784094c5'
    'data/sblgnt/text/2John.txt' = 'b2daf832c6f88d67ae0bdf2ecfbbb42309e330dc161f5b127c8939d5cb448bc7'
    'data/sblgnt/text/3John.txt' = 'db0bfc024dfbafa4bf0c978d6f53186c35e6eb771058672ff4f1c31708db6c16'
    'data/sblgnt/text/Jude.txt' = '9d80e8332848395a7af425ebf30edd21c9c43f0f446489f648b65c652630b734'
    'data/sblgnt/text/Rev.txt' = '61419de7970661f8c481975c35b50e7c1df0422db0c6400d1ce2c6ade35da9f0'
}

# Where each repository file is kept: the text under text/, the rest beside it.
function Local-Path([string] $repositoryPath) {
    if ($repositoryPath.StartsWith('data/sblgnt/text/')) {
        return Join-Path 'text' ($repositoryPath.Substring('data/sblgnt/text/'.Length))
    }
    return $repositoryPath
}

$target = Join-Path $ResourcesPath 'SBLGNT'

$complete = $true
foreach ($entry in $Expected.GetEnumerator()) {
    $installed = Join-Path $target (Local-Path $entry.Key)
    if (-not (Test-Path $installed) -or (Get-FileHash -Algorithm SHA256 $installed).Hash.ToLowerInvariant() -ne $entry.Value) {
        $complete = $false
        break
    }
}

if ($complete) {
    Write-Host "The SBLGNT is already in $target at $($Commit.Substring(0, 7))."
    return
}

if (Test-Path $target) {
    $kept = Get-ChildItem $target -Recurse -File | Where-Object { $_.Name -ne 'LICENCE.md' }
    if ($kept) {
        throw "$target holds files that are not the SBLGNT at $($Commit.Substring(0, 7)). Move them aside and run this again."
    }
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "sblgnt-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'text') | Out-Null

try {
    foreach ($entry in $Expected.GetEnumerator()) {
        $file = Join-Path $staging (Local-Path $entry.Key)
        Invoke-WebRequest -Uri "$Raw/$($entry.Key)" -OutFile $file
        $hash = (Get-FileHash -Algorithm SHA256 $file).Hash.ToLowerInvariant()
        if ($hash -ne $entry.Value) {
            throw "$($entry.Key) at $($Commit.Substring(0, 7)) has SHA-256 $hash, not $($entry.Value). Nothing was replaced."
        }
    }

    if ((Get-Content (Join-Path $staging 'README.md') -Raw) -notmatch 'Creative Commons Attribution 4\.0 International License') {
        throw "The SBLGNT README no longer states Creative Commons Attribution 4.0. Nothing was replaced."
    }

    if ((Get-Content (Join-Path $staging 'LICENSE') -Raw) -notmatch '^\s*Attribution 4\.0 International') {
        throw "The SBLGNT LICENSE is no longer the Attribution 4.0 International text. Nothing was replaced."
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $target 'text') | Out-Null
    foreach ($entry in $Expected.GetEnumerator()) {
        Copy-Item (Join-Path $staging (Local-Path $entry.Key)) (Join-Path $target (Local-Path $entry.Key)) -Force
    }

    Write-Host "The SBLGNT fetched into $target ($($Commit.Substring(0, 7)), $($Expected.Count) files)"
}
finally {
    Remove-Item -Recurse -Force $staging
}
