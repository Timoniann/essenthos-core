# Historical (TSK-1452): the run folders this script names under Resources/Essenthos/generation were archived on
# 2026-11-04 to E:\EssenthosArchive\generation\<run>, with a verified manifest beside it. The script is kept as the record
# of how the run was made and no longer runs as written; to run it again, point it at a copy of the run folder, never
# at the archive itself.
$ErrorActionPreference='Stop'
$mainRoot='C:\Users\timon\Projects\Essenthos\essenthos-core'
$runRoot=Join-Path $mainRoot 'Resources/Essenthos/generation/presence-closure-2026-10-26-v2'
function Sql([string]$query) {
    $answer=$query|& docker exec -i essenthos-core-db-1 sh -c 'psql -X -v ON_ERROR_STOP=1 -q -t -A -U "$POSTGRES_USER" -d essenthos_core'
    if($LASTEXITCODE -ne 0){throw 'Bounded presence query failed'}
    return ($answer -join "`n").Trim()
}
function Step([string]$name,[scriptblock]$action) {
    "$(Get-Date -Format o) START $name"|Add-Content "$runRoot/admission-progress.txt" -Encoding utf8
    & $action
    if($LASTEXITCODE -ne 0){throw "$name failed ($LASTEXITCODE)"}
    "$(Get-Date -Format o) OK $name"|Add-Content "$runRoot/admission-progress.txt" -Encoding utf8
}
$gates=Get-Content "$runRoot/admission-gates.json" -Raw -Encoding utf8|ConvertFrom-Json
if(!$gates.main_suite_passed -or !$gates.isolated_suite_passed -or !$gates.source_proof_passed -or !$gates.full_file_test_passed){throw 'Required admission checks are missing'}
$revision=& git -C $mainRoot rev-parse HEAD
if($LASTEXITCODE -ne 0 -or $revision.Trim() -ne $gates.head){throw 'Main revision changed after checks'}
$inputPath=Join-Path $mainRoot 'Resources/Essenthos/person-presence.json'
if((Get-FileHash $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $gates.input_sha256){throw 'Published input differs from tested input'}
$dataset=Get-Content $inputPath -Raw -Encoding utf8|ConvertFrom-Json
$geography="SET statement_timeout='30s'; SELECT (SELECT count(*)||'|'||sum(hashtextextended(l::text,0)) FROM place_location l)||';'||(SELECT count(*)||'|'||sum(hashtextextended(c::text,0)) FROM place_candidate c);"
$geographyBefore=Sql $geography
Sql "SET statement_timeout='30s'; SELECT jsonb_agg(to_jsonb(p)) FROM person_presence p;"|Set-Content "$runRoot/live-presence-before.json" -Encoding utf8
Set-Location -LiteralPath (Split-Path -Parent $mainRoot)
Step 'forge build' {avioniq services run core-forge-build}
Step 'API publish' {avioniq services run core-api-publish}
if((Get-FileHash $inputPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $gates.input_sha256){throw 'Input changed before writer'}
Step 'scoped person-presence apply' {avioniq services run core-forge --set verb=person-presence}
$fingerprint="SET statement_timeout='30s'; SELECT count(*)||'|'||sum(hashtextextended(p::text,0)) FROM person_presence p;"
$beforeReplay=Sql $fingerprint
Step 'scoped person-presence replay' {avioniq services run core-forge --set verb=person-presence}
$afterReplay=Sql $fingerprint
if($beforeReplay -ne $afterReplay){throw 'Presence replay changed rows'}
if([int]($afterReplay.Split('|')[0]) -ne $dataset.records.Count){throw 'Stored pair count differs from admitted input'}
$broken=Sql "SET statement_timeout='30s'; SELECT count(*) FROM person_presence p LEFT JOIN entity e ON e.id=p.person_id LEFT JOIN entity l ON l.id=p.place_id WHERE e.kind IS DISTINCT FROM 'person' OR l.kind NOT IN ('place','object') OR l.id IS NULL OR cardinality(p.addresses)=0 OR jsonb_typeof(p.evidence)<>'array' OR jsonb_array_length(p.evidence)=0 OR p.category NOT IN ('present','buried');"
if($broken -ne '0'){throw 'Targeted presence integrity failed'}
if((Sql $geography) -ne $geographyBefore){throw 'Geography changed during presence admission'}
Step 'restart core' {avioniq services restart core}
$ready=$false
for($attempt=0;$attempt -lt 30;$attempt++) {
    try {$null=Invoke-RestMethod 'http://localhost:5279/v1/books' -TimeoutSec 5;$ready=$true;break} catch {Start-Sleep -Seconds 1}
}
if(!$ready){throw 'Core did not become ready'}
$api=@()
foreach($person in @('huldah','moses','amaziah','jacob')) {
    $response=Invoke-RestMethod "http://localhost:5279/v1/entities/$person/places?language=ukr&verses=50" -TimeoutSec 30
    $expected=@($dataset.records|Where-Object person -eq $person)
    foreach($finding in $expected) {
        $group=if($finding.category -eq 'buried'){'buried'}else{'event'}
        if(!($response.items|Where-Object {$_.slug -eq $finding.place -and $group -in $_.roles})){throw "Missing served finding: $person/$($finding.place)/$group"}
    }
    $api+=@{person=$person;response=$response}
}
$api|ConvertTo-Json -Depth 12|Set-Content "$runRoot/admission-api.json" -Encoding utf8
Step 'backup' {avioniq services run backup}
@{status='completed';at=(Get-Date -Format o);pairs=$dataset.records.Count;presence_fingerprint=$afterReplay;
    geography_unchanged=$true;targeted_presence_violations=0;full_corpus_verification_run=$false;api_people=$api.Count}|
    ConvertTo-Json -Depth 5|Set-Content "$runRoot/admission-completion.json" -Encoding utf8
