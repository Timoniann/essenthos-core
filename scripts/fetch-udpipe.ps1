[CmdletBinding()]
param(
    # Recovery is deliberately opt-in: it discards whatever a previous run left half-fetched under
    # Resources/UDPipe and starts a clean download. Without it an unrecognised folder is refused.
    [switch] $Repair
)

$ErrorActionPreference = 'Stop'

# EVIDENTIA uses these only to *analyse* an unannotated translation. They are not corpus text,
# are not loaded into Postgres, and cannot themselves create a word link. Keeping the model bytes,
# their checksums and their licence beside each other makes a later adapter auditable and replaceable.
$destination = Join-Path $PSScriptRoot '..\Resources\UDPipe'

# What was fetched, whose it is and what it obliges is recorded in Resources/UDPipe/LICENCE.md.
# .gitignore un-ignores that one name per corpus folder, so it is the only statement here that
# survives a clone; it is maintained in the repository and never regenerated from this script.
$carried = 'LICENCE.md'
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('essenthos-udpipe-' + [guid]::NewGuid())
$models = @(
    @{ Name = 'ukrainian-iu-ud-2.5-191206.udpipe'; Md5 = 'c134ee9fad7989636fc0b6499d0c5e1e'; Bytes = 16929888; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/72ba199d-b8fc-40a5-a12d-c5ea219de998/content' },
    @{ Name = 'russian-syntagrus-ud-2.5-191206.udpipe'; Md5 = '8a985b9b7c3902bf76e55f0806ccbbd2'; Bytes = 45859472; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/f612687b-e000-4493-b078-b2d8fd9e008c/content' },
    @{ Name = 'english-ud-2.1-20180111.udpipe'; Md5 = 'a5e99059a91f04740e1a588732a8c27c'; Bytes = 16368326; Url = 'https://raw.githubusercontent.com/bnosac/udpipe.models.ud/master/models/english-ud-2.1-20180111.udpipe' }
)

if (Test-Path -LiteralPath $destination) {
    $expectedModels = $models | ForEach-Object { Join-Path $destination ('models\' + $_.Name) }
    $executable = Join-Path $destination 'bin\udpipe.exe'
    $missing = @($expectedModels | Where-Object { -not (Test-Path -LiteralPath $_) })
    if ($missing.Count -eq 0 -and (Test-Path -LiteralPath $executable)) {
        foreach ($model in $models) {
            $actual = (Get-FileHash -Algorithm MD5 -LiteralPath (Join-Path $destination ('models\' + $model.Name))).Hash.ToLowerInvariant()
            if ($actual -ne $model.Md5) {
                throw "Existing $($model.Name) has MD5 $actual, not pinned $($model.Md5); refusing to merge a replacement."
            }
        }
        Write-Host 'UDPipe 1.4.0 and both pinned models are already present and verified; nothing downloaded.'
        return
    }
    # A fresh clone already has this folder holding LICENCE.md alone, because .gitignore un-ignores
    # exactly that one name per corpus folder. That is an empty destination, not a half-finished
    # download, and refusing it would make the script unrunnable on every clone.
    $fetched = @(Get-ChildItem -LiteralPath $destination -Force | Where-Object { $_.Name -ne $carried })
    if ($fetched.Count -gt 0) {
        if (-not $Repair) {
            throw "UDPipe destination holds more than $carried and no complete download. Refusing to merge into $destination; inspect it, or rerun with -Repair to discard what is there and fetch cleanly."
        }
        $fetched | Remove-Item -Recurse -Force
        Write-Host 'Discarded the incomplete UDPipe download; fetching it cleanly.'
    }
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

    # The three original licence texts are kept, not paraphrased: CC BY-NC-SA 4.0 for the two
    # LINDAT models, CC BY-SA 4.0 for the English one, MPL-2.0 for the tool.
    $binaryDirectory = Join-Path $ready 'bin'
    New-Item -ItemType Directory -Force -Path $binaryDirectory | Out-Null
    Copy-Item -LiteralPath $executable.FullName -Destination (Join-Path $binaryDirectory 'udpipe.exe')
    Invoke-WebRequest -Uri 'https://creativecommons.org/licenses/by-nc-sa/4.0/legalcode.txt' -OutFile (Join-Path $ready 'CC-BY-NC-SA-4.0.txt')
    Invoke-WebRequest -Uri 'https://creativecommons.org/licenses/by-sa/4.0/legalcode.txt' -OutFile (Join-Path $ready 'CC-BY-SA-4.0.txt')
    Invoke-WebRequest -Uri 'https://raw.githubusercontent.com/ufal/udpipe/v1.4.0/LICENSE' -OutFile (Join-Path $ready 'MPL-2.0.txt')
    New-Item -ItemType Directory -Force -Path $destination | Out-Null
    Get-ChildItem -LiteralPath $ready -Force |
        Move-Item -Destination $destination -Force
    Get-ChildItem -LiteralPath $destination -File -Recurse | Get-FileHash -Algorithm SHA256 |
        Select-Object Path, Hash | Format-Table -AutoSize
}
finally {
    if (Test-Path -LiteralPath $staging) {
        Remove-Item -LiteralPath $staging -Recurse -Force
    }
}
