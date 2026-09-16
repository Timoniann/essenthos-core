[CmdletBinding()]
param(
    # Recovery is deliberately opt-in: it removes only a previous incomplete bootstrap folder
    # whose provenance identifies it as this script's own output, then starts a clean download.
    [switch] $Repair
)

$ErrorActionPreference = 'Stop'

# EVIDENTIA uses these only to *analyse* an unannotated translation. They are not corpus text,
# are not loaded into Postgres, and cannot themselves create a word link. Keeping the model bytes,
# their checksums and their licence beside each other makes a later adapter auditable and replaceable.
$destination = Join-Path $PSScriptRoot '..\Resources\UDPipe'
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('essenthos-udpipe-' + [guid]::NewGuid())
$models = @(
    @{ Name = 'ukrainian-iu-ud-2.5-191206.udpipe'; Md5 = 'c134ee9fad7989636fc0b6499d0c5e1e'; Bytes = 16929888; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/72ba199d-b8fc-40a5-a12d-c5ea219de998/content' },
    @{ Name = 'russian-syntagrus-ud-2.5-191206.udpipe'; Md5 = '8a985b9b7c3902bf76e55f0806ccbbd2'; Bytes = 45859472; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/f612687b-e000-4493-b078-b2d8fd9e008c/content' },
    @{ Name = 'english-ud-2.1-20180111.udpipe'; Md5 = 'a5e99059a91f04740e1a588732a8c27c'; Bytes = 16368326; Url = 'https://raw.githubusercontent.com/bnosac/udpipe.models.ud/master/models/english-ud-2.1-20180111.udpipe' }
)

if (Test-Path -LiteralPath $destination) {
    $expectedModels = $models | ForEach-Object { Join-Path $destination ('models\' + $_.Name) }
    $executable = Join-Path $destination 'bin\udpipe.exe'
    $complete = $expectedModels | ForEach-Object { Test-Path -LiteralPath $_ } | Where-Object { -not $_ }
    if ($complete.Count -eq 0 -and (Test-Path -LiteralPath $executable)) {
        foreach ($model in $models) {
            $actual = (Get-FileHash -Algorithm MD5 -LiteralPath (Join-Path $destination ('models\' + $model.Name))).Hash.ToLowerInvariant()
            if ($actual -ne $model.Md5) {
                throw "Existing $($model.Name) has MD5 $actual, not pinned $($model.Md5); refusing to merge a replacement."
            }
        }
        Write-Host 'UDPipe 1.4.0 and both pinned models are already present and verified; nothing downloaded.'
        return
    }
    $provenance = Join-Path $destination 'PROVENANCE.md'
    if (-not $Repair -or -not (Test-Path -LiteralPath $provenance) -or
        -not ((Get-Content -Raw -LiteralPath $provenance) -match 'UDPipe — audited local analysis tools')) {
        throw "UDPipe destination exists but is incomplete. Refusing to merge into $destination; inspect it or rerun with -Repair if it is this script's own bootstrap folder."
    }
    Remove-Item -LiteralPath $destination -Recurse -Force
    Write-Host 'Removed the incomplete UDPipe bootstrap folder created by this script; fetching it cleanly.'
}

try {
    New-Item -ItemType Directory -Force -Path $staging | Out-Null
    $ready = Join-Path $staging 'UDPipe'
    $modelDirectory = Join-Path $ready 'models'
    $binaryArchive = Join-Path $staging 'udpipe-1.4.0-bin.zip'
    New-Item -ItemType Directory -Force -Path $modelDirectory | Out-Null

    foreach ($model in $models) {
        $file = Join-Path $modelDirectory $model.Name
        Invoke-WebRequest -Uri $model.Url -OutFile $file
        $actual = (Get-FileHash -Algorithm MD5 -LiteralPath $file).Hash.ToLowerInvariant()
        if ($actual -ne $model.Md5 -or (Get-Item -LiteralPath $file).Length -ne $model.Bytes) {
            throw "Refusing $($model.Name): expected $($model.Bytes) bytes / MD5 $($model.Md5), got $((Get-Item -LiteralPath $file).Length) / $actual."
        }
    }

    Invoke-WebRequest -Uri 'https://github.com/ufal/udpipe/releases/download/v1.4.0/udpipe-1.4.0-bin.zip' -OutFile $binaryArchive
    $extracted = Join-Path $staging 'release'
    Expand-Archive -LiteralPath $binaryArchive -DestinationPath $extracted
    $executable = Get-ChildItem -Path $extracted -Filter 'udpipe.exe' -Recurse |
        Where-Object { $_.DirectoryName -match 'bin-win64$' } |
        Select-Object -First 1
    if ($null -eq $executable) {
        throw 'The verified UDPipe 1.4.0 release contained no Windows udpipe.exe.'
    }

    # The two original licence texts are kept, not paraphrased. The repository record names the
    # model licence CC BY-NC-SA 4.0; the tool itself is MPL-2.0.
    $binaryDirectory = Join-Path $ready 'bin'
    New-Item -ItemType Directory -Force -Path $binaryDirectory | Out-Null
    Copy-Item -LiteralPath $executable.FullName -Destination (Join-Path $binaryDirectory 'udpipe.exe')
    Invoke-WebRequest -Uri 'https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.txt' -OutFile (Join-Path $ready 'CC-BY-NC-SA-4.0.txt')
    Invoke-WebRequest -Uri 'https://creativecommons.org/licenses/by-sa/4.0/legalcode.txt' -OutFile (Join-Path $ready 'CC-BY-SA-4.0.txt')
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/ufal/udpipe/v1.4.0/LICENSE' -OutFile (Join-Path $ready 'MPL-2.0.txt')
    @"
# UDPipe — audited local analysis tools

Fetched by `scripts/fetch-udpipe.ps1`, never silently by the application.

| Component | Pinned source | Licence | Purpose |
| --- | --- | --- | --- |
| Ukrainian-IU model | LINDAT item 11234/1-3131, UD 2.5, 2019-12-06 | CC BY-NC-SA 4.0 | Ukrainian lemma, UPOS, features and dependency parse candidate |
| Russian-SynTagRus model | LINDAT item 11234/1-3131, UD 2.5, 2019-12-06 | CC BY-NC-SA 4.0 | Russian lemma, UPOS, features and dependency parse candidate |
| English UD model | bnosac/udpipe.models.ud, UD 2.1, 2018-01-11 | CC BY-SA 4.0 | English lemma, UPOS, features and dependency parse candidate |
| UDPipe 1.4.0 executable | github.com/ufal/udpipe release v1.4.0 | MPL-2.0 | Local tokenisation, tagging, lemmatisation and parsing |

The Ukrainian and Russian models are derived from Universal Dependencies 2.5 and the LINDAT
catalogue supplies the CC BY-NC-SA 4.0 licence. The included license files are the original legal
texts. Attribute Milan Straka and Jana Straková / Charles University, ÚFAL and retain this note
wherever model-derived annotation is redistributed. Model output remains a candidate analysis; it
is neither a source claim nor a persisted word mapping.

The English model is made by bnosac from UD English and is CC BY-SA 4.0. Attribute bnosac and
the UD English contributors; assess ShareAlike separately if model-derived annotations are ever
redistributed.
"@ | Set-Content -LiteralPath (Join-Path $ready 'PROVENANCE.md') -Encoding utf8

    Move-Item -LiteralPath $ready -Destination $destination
    Get-ChildItem -LiteralPath $destination -File -Recurse | Get-FileHash -Algorithm SHA256 |
        Select-Object Path, Hash | Format-Table -AutoSize
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
