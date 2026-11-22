<#
.SYNOPSIS
    Fetches Thayer's Greek-English Lexicon of the New Testament (1889) as the Every Promise project published it.

.DESCRIPTION
    The 5,521 entries G1-G5624 as Markdown, one file per run of Strong numbers, decoded by that project from an e-Sword
    module and released as CC0. Pinned to the commit read on 2026-11-22 and checked against its hashes; the LICENSE and
    NOTICE, which say the lexicon is public domain and the edition's own rights are dedicated under CC0, are re-read
    before anything is replaced. The same repository's thayers.html is the same text on one page and is not taken.

.EXAMPLE
    ./scripts/fetch-thayer.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Commit = 'a31ff38c40a681ef1029dcc51eb7e7ee3f2ea409'
$Repository = 'https://raw.githubusercontent.com/imbennyhim/Every-Promise-Thayers'
$Files = [ordered]@{
    'LICENSE'                                = 'a2010f343487d3f7618affe54f789f5487602331c0a8d03f49e9a7c547cf0499'
    'NOTICE'                                 = 'c1c8c8d53d412e4b207d397b5f11ad6c4c2ba73a47859c1b1c52718a00e51aab'
    'README.md'                              = '03f61f08df7674ff438c24819a8b22672a289efc8a4dffbe99b0fef41c859698'
    'book/01-G1-G224.md'                     = 'fc98b8f96d2a386b01439b602ae86d48e40a0e34f3d035600a5895ea13a523db'
    'book/02-G225-G499.md'                   = 'd761be39295c10f86f410abd5c9609cdea23549e9483ac97c1b74a44c56ac7f0'
    'book/03-G500-G745.md'                   = '3ad5b82322c4c80d384b81444da5596c58896cb6433e39053c7e1cb6ca026a7a'
    'book/04-G746-G986.md'                   = 'e03fe47578b626c5c2bdc36ae3c027ffb2a3639eaf5638d4bd367e10a0dc4737'
    'book/05-G987-G1188.md'                  = 'be85dd2515a6ac93b7eafdaa83fd68dfd4f24a3e8f912da935863539e3cdd182'
    'book/06-G1189-G1383.md'                 = '9858138afa7174bdd3632f1acc9d71e130ba86e80a158defc182e8c1ddb759af'
    'book/07-G1384-G1514.md'                 = 'a2b1c7fd3d6ba7b67199f6c5b655c2654a980bc9cf78689f37369f8817ba83d5'
    'book/08-G1515-G1706.md'                 = '118097db28a7778180ce409d02811dc1eda63d3efbad5579069ae225a0dbc35c'
    'book/09-G1707-G1908.md'                 = '6cb0e0106bc6a468f1d9f5a56efd8d63ae6cc9f6ac8e58be01dcc6a88918c26b'
    'book/10-G1909-G2097.md'                 = '287a6d9bcb3a7eaabd7f31f7d4e75cbd92fa15a7201ae15a2a0ab346abcb92e4'
    'book/11-G2098-G2288.md'                 = '0c598fdcc7119c70d82ddc693b6978511ecf4e75679e97e21db0a7226d718e5c'
    'book/12-G2289-G2482.md'                 = 'a5c95891800b9456af8628cb6f6258d0e64c978b45234c13453a73f8cc7d41f5'
    'book/13-G2483-G2670.md'                 = 'b44fb8abd1de84eb9716c45183f1c97ed92bd4f3378f45dbf2b5b4541c135160'
    'book/14-G2671-G2918.md'                 = '423e6a522ae9a5c8d4525354dbf2af173b25e76bed95d873a534f0a8d3018f0d'
    'book/15-G2919-G3096.md'                 = '5826e7df8d8684a8a1a07dcab1111c6a665eb8ed4b9d834e251ea1f68498a282'
    'book/16-G3097-G3367.md'                 = '94be2fa9f8e5f76cf8008f0cdf46d91a01501614d99713084526e26a2c280871'
    'book/17-G3368-G3597.md'                 = 'e07ed049e09866cadd82e5ac0582617e432e229f95cbbba6767cb456c2497376'
    'book/18-G3598-G3766.md'                 = 'b46c6f65104e6efc2470c5a884657419261014daf5a40c5562c27ca6717ab626'
    'book/19-G3767-G3937.md'                 = 'f7c702d499bba7ea44a69df51fb64dd6096f0ac4cdfbf3244eee8e34210d4ff6'
    'book/20-G3938-G4121.md'                 = '09879257e74a4cac35fe3652d92b841c88eed9c64d81d7f21430259b482c35ad'
    'book/21-G4122-G4313.md'                 = '4f0059779b79614eba95bc1a685b62eb8a0f349732f295b45906c0060049ae54'
    'book/22-G4314-G4533.md'                 = 'b796dcf6f6b59cddbb654dbffd08ca5a7798d5bfd108b053a2b2d978c144232f'
    'book/23-G4534-G4782.md'                 = '2145817a1e4b5cbdd6f1723ecc4965f5f37a16515b14ad8d7ffd5d4fe07bb0a2'
    'book/24-G4783-G5045.md'                 = 'fb10e9dd9ade649194d4e3d4e47124f44868211ed43f364a5217d39f1ccdb530'
    'book/25-G5046-G5277.md'                 = '9ec7833476aa8c03248cfbcf4589527245356a82eec6e4021c583bd70e600749'
    'book/26-G5278-G5524.md'                 = '37d346db5bbb6088b59a480e83d9260ec2376775c98243396fd8949d9620c293'
    'book/27-G5525-G5624.md'                 = '27d27210def592c12f184a3693117a8a43d5a4ed11a4d9aa6fb9256037cbd8ce'
}

