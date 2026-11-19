<#
.SYNOPSIS
    Fetches the books of the Ohienko that people aligned word by word in translationCore projects of
    their own on Door43, outside unfoldingWord's Ukrainian interlinear, into
    Resources/Door43/uk_ubio_books.

.DESCRIPTION
    uk_ts/uk_ubio aligns parts of eight New Testament books of the Ohienko. Four personal projects
    align Hebrews, Romans, Revelation and Philippians nearly whole, and three add verses of John, Luke
    and 1 Peter. They are the same USFM 3 alignment over the same base text, read by the same reader
    (TSK-1905; Resources/Door43/LICENCE.md says whose each is and what its terms oblige).

    All seven are fetched; the producer reads only the books Door43BookAlignment lists. Hebrews and
    Luke are held back because too many of their links read by hand were wrong, and are kept so that
    the measurement can be repeated on the same bytes.

    Each project is pinned to a commit, and each file taken from it to the SHA-256 it had then: the
    aligned book translationCore exported on upload (<repository name>.usfm, not the unaligned
    <book>.usfm beside it), the project's LICENSE.md and its manifest.json. The licence and manifest
    are kept beside the book as LICENSE-<book>.md and manifest-<book>.json. The licence each states was
    read before the decision to take it, so a file whose hash moved stops the fetch.

    A folder already holding every file with its pinned hash is left alone. A file there with another
    hash is not replaced: say so and stop, since replacing it is a decision, not a repair.

.EXAMPLE
    ./scripts/fetch-door43-ohienko-books.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Host43 = 'https://git.door43.org'
$Folder = 'uk_ubio_books'

$PublicDomain = 'a16931732c135f9147e275e3499863f712ba925fd8a265f4095474518698937c'
$ShareAlike = '818d8dec9c93aff533c5d4b803485ab57f9724d68877247c9ce3b357a9ba7888'

# The file name carries the Paratext book number the producer reads the book from, as uk_ubio's do.
$Projects = @(
    @{ Name = '43-LUK'; Repository = 'AndreyParf/uk_ubio_luk_book'; Commit = 'c76126f181b76d56ad9a28668f3c9178b0535dbd'
       Book = '342c01ee2c39bf274bd3e7bae1fe552d5cd9ca586caf39c1e1ea2575a1850c8a'; Licence = $ShareAlike
       Manifest = '90d39be11f32727d0fe11a406c07c2f8796c754248e22053a7f4d2669bbab786'; Milestones = 2110 }
    @{ Name = '44-JHN'; Repository = 'davidmax/uk_ubi_jhn_book'; Commit = '356181788046b9a96bf601cb8366befaf3ea90fa'
       Book = '986cc2ebede58f3ab523f7fe82e290e65e1a47541e58a4bceab64a5fa5d5c069'; Licence = $PublicDomain
       Manifest = 'f45b5cc5549af48aaa555686c3ef38df6666cb375f9a416359017cda264a3f64'; Milestones = 4460 }
    @{ Name = '46-ROM'; Repository = 'ViraVlasiuk/uk_ubio_rom_book'; Commit = '10f17542785e4fa33a8a54373b06bb6131f760bf'
       Book = '54595bbeb4481bd710d0a2a9880f0019b32f43d5633284f35cc8a4bbc23b0746'; Licence = $PublicDomain
       Manifest = '41df62caf09cb6ce8ccf65325ac365b6bd15e1bfbaf65ae1e547c27a9a1a4e4e'; Milestones = 5802 }
    @{ Name = '51-PHP'; Repository = 'ViraVlasiuk/uk_ubio_php_book'; Commit = '07959c15f22a8b147195a06c1c1d75e30dcb4545'
       Book = 'ff9fd71b72d573a33c6d50e97f36f176856757dd0f1e05cf9a2c8d887e9b420f'; Licence = $ShareAlike
       Manifest = '3fd2e86a0ffd2ff66472fd566662e0becb638496931d8ae0343f8ddfbd07ae54'; Milestones = 1395 }
    @{ Name = '59-HEB'; Repository = 'IlliaTs/uk_ubio_heb_book'; Commit = 'a4cdbb27d5c6bffea637fd49fd634d47df3ccf72'
       Book = '1e62ecc5080cf3df03d76e5313ec10d0b228df48877eb5562f33ead348dba876'; Licence = $PublicDomain
       Manifest = 'd4614069dc6a31b86124bea80aed12b8dd66552610e988f3daf8f0ff0a8510c1'; Milestones = 4232 }
    @{ Name = '61-1PE'; Repository = 'AndreyParf/uk_ubio_1pe_book'; Commit = '241510b52609dde8a4edf4df9b2e3618fed32718'
       Book = '18c5e408f2b179be4dcec565d9df46144f32f276947e43f152d6fc748dbd8426'; Licence = $ShareAlike
       Manifest = '75bbad53350778e3827b07d2fd6a48108fb492ee9469a2b7570ab897bc3bc1ea'; Milestones = 370 }
    @{ Name = '67-REV'; Repository = 'ViraVlasiuk/uk_ogi_rev_book'; Commit = '324455619b1e2966d2593672daca61bf050822d5'
       Book = 'd22d3afc14204bbb52f604e57d72b4e80303156a05c88623ab4ce80a2f550b06'; Licence = $ShareAlike
       Manifest = 'a727e60956fabdde00487a36acedad0ab4b7054c82d4d4523fba1fc59d766797'; Milestones = 7742 }
)

