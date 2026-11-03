# Historical (TSK-1452): the run folders this script names under Resources/Essenthos/generation were archived on
# 2026-11-04 to E:\EssenthosArchive\generation\<run>, with a verified manifest beside it. The script is kept as the record
# of how the run was made and no longer runs as written; to run it again, point it at a copy of the run folder, never
# at the archive itself.
$ErrorActionPreference='Stop'
$mainRoot='C:\Users\timon\Projects\Essenthos\essenthos-core'
$runRoot=Join-Path $mainRoot 'Resources/Essenthos/generation/presence-locations-2026-10-26-v1'
function Sql([string]$query) {
    $answer=$query | & docker exec -i essenthos-core-db-1 sh -c 'psql -X -v ON_ERROR_STOP=1 -q -t -A -U "$POSTGRES_USER" -d essenthos_core'
    if($LASTEXITCODE -ne 0){throw 'Bounded corpus query failed.'}
    return ($answer -join "`n").Trim()
}
function Step([string]$name,[scriptblock]$action) {
    "$(Get-Date -Format o) START $name"|Add-Content -LiteralPath "$runRoot/progress.txt" -Encoding utf8
    & $action
    if($LASTEXITCODE -ne 0){throw "$name failed ($LASTEXITCODE)"}
    "$(Get-Date -Format o) OK $name"|Add-Content -LiteralPath "$runRoot/progress.txt" -Encoding utf8
}
$gates=Get-Content -LiteralPath "$runRoot/gates.json" -Raw -Encoding utf8|ConvertFrom-Json
$inputPath=Join-Path $mainRoot 'Essenthos.Forge/Loading/Encyclopedia/LocationSupplements.json'
if(!$gates.main_suite_passed -or !$gates.source_tests_passed -or !$gates.isolated_suite_passed){throw 'Required gates missing.'}
if((Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $gates.input_sha256){throw 'Location input changed after proof.'}
$presenceBefore=Sql "SET statement_timeout='30s'; SELECT count(*)||'|'||sum(hashtextextended(p::text,0)) FROM person_presence p;"
Set-Location -LiteralPath (Split-Path -Parent $mainRoot)
Step 'forge build' {avioniq services run core-forge-build}
Step 'API publish' {avioniq services run core-api-publish}
if((Get-FileHash -LiteralPath $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $gates.input_sha256){throw 'Location input changed before writer.'}
Step 'locations load and integrity' {avioniq services run core-load --set only=locations}
$fingerprintQuery="SET statement_timeout='30s'; SELECT (SELECT count(*)||'|'||sum(hashtextextended(l::text,0)) FROM place_location l)||';'||(SELECT count(*)||'|'||sum(hashtextextended(c::text,0)) FROM place_candidate c);"
$beforeReplay=Sql $fingerprintQuery
Step 'repeat locations load' {avioniq services run core-load --set only=locations}
$afterReplay=Sql $fingerprintQuery
if($beforeReplay -ne $afterReplay){throw 'Location replay changed rows.'}
$presenceAfter=Sql "SET statement_timeout='30s'; SELECT count(*)||'|'||sum(hashtextextended(p::text,0)) FROM person_presence p;"
if($presenceBefore -ne $presenceAfter){throw 'Presence findings unexpectedly changed.'}
Step 'restart core' {avioniq services restart core}
$ready=$false
for($attempt=0;$attempt -lt 30;$attempt++) {
    try {$null=Invoke-RestMethod 'http://localhost:5279/v1/books' -TimeoutSec 5;$ready=$true;break} catch {Start-Sleep -Seconds 1}
}
if(!$ready){throw 'Core not ready.'}
$nebo=Invoke-RestMethod 'http://localhost:5279/v1/entities/mountnebo?language=ukr' -TimeoutSec 30
if(!$nebo.location -or [Math]::Abs($nebo.location.lat-(31+45/60.0+44/3600.0)) -gt 1e-8 -or $nebo.locations[0].site -ne 'Jabal al Naba'){throw 'Serving Nebo location mismatch.'}
$nebo|ConvertTo-Json -Depth 10|Set-Content -LiteralPath "$runRoot/nebo-api.json" -Encoding utf8
$health=Invoke-RestMethod 'http://localhost:5279/v1/health' -TimeoutSec 30
if($health.verified.broken -ne 0){throw 'Integrity failed.'}
$health|Select-Object status,verified|ConvertTo-Json -Depth 4|Set-Content -LiteralPath "$runRoot/health.json" -Encoding utf8
Step 'backup' {avioniq services run backup}
@{status='completed';at=(Get-Date -Format o);presence_unchanged=$true;presence_fingerprint=$presenceAfter;replay_fingerprint=$afterReplay;nebo_location=$nebo.location;broken=$health.verified.broken}|
    ConvertTo-Json -Depth 6|Set-Content -LiteralPath "$runRoot/completion.json" -Encoding utf8