$root = Join-Path (Resolve-Path $ResourcesPath) 'Thayer'
if ((Test-Path $root) -and @($Files.Keys | Where-Object { -not (Test-Path (Join-Path $root $_)) }).Count -eq 0) {
    $moved = @($Files.Keys | Where-Object { (Get-FileHash -Algorithm SHA256 (Join-Path $root $_)).Hash.ToLowerInvariant() -ne $Files[$_] })
    if ($moved.Count -eq 0) {
        Write-Host "Thayer is current ($Commit)."
        return
    }
    throw "In $root these differ from the pinned bytes: $($moved -join ', '). Nothing was replaced; move the folder aside to fetch again."
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "thayer-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path (Join-Path $staging 'book') | Out-Null
try {
    foreach ($file in $Files.Keys) {
        $destination = Join-Path $staging $file
        Invoke-WebRequest -Uri "$Repository/$Commit/$file" -OutFile $destination -Headers @{ 'User-Agent' = 'essenthos' }
        $hash = (Get-FileHash -Algorithm SHA256 $destination).Hash.ToLowerInvariant()
        if ($hash -ne $Files[$file]) {
            throw "$file hashes to $hash, not the $($Files[$file]) read on 2026-11-22. Nothing was replaced."
        }
    }

    if ((Get-Content -Raw (Join-Path $staging 'NOTICE')) -notmatch 'PUBLIC DOMAIN' -or (Get-Content -Raw (Join-Path $staging 'LICENSE')) -notmatch 'CC0 1.0 Universal') {
        throw 'The NOTICE and LICENSE no longer say what they said on 2026-11-22 (public domain; CC0 1.0). Read the terms before taking it.'
    }

    New-Item -ItemType Directory -Force -Path (Join-Path $root 'book') | Out-Null
    Get-ChildItem -Path $staging -File -Recurse | ForEach-Object {
        $relative = $_.FullName.Substring($staging.Length).TrimStart('\', '/')
        Copy-Item $_.FullName (Join-Path $root $relative) -Force
    }
}
finally {
    Remove-Item -Recurse -Force -Path $staging -ErrorAction SilentlyContinue
}

$total = (Get-ChildItem -Path $root -File -Recurse | Measure-Object -Property Length -Sum).Sum / 1MB
Write-Host ("{0:N1} MB in {1}" -f $total, $root)
Write-Host 'Then run: python scripts/corpus-manifest.py --folders Thayer'