function Get-Files($project) {
    $repositoryName = $project.Repository.Split('/')[1]
    @(
        @{ Source = "$repositoryName.usfm"; Target = "$($project.Name).usfm"; Sha256 = $project.Book }
        @{ Source = 'LICENSE.md'; Target = "LICENSE-$($project.Name).md"; Sha256 = $project.Licence }
        @{ Source = 'manifest.json'; Target = "manifest-$($project.Name).json"; Sha256 = $project.Manifest }
    )
}

function Get-Sha256([string] $path) { (Get-FileHash -Algorithm SHA256 $path).Hash.ToLowerInvariant() }

$targetFolder = Join-Path (Resolve-Path $ResourcesPath) 'Door43' $Folder

# What is already there either is what was pinned or stops everything: a half-replaced folder would
# mix two states of somebody's work under one set of hashes.
$present = 0
$expected = 0
foreach ($project in $Projects) {
    foreach ($file in Get-Files $project) {
        $expected++
        $target = Join-Path $targetFolder $file.Target
        if (-not (Test-Path $target)) { continue }
        $hash = Get-Sha256 $target
        if ($hash -ne $file.Sha256) {
            throw "$target hashes to $hash, not to the $($file.Sha256) pinned for $($project.Repository) at " +
                  "$($project.Commit). Nothing was replaced: find out where it came from before removing it."
        }
        $present++
    }
}

if ($present -eq $expected) {
    Write-Host "$targetFolder already holds every pinned file."
    return
}

$staging = Join-Path ([IO.Path]::GetTempPath()) "door43-ohienko-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    foreach ($project in $Projects) {
        Write-Host "Fetching $($project.Repository) at $($project.Commit)"
        foreach ($file in Get-Files $project) {
            $downloaded = Join-Path $staging $file.Target
            Invoke-WebRequest -Uri "$Host43/$($project.Repository)/raw/commit/$($project.Commit)/$($file.Source)" `
                -OutFile $downloaded -Headers @{ 'User-Agent' = 'essenthos' }
            $hash = Get-Sha256 $downloaded
            if ($hash -ne $file.Sha256) {
                throw "$($project.Repository)/$($file.Source) hashes to $hash, where it hashed to $($file.Sha256) at " +
                      "commit $($project.Commit). Nothing was replaced; a pinned commit should not change, so look at what was served."
            }
        }

        $usfm = [IO.File]::ReadAllText((Join-Path $staging "$($project.Name).usfm"))
        $milestones = ([regex]::Matches($usfm, 'zaln-s')).Count
        if ($milestones -ne $project.Milestones) {
            throw "$($project.Name).usfm holds $milestones alignment milestones, not $($project.Milestones). Nothing was replaced."
        }
    }

    New-Item -ItemType Directory -Force -Path $targetFolder | Out-Null
    foreach ($project in $Projects) {
        foreach ($file in Get-Files $project) {
            $target = Join-Path $targetFolder $file.Target
            if (-not (Test-Path $target)) {
                Copy-Item -Path (Join-Path $staging $file.Target) -Destination $target
            }
        }
    }

    Write-Host "$($Projects.Count) projects, $(($Projects | Measure-Object -Property Milestones -Sum).Sum) alignment milestones in $targetFolder"
    Write-Host "Then run: python scripts/corpus-manifest.py --folders Door43"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
