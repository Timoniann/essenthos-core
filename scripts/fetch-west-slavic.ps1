<#
.SYNOPSIS
    Fetches the Slovak, Czech and Polish Bibles the corpus holds: three eBible.org USFM archives and two CrossWire
    SWORD modules, each pinned by SHA-256 with its terms re-read.

.DESCRIPTION
        BiblicaSlovak2023      eBible slk        Biblica Open Slovak Hope for All New Testament (Nádej pre každého), CC BY-SA 4.0
        Kralice1613            eBible ces1613    Bible kralická 1613, public domain
        UpdatedGdansk2018      eBible polubg     Uwspółcześniona Biblia Gdańska 2018, CC BY-ND 4.0
        CzechStudy2009         CrossWire CzeCSP  Český studijní překlad 2009, CC BY-NC-ND 4.0
        Gdansk1881             CrossWire PolGdanska  Biblia Gdańska, edition of 1881, public domain

    Every file installed, and every archive downloaded, is held to the hash read on 2026-11-24 in
    scripts/fetch-west-slavic.sha256. Before anything is installed the terms are read again where they stand: eBible's
    copyright page and catalogue row for an eBible text (and the copy of the page inside the archive, which is kept
    beside the books as copr.htm), the module's own conf for a CrossWire one, and for the Updated Gdańsk Bible
    CrossWire's conf of the same translation as well, which states other terms (kept as crosswire-polugdanska.conf).
    If a statement no longer says what it said, nothing is installed: the terms moved, and that is the owner's to read.

    A folder already holding every pinned file with its hash is current and left alone. A folder that differs stops the
    run, naming the files, and nothing is replaced; move it aside to fetch again. A new folder is assembled in staging
    and moved into place only when whole. Resources/<folder>/LICENCE.md says what each statement is and what was taken.

.EXAMPLE
    ./scripts/fetch-west-slavic.ps1

.EXAMPLE
    ./scripts/fetch-west-slavic.ps1 -Only BiblicaSlovak2023
#>

