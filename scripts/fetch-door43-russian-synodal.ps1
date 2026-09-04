<#
.SYNOPSIS
    Fetches the only word-aligned Russian Synodal text anyone publishes: Titus, Philemon and
    2 John, aligned to the Greek by hand in translationCore and dedicated to the public domain.

.DESCRIPTION
    The Synodal is the largest text in this corpus with no stated word-level correspondence at
    all — every link it has to an original came out of a model, and nothing could score them.
    These three books are the whole of what exists. 1,266 alignment milestones over 84 verses;
    small, and enough to say how right the model is where a person also answered.

    They are the same USFM 3.0 word alignment the Ukrainian interlinear arrives in, so the reader
    already here parses them and the loader already here joins them.

    The Door43 catalogue holds several uploads of each book, and this takes the fullest of each:
    Anna's, which are also what the aggregate ru_rsb repository carries. Anatolii's Titus is eight
    milestones short and his 2 John nine; aleksey.ignashin's Titus is byte-identical to Anna's but
    for the timestamp in its \id line.

    The licence is checked rather than assumed, and it is checked because the same alignment is
    published twice under two different terms. Each per-book repository states CC0 1.0 in its own
    LICENSE.md and again in its manifest, and that is the statement attached to these bytes. The
    aggregate at BSA/ru_rsb states CC BY-SA 4.0 over the same three books. This fetch takes the
    per-book repositories and stops if either statement in them moves.

.EXAMPLE
    ./scripts/fetch-door43-russian-synodal.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # A branch, tag or commit of each repository below.
    [string] $Ref = 'master'
)

$ErrorActionPreference = 'Stop'

$Host43 = 'https://git.door43.org'

# Owner/repo, the aligned file in it, the name it takes here, and the milestone count this was
# written against. The file name carries the Paratext book number the loader reads the book from,
# which is what the Ukrainian folder beside this one is named by.
$Books = @(
    [ordered]@{ Repository = 'Anna/ru_rsb_tit_book'; Source = 'ru_rsb_tit_book.usfm'; Name = '57-TIT.usfm'; Spans = 672 }
    [ordered]@{ Repository = 'Anna/ru_rsb_phm_book'; Source = 'ru_rsb_phm_book.usfm'; Name = '58-PHM.usfm'; Spans = 340 }
    [ordered]@{ Repository = 'Anna/ru_rsb_2jn_book'; Source = 'ru_rsb_2jn_book.usfm'; Name = '64-2JN.usfm'; Spans = 254 }
)

$ExpectedDedication = 'Creative Commons CC0 1.0 Universal (CC0 1.0) Public Domain Dedication'
# Written without spaces because the manifest is compared with its own whitespace removed, so that
# a reformatted file still reads as the same statement.
$ExpectedManifestLicence = '"license":"CC01.0PublicDomain"'
$ExpectedResource = '"resource":{"id":"rsb","name":"RussianSynodalBible"}'

$staging = Join-Path ([IO.Path]::GetTempPath()) "ru_rsb-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

# Invoke-WebRequest rather than Invoke-RestMethod: the manifest is JSON and the latter would parse
# it, leaving nothing to check the licence string against.
function Get-Door43File([string] $repository, [string] $file) {
    (Invoke-WebRequest -Uri "$Host43/$repository/raw/branch/$Ref/$file" `
        -Headers @{ 'User-Agent' = 'essenthos' }).Content
}

try {
    $total = 0
    foreach ($book in $Books) {
        $repository = $book.Repository
        Write-Host "Fetching $repository"

        $licence = Get-Door43File $repository 'LICENSE.md'
        if ($licence -notmatch [regex]::Escape($ExpectedDedication)) {
            throw "$repository no longer dedicates its work to the public domain under CC0. The " +
                  "same alignment is published at BSA/ru_rsb under CC BY-SA 4.0, so a change here " +
                  "is a licence decision and not a download. Nothing was replaced."
        }

        # The manifest is the uploader's own statement, and it is the one that names both the
        # licence and which Russian text this is.
        $manifest = (Get-Door43File $repository 'manifest.json') -replace '\s', ''
        if ($manifest -notmatch [regex]::Escape($ExpectedManifestLicence)) {
            throw "$repository's manifest no longer states CC0 1.0. Nothing was replaced."
        }

        if ($manifest -notmatch [regex]::Escape($ExpectedResource)) {
            throw "$repository's manifest no longer says its text is the Russian Synodal Bible. " +
                  "An alignment of a different Russian translation does not join to our Synodal " +
                  "by word position. Nothing was replaced."
        }

        $usfm = Get-Door43File $repository $book.Source
        $spans = ([regex]::Matches($usfm, 'zaln-s')).Count
        if ($spans -ne $book.Spans) {
            throw "$($book.Source) holds $spans alignment milestones and this was written against " +
                  "$($book.Spans). Either the download is partial or the alignment moved; nothing " +
                  "was replaced."
        }

        # The plain <book>.usfm beside it in every one of these repositories is the unaligned
        # source text. Taking it by mistake loads a book with no correspondence in it at all.
        if ($usfm -notmatch 'x-strong="G') {
            throw "$($book.Source) carries no Greek Strong numbers, so it is not the aligned file. " +
                  "Nothing was replaced."
        }

        [IO.File]::WriteAllText((Join-Path $staging $book.Name), $usfm)
        [IO.File]::WriteAllText((Join-Path $staging "LICENSE-$($book.Name -replace '\.usfm$', '').md"), $licence)
        $total += $spans
    }

    $root = Join-Path (Resolve-Path $ResourcesPath) 'Door43' 'ru_rsb'
    New-Item -ItemType Directory -Force -Path $root | Out-Null
    Get-ChildItem $root -File | Remove-Item -Force
    Copy-Item -Path (Join-Path $staging '*') -Destination $root -Force

    Write-Host "$total alignment milestones over $($Books.Count) books in $root"
    Write-Host "Taken from $Host43, branch $Ref, under CC0 1.0."
    Write-Host "Then run: python scripts/corpus-manifest.py, and commit the manifest."
    Write-Host ("A reload is not automatic: the loader returns early once the Synodal has any " +
                "stated link, so a restart alone picks nothing up.")
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
