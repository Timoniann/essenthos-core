<#
.SYNOPSIS
    Fetches the Chinese Union Version and the Korean Revised Version as CrossWire publishes them:
    three SWORD modules, and the statements their terms rest on.

.DESCRIPTION
    ChiUn and ChiUns are the Chinese Union Version of 1919 in traditional and simplified characters,
    rebuilt by CrossWire in 2021 from the text the Faith Hope Love foundation (bible.fhl.net) typed
    from the 1919 printing, with FHL's Strong numbers on its words. KorRV is 개역한글, the Korean
    Revised Version of 1961, taken by CrossWire from Korean Wikisource.

    Each module is pinned to the archive read on 2026-09-25 and checked against its hash then: the
    decision to take the text, the segmentation and the numbers measured on it were all made against
    those bytes. The two statements the terms rest on are re-read at their source before anything is
    replaced: FHL's copyright page, which says the typed text is the out-of-copyright 1919 edition and
    that its Strong numbers are released under the GNU FDL, and the Korean Bible Society's answer that
    its 1961 text is free of charge to use. If either no longer says so, nothing is replaced — the
    terms moved, and that is the owner's to read, not a download's.

    Resources/ChineseUnion1919/LICENCE.md and Resources/KoreanRevised1961/LICENCE.md say what each
    statement is and what was decided on it.

.EXAMPLE
    ./scripts/fetch-crosswire.ps1
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Packages = 'https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip'

# Each module: the folder it goes into, and what its archive was on 2026-09-25.
$Modules = [ordered]@{
    'ChiUn'  = @{ Folder = 'ChineseUnion1919'; Sha256 = 'f03e7e5f031521c18fe6dad268d8f5ce0570b4584959c72e3a5f100bfcd38676' }
    'ChiUns' = @{ Folder = 'ChineseUnion1919'; Sha256 = '37fcef08bd3b5f42f5d7b8e0a9326075f9f41e6aa39906711b092f105ae4857e' }
    'KorRV'  = @{ Folder = 'KoreanRevised1961'; Sha256 = '194dd52ceabdefb97a60f6627de22e97990e0a43b1b388caf4013d010884ecd7' }
}

# The statements, the file each is kept in beside the modules, and the phrases the decision rests on.
$Statements = @(
    @{
        Folder  = 'ChineseUnion1919'
        File    = 'fhl-copyright.html'
        Url     = 'https://www.fhl.net/gb/fhl/fhl8.html'
        Phrases = @('1919', 'FHL和合本', 'FDL')
    },
    @{
        Folder  = 'KoreanRevised1961'
        File    = 'kbs-copyright-faq.html'
        Url     = 'https://www.bskorea.or.kr/bbs/board.php?bo_table=copyright_faq&wr_id=5'
        Phrases = @('개역한글판', '저작권료 지급없이 사용 가능', '동일성유지권', '성명표시권')
    }
)

$staging = Join-Path ([IO.Path]::GetTempPath()) "crosswire-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    foreach ($statement in $Statements) {
        Write-Host "Reading $($statement.Url)"
        $path = Join-Path $staging $statement.File
        Invoke-WebRequest -Uri $statement.Url -OutFile $path -UseBasicParsing -Headers @{ 'User-Agent' = 'Mozilla/5.0 (essenthos)' }
        $flattened = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) -replace '\s+', ' '
        foreach ($phrase in $statement.Phrases) {
            if (-not $flattened.Contains($phrase)) {
                throw "$($statement.Url) no longer says `"$phrase`". The decision to take this text was made " +
                      "against that statement, so nothing was replaced. Read what the page says now and record " +
                      "it in Resources/$($statement.Folder)/LICENCE.md before fetching again."
            }
        }
    }

    foreach ($module in $Modules.Keys) {
        $archive = Join-Path $staging "$module.zip"
        Invoke-WebRequest -Uri "$Packages/$module.zip" -OutFile $archive -Headers @{ 'User-Agent' = 'essenthos' }
        $hash = (Get-FileHash -Algorithm SHA256 $archive).Hash.ToLowerInvariant()
        if ($hash -ne $Modules[$module].Sha256) {
            throw "$module.zip hashes to $hash, where it hashed to $($Modules[$module].Sha256) on 2026-09-25. " +
                  "CrossWire has published a different module, so nothing was replaced; read its History " +
                  "lines and compare the text before taking it."
        }

        Expand-Archive -Path $archive -DestinationPath (Join-Path $staging $module) -Force
    }

    $root = Resolve-Path $ResourcesPath
    foreach ($module in $Modules.Keys) {
        $target = Join-Path $root $Modules[$module].Folder $module
        if (Test-Path $target) {
            Remove-Item -Recurse -Force $target
        }

        New-Item -ItemType Directory -Force -Path (Split-Path $target) | Out-Null
        Copy-Item -Recurse -Path (Join-Path $staging $module) -Destination $target
    }

    foreach ($statement in $Statements) {
        Copy-Item -Path (Join-Path $staging $statement.File) -Destination (Join-Path $root $statement.Folder) -Force
    }

    foreach ($folder in ($Modules.Values.Folder | Select-Object -Unique)) {
        $size = (Get-ChildItem (Join-Path $root $folder) -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
        Write-Host ("{0:N1} MB in {1}" -f $size, (Join-Path $root $folder))
    }

    Write-Host "Then run: python scripts/corpus-manifest.py --folders ChineseUnion1919,KoreanRevised1961"
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}
