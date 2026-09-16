[CmdletBinding()]
param(
    [ValidateSet('ukrainian', 'russian', 'english')]
    [string] $Language = 'ukrainian'
)

$ErrorActionPreference = 'Stop'
$root = Join-Path $PSScriptRoot '..\Resources\UDPipe'
$model = if ($Language -eq 'ukrainian') {
    Join-Path $root 'models\ukrainian-iu-ud-2.5-191206.udpipe'
} elseif ($Language -eq 'russian') {
    Join-Path $root 'models\russian-syntagrus-ud-2.5-191206.udpipe'
} else {
    Join-Path $root 'models\english-ud-2.1-20180111.udpipe'
}
$executable = Get-ChildItem -Path (Join-Path $root 'bin') -Filter 'udpipe.exe' -Recurse | Select-Object -First 1
if ($null -eq $executable -or -not (Test-Path -LiteralPath $model)) {
    throw 'UDPipe resources are absent. Run the fetch-udpipe action first.'
}

$sentence = if ($Language -eq 'ukrainian') {
    'Ісус же рече їм: Через невірство ваше.'
} else {
    if ($Language -eq 'english') {
        'Jesus said unto them, Because of your unbelief.'
    } else {
    'Иисус же сказал им: по неверию вашему.'
    }
}
Write-Host "UDPipe 1.4.0 smoke run ($Language); model: $(Split-Path -Leaf $model)"
$sentence | & $executable.FullName --tokenize --tag --parse --input horizontal --output conllu $model
if ($LASTEXITCODE -ne 0) {
    throw "UDPipe exited with $LASTEXITCODE."
}
