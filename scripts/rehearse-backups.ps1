[CmdletBinding()]
param([string] $Output = 'backup-rehearsal')

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true
if ($Output -notmatch '^[a-zA-Z0-9-]+$') { throw 'Output must be a simple directory label.' }
$checkout = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$previousEnabled = $env:ESSENTHOS_BACKUP_REHEARSAL
$previousRoot = $env:ESSENTHOS_REHEARSAL_ROOT
try {
    $env:ESSENTHOS_BACKUP_REHEARSAL = '1'
    $env:ESSENTHOS_REHEARSAL_ROOT = $checkout
    dotnet test (Join-Path $checkout 'Essenthos.Core.Tests') --nologo "-p:BaseOutputPath=bin/$Output/" --filter FullyQualifiedName~EncryptedBackupTests --logger 'trx;LogFileName=encrypted-backup.trx' --results-directory (Join-Path $checkout "TestResults/$Output")
    if ($LASTEXITCODE -ne 0) { throw "Backup rehearsal failed with native exit $LASTEXITCODE." }
}
finally {
    $env:ESSENTHOS_BACKUP_REHEARSAL = $previousEnabled
    $env:ESSENTHOS_REHEARSAL_ROOT = $previousRoot
}