[CmdletBinding()]
param(
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources'),

    # One folder name, where only one text is wanted. All of them by default.
    [string] $Only
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$Catalogue = 'https://ebible.org/Scriptures/translations.csv'
$Packages = 'https://www.crosswire.org/ftpmirror/pub/sword/packages/rawzip'
$Confs = 'https://www.crosswire.org/ftpmirror/pub/sword/raw/mods.d'

$Texts = @(
    [pscustomobject]@{
        Folder = 'BiblicaSlovak2023'; Kind = 'ebible'; Id = 'slk'; Books = 27; Verses = 7955
        Page = 'Creative Commons Attribution Share-Alike license 4.0'
        Row = 'Copyright © 1993, 2023 Biblica, Inc.'
    }
    [pscustomobject]@{
        Folder = 'Kralice1613'; Kind = 'ebible'; Id = 'ces1613'; Books = 66; Verses = 23214 + 7958
        Page = 'Public Domain'
        Row = 'public domain'
    }
    [pscustomobject]@{
        Folder = 'UpdatedGdansk2018'; Kind = 'ebible'; Id = 'polubg'; Books = 66; Verses = 23145 + 7957
        Page = 'Creative Commons Attribution-No Derivatives license 4.0'
        Row = 'Copyright © 2018 Fundacja Wrota Nadziei'
        Also = [pscustomobject]@{
            Url = "$Confs/polugdanska.conf"; File = 'crosswire-polugdanska.conf'
            Phrases = @('DistributionLicense=Copyrighted; Free non-commercial distribution', 'content remains completely unchanged')
        }
    }
    [pscustomobject]@{
        Folder = 'CzechStudy2009'; Kind = 'sword'; Module = 'CzeCSP'; Conf = 'czecsp.conf'
        Phrases = @('DistributionLicense=Creative Commons: BY-NC-ND 4.0', 'CopyrightHolder=Nakladatelství KMS, s.r.o')
    }
    [pscustomobject]@{
        Folder = 'Gdansk1881'; Kind = 'sword'; Module = 'PolGdanska'; Conf = 'polgdanska.conf'
        Phrases = @('DistributionLicense=Public Domain', 'Description=Polish Biblia Gdanska (1881)')
    }
)

if ($Only) {
    $Texts = @($Texts | Where-Object { $_.Folder -eq $Only })
    if ($Texts.Count -eq 0) {
        throw "There is no text called `"$Only`" here. It is one of: BiblicaSlovak2023, Kralice1613, UpdatedGdansk2018, CzechStudy2009, Gdansk1881."
    }
}

# The pins: <hash>  <folder>/<path> for every file installed, <hash>  archive:<url> for every download.
$Pins = @{}
$Archives = @{}
foreach ($line in Get-Content -Encoding UTF8 (Join-Path $PSScriptRoot 'fetch-west-slavic.sha256')) {
    if ($line -match '^([0-9a-f]{64})\s+archive:(\S+)$') { $Archives[$Matches[2]] = $Matches[1] }
    elseif ($line -match '^([0-9a-f]{64})\s+(\S+)$') { $Pins[$Matches[2]] = $Matches[1] }
}

function Get-Hash([string] $Path) { (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant() }

function Get-Archive([string] $Uri, [string] $Path) {
    # eBible drops long connections; curl resumes, and the hash is the only check that knows the file is whole.
    for ($attempt = 1; $attempt -le 6; $attempt++) {
        curl.exe --silent --show-error --location --max-time 300 --continue-at - --user-agent 'essenthos' --output $Path $Uri
        if ((Test-Path $Path) -and (Get-Hash $Path) -eq $Archives[$Uri]) { return }
    }

    $found = if (Test-Path $Path) { Get-Hash $Path } else { 'nothing' }
    throw "$Uri hashes to $found, not the $($Archives[$Uri]) read on 2026-11-24. The publisher has changed it, so nothing " +
          'was installed; read what changed, and the terms, before pinning the new bytes.'
}

function Assert-Says([string] $Content, [string[]] $Phrases, [string] $Where, [string] $Folder) {
    $flat = $Content -replace '\s+', ' '
    foreach ($phrase in $Phrases) {
        if (-not $flat.Contains($phrase)) {
            throw "$Where no longer says `"$phrase`". The decision to take this text rests on that statement, so nothing " +
                  "was installed. Read what it says now and record it in Resources/$Folder/LICENCE.md before fetching again."
        }
    }
}

$resources = Resolve-Path $ResourcesPath
$staging = Join-Path ([IO.Path]::GetTempPath()) "west-slavic-$([guid]::NewGuid().ToString('n'))"
New-Item -ItemType Directory -Force -Path $staging | Out-Null

