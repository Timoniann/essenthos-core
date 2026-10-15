[CmdletBinding()]
param(
    # Recovery is deliberately opt-in: it discards whatever a previous run left half-fetched under
    # Resources/UDPipe and starts a clean download. Without it an unrecognised folder is refused.
    [switch] $Repair,
    # The default matches Dataset:ResourcesPath: this project's own Resources folder.
    [string] $ResourcesPath = (Join-Path $PSScriptRoot '..' 'Resources')
)

$ErrorActionPreference = 'Stop'

# EVIDENTIA uses these only to *analyse* an unannotated translation. They are not corpus text,
# are not loaded into Postgres, and cannot themselves create a word link. Keeping the model bytes,
# their checksums and their licence beside each other makes a later adapter auditable and replaceable.
$destination = Join-Path $ResourcesPath 'UDPipe'

# What was fetched, whose it is and what it obliges is recorded in Resources/UDPipe/LICENCE.md.
# .gitignore un-ignores that one name per corpus folder, so it is the only statement here that
# survives a clone; it is maintained in the repository and never regenerated from this script.
$carried = 'LICENCE.md'
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ('essenthos-udpipe-' + [guid]::NewGuid())
$models = @(
    @{ Name = 'ukrainian-iu-ud-2.5-191206.udpipe'; Md5 = 'c134ee9fad7989636fc0b6499d0c5e1e'; Bytes = 16929888; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/72ba199d-b8fc-40a5-a12d-c5ea219de998/content' },
    @{ Name = 'russian-syntagrus-ud-2.5-191206.udpipe'; Md5 = '8a985b9b7c3902bf76e55f0806ccbbd2'; Bytes = 45859472; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/f612687b-e000-4493-b078-b2d8fd9e008c/content' },
    @{ Name = 'english-ud-2.1-20180111.udpipe'; Md5 = 'a5e99059a91f04740e1a588732a8c27c'; Bytes = 16368326; Url = 'https://raw.githubusercontent.com/bnosac/udpipe.models.ud/master/models/english-ud-2.1-20180111.udpipe' },
    @{ Name = 'german-hdt-ud-2.5-191206.udpipe'; Md5 = '7f0a828e795c0f0c630629b4ab1a017d'; Bytes = 62982275; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/0d5a53e7-039b-4997-af3f-94faad001a6c/content' },
    @{ Name = 'spanish-ancora-ud-2.5-191206.udpipe'; Md5 = '08e63ea07501b2917d29c011276a499a'; Bytes = 20400504; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/52474ca8-939b-4fcf-80b0-228679464582/content' },
    @{ Name = 'arabic-padt-ud-2.5-191206.udpipe'; Md5 = '4c4e36160d4d62fb096857aceed0a9a4'; Bytes = 19752087; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/950d4077-bd61-4e5c-af05-ac9de11ddb39/content' },
    @{ Name = 'ancient_greek-proiel-ud-2.5-191206.udpipe'; Md5 = '26ba3e4b8f9b0b1dc0d853841ace3e3c'; Bytes = 22597368; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/f7fe8e97-506c-4f8c-a36c-bc835ec486a0/content' },
    @{ Name = 'latin-proiel-ud-2.5-191206.udpipe'; Md5 = '9418fc6a1c750e7d94791dc19d32f068'; Bytes = 23839923; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/ed8a6a71-3753-471b-9941-1d384765f89b/content' },
    @{ Name = 'french-gsd-ud-2.5-191206.udpipe'; Md5 = '8b558d0398b6d6fabeddc0cf4454f950'; Bytes = 23811948; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/75091a21-f175-4a57-8bbd-8f9a12a7d417/content' },
    @{ Name = 'portuguese-bosque-ud-2.5-191206.udpipe'; Md5 = 'a6d426f6e49b95f8425bc5cc546c4c77'; Bytes = 16974480; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/29d8c3fd-1eb3-479f-93fc-e8982b051aa2/content' },
    @{ Name = 'hindi-hdtb-ud-2.5-191206.udpipe'; Md5 = '31c61702c78985b6720a9f6ecce6557b'; Bytes = 25857814; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/08ffe51d-1072-4884-90e1-58693a198390/content' },
    @{ Name = 'marathi-ufal-ud-2.5-191206.udpipe'; Md5 = 'd6ac0c0cae7c5b9216bfa1622cd3e6f3'; Bytes = 2382087; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/b7a1c990-cb8d-4b57-b8a3-87504d409e19/content' },
    @{ Name = 'tamil-ttb-ud-2.5-191206.udpipe'; Md5 = 'c5e4ff1566798f96905eaa01e70e1732'; Bytes = 3201866; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/8b35bcb3-a284-4fce-93fe-118becba5018/content' },
    @{ Name = 'telugu-mtg-ud-2.5-191206.udpipe'; Md5 = '7bcfa7add2d74fa49cb51f2b1cad5b0e'; Bytes = 2168122; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/998b4270-a70a-42fe-a292-b70d7d66d96c/content' },
    @{ Name = 'korean-kaist-ud-2.5-191206.udpipe'; Md5 = 'b49f974d6886fb24fccd129e4ed9d2a9'; Bytes = 41461296; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/bf911ab5-54e7-4833-81ea-5e395da8fa33/content' },
    @{ Name = 'vietnamese-vtb-ud-2.5-191206.udpipe'; Md5 = '2f029f63a9ddc4f95d3154ef30434007'; Bytes = 4673798; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/ec8bcc42-f06a-4193-9625-2e7be9fb587a/content' },
    @{ Name = 'chinese-gsd-ud-2.5-191206.udpipe'; Md5 = '6268b716f0be98553c73815593d72ef1'; Bytes = 13863905; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/1dca5fb3-5bed-49d7-95d7-d35ba8832040/content' },
    @{ Name = 'chinese-gsdsimp-ud-2.5-191206.udpipe'; Md5 = '4db292a2b8d8bb88b962589a5d6a82bf'; Bytes = 11864178; Url = 'https://lindat.mff.cuni.cz/repository/server/api/core/bitstreams/63505e33-0290-488d-a3bf-256ccac973e0/content' }
)

function Get-PinnedModel([hashtable] $model, [string] $directory) {
    $file = Join-Path $directory $model.Name
    Invoke-WebRequest -Uri $model.Url -OutFile $file
    $actual = (Get-FileHash -Algorithm MD5 -LiteralPath $file).Hash.ToLowerInvariant()
    if ($actual -ne $model.Md5 -or (Get-Item -LiteralPath $file).Length -ne $model.Bytes) {
        throw "Refusing $($model.Name): expected $($model.Bytes) bytes / MD5 $($model.Md5), got $((Get-Item -LiteralPath $file).Length) / $actual."
    }
}

if (Test-Path -LiteralPath $destination) {
    $executable = Join-Path $destination 'bin\udpipe.exe'
    $present = @($models | Where-Object { Test-Path -LiteralPath (Join-Path $destination ('models\' + $_.Name)) })
    foreach ($model in $present) {
        $actual = (Get-FileHash -Algorithm MD5 -LiteralPath (Join-Path $destination ('models\' + $model.Name))).Hash.ToLowerInvariant()
        if ($actual -ne $model.Md5) {
            throw "Existing $($model.Name) has MD5 $actual, not pinned $($model.Md5); refusing to merge a replacement."
        }
    }
    if ($present.Count -gt 0 -and (Test-Path -LiteralPath $executable)) {
        # A verified installation that predates a model on the list gains that model alone; what is
        # already here and verified is not fetched again.
        $absent = @($models | Where-Object { $present -notcontains $_ })
        if ($absent.Count -eq 0) {
            Write-Host "UDPipe 1.4.0 and all $($models.Count) pinned models are already present and verified; nothing downloaded."
            return
        }
        try {
            New-Item -ItemType Directory -Force -Path $staging | Out-Null
            foreach ($model in $absent) {
                Get-PinnedModel $model $staging
            }
            Get-ChildItem -LiteralPath $staging -File |
                Move-Item -Destination (Join-Path $destination 'models') -Force
            $absent | ForEach-Object { Get-FileHash -Algorithm SHA256 -LiteralPath (Join-Path $destination ('models\' + $_.Name)) } |
                Select-Object Path, Hash | Format-Table -AutoSize
        }
        finally {
            if (Test-Path -LiteralPath $staging) {
                Remove-Item -LiteralPath $staging -Recurse -Force
            }
        }
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
        Get-PinnedModel $model $modelDirectory
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

    # The three original licence texts are kept, not paraphrased: CC BY-NC-SA 4.0 for the
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