try {
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $listing = $null

    foreach ($text in $Texts) {
        $root = Join-Path $resources $text.Folder
        $pinned = @($Pins.Keys | Where-Object { $_.StartsWith("$($text.Folder)/") } | Sort-Object)
        if ($pinned.Count -eq 0) { throw "fetch-west-slavic.sha256 pins no file of $($text.Folder)." }

        if (Test-Path $root) {
            $differ = @($pinned | Where-Object {
                $path = Join-Path $resources $_
                -not (Test-Path -LiteralPath $path) -or (Get-Hash $path) -ne $Pins[$_]
            })
            if ($differ.Count -eq 0) {
                Write-Host "$($text.Folder) is current ($($pinned.Count) files as pinned)."
                continue
            }

            throw "In $root these are missing or differ from the pinned bytes: $($differ -join ', '). Nothing was replaced; " +
                  'move the folder aside to fetch again.'
        }

        $work = Join-Path $staging $text.Folder
        $built = Join-Path $staging "$($text.Folder).built"
        New-Item -ItemType Directory -Force -Path $work, $built | Out-Null

        if ($text.Kind -eq 'ebible') {
            $page = "https://ebible.org/$($text.Id)/copyright.htm"
            Write-Host "$($text.Folder): reading $page"
            $content = (Invoke-WebRequest -Uri $page -UseBasicParsing -Headers @{ 'User-Agent' = 'essenthos' }).Content
            Assert-Says $content @($text.Page) $page $text.Folder

            if (-not $listing) {
                $listing = Join-Path $staging 'translations.csv'
                Invoke-WebRequest -Uri $Catalogue -OutFile $listing -Headers @{ 'User-Agent' = 'essenthos' }
            }
            $row = Import-Csv $listing -Encoding UTF8 | Where-Object { $_.translationId -eq $text.Id }
            if (-not $row -or $row.Copyright -ne $text.Row -or $row.Redistributable -ne 'True') {
                throw "eBible's catalogue says Copyright=`"$($row.Copyright)`", Redistributable=`"$($row.Redistributable)`" " +
                      "for $($text.Id), where it said `"$($text.Row)`" and `"True`"; nothing was installed."
            }

            $uri = "https://ebible.org/Scriptures/$($text.Id)_usfm.zip"
            $zip = Join-Path $work 'archive.zip'
            Get-Archive $uri $zip
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, (Join-Path $work 'x'))

            $copy = Join-Path $work 'x' 'copr.htm'
            if (-not (Test-Path $copy)) { throw "The archive carries no copr.htm, so the terms would not travel with the bytes." }
            Assert-Says ([IO.File]::ReadAllText($copy)) @($text.Page) "the copr.htm inside $uri" $text.Folder

            $books = @(Get-ChildItem (Join-Path $work 'x') -Filter '*.usfm')
            $verses = ($books | ForEach-Object { Get-Content -Encoding UTF8 $_.FullName } | Where-Object { $_.StartsWith('\v ') }).Count
            if ($books.Count -ne $text.Books -or $verses -ne $text.Verses) {
                throw "The archive holds $($books.Count) books and $verses verses, where the edition has $($text.Books) and " +
                      "$($text.Verses); nothing was installed."
            }

            $books | Copy-Item -Destination $built
            Copy-Item $copy -Destination $built

            if ($text.Also) {
                $also = Join-Path $built $text.Also.File
                Invoke-WebRequest -Uri $text.Also.Url -OutFile $also -Headers @{ 'User-Agent' = 'essenthos' }
                Assert-Says ([IO.File]::ReadAllText($also)) $text.Also.Phrases $text.Also.Url $text.Folder
            }
        }
        else {
            $uri = "$Packages/$($text.Module).zip"
            Write-Host "$($text.Folder): fetching $uri"
            $zip = Join-Path $work 'archive.zip'
            Get-Archive $uri $zip
            $module = Join-Path $built $text.Module
            [IO.Compression.ZipFile]::ExtractToDirectory($zip, $module)
            Assert-Says ([IO.File]::ReadAllText((Join-Path $module 'mods.d' $text.Conf))) $text.Phrases "the module's conf $($text.Conf)" $text.Folder
        }

        $differ = @($pinned | Where-Object {
            $path = Join-Path $built $_.Substring($text.Folder.Length + 1)
            -not (Test-Path -LiteralPath $path) -or (Get-Hash $path) -ne $Pins[$_]
        })
        if ($differ.Count -gt 0) {
            throw "These differ from the pinned bytes although the archive matched: $($differ -join ', '). Nothing was installed."
        }

        Move-Item -LiteralPath $built -Destination $root
        $size = (Get-ChildItem $root -Recurse -File | Measure-Object -Property Length -Sum).Sum / 1MB
        Write-Host ("  {0} files, {1:N1} MB in {2}" -f $pinned.Count, $size, $root)
    }

    Write-Host 'Then run: python scripts/corpus-manifest.py --folders BiblicaSlovak2023,Kralice1613,UpdatedGdansk2018,CzechStudy2009,Gdansk1881'
}
finally {
    Remove-Item -Recurse -Force -Path $staging -ErrorAction SilentlyContinue
}
