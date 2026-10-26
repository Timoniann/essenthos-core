param([ValidateSet('Preflight','Apply')][string]$Mode = 'Preflight')
$ErrorActionPreference = 'Stop'
$mainRoot = 'C:\Users\timon\Projects\Essenthos\essenthos-core'
$controlRoot = Split-Path -Parent $mainRoot
$runRoot = Join-Path $mainRoot 'Resources/Essenthos/generation/presence-core-integration-2026-10-26-v1'
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null

function Invoke-Sql([string]$sql) {
    $answer = $sql | & docker exec -i essenthos-core-db-1 sh -c 'psql -X -v ON_ERROR_STOP=1 -q -t -A -U "$POSTGRES_USER" -d essenthos_core'
    if ($LASTEXITCODE -ne 0) { throw "Corpus SQL failed ($LASTEXITCODE)." }
    return ($answer -join "`n").Trim()
}

function Step([string]$name, [scriptblock]$action) {
    $line = "$(Get-Date -Format o) START $name"
    $line | Add-Content -LiteralPath (Join-Path $runRoot 'progress.txt') -Encoding utf8
    Write-Output $line
    & $action
    if ($LASTEXITCODE -ne 0) { throw "$name failed with exit $LASTEXITCODE" }
    "$(Get-Date -Format o) OK $name" | Add-Content -LiteralPath (Join-Path $runRoot 'progress.txt') -Encoding utf8
}

$datasetPath = Join-Path $mainRoot 'Resources/Essenthos/person-presence.json'
if ($Mode -eq 'Preflight' -and !(Test-Path -LiteralPath $datasetPath)) {
    $datasetPath = Join-Path $mainRoot '.worktrees/presence-core/Resources/Essenthos/person-presence.json'
}
$dataset = Get-Content -LiteralPath $datasetPath -Raw -Encoding utf8 | ConvertFrom-Json
$identities = @($dataset.records | ForEach-Object {
    @{slug=$_.person; kind='person'}; @{slug=$_.place; kind='place'}
}) | ConvertTo-Json -Compress
$identities = $identities.Replace("'", "''")
$sql = @"
SET statement_timeout = '30s';
WITH wanted AS (SELECT DISTINCT slug,kind FROM jsonb_to_recordset('$identities'::jsonb) AS x(slug text,kind text)),
checked AS (SELECT w.*,coalesce(e.id,k.id) id,coalesce(e.kind,k.kind) actual_kind FROM wanted w
LEFT JOIN entity e ON e.slug=w.slug LEFT JOIN merged_record m ON m.slug=w.slug LEFT JOIN entity k ON k.id=m.entity_id)
SELECT jsonb_build_object('last_migration',(SELECT migration_id FROM "__EFMigrationsHistory" ORDER BY migration_id DESC LIMIT 1),
'unresolved',coalesce((SELECT jsonb_agg(jsonb_build_object('slug',slug,'expected',kind,'actual',actual_kind)) FROM checked WHERE id IS NULL OR actual_kind<>kind),'[]'::jsonb));
"@
$preflight = Invoke-Sql $sql | ConvertFrom-Json
$preflight | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'preflight.json') -Encoding utf8
if (@($preflight.unresolved).Count -ne 0) { throw 'Presence identities are missing or have changed kind; inspect preflight.json.' }
Write-Output ($preflight | ConvertTo-Json -Depth 5 -Compress)
if ($Mode -eq 'Preflight') { exit 0 }

$gates = Get-Content -LiteralPath (Join-Path $runRoot 'gates.json') -Raw -Encoding utf8 | ConvertFrom-Json
if (!$gates.isolated_suite_passed -or !$gates.main_suite_passed -or !$gates.full_dataset_test_passed) { throw 'Required test gates have not passed.' }
$hash = (Get-FileHash -LiteralPath $datasetPath -Algorithm SHA256).Hash.ToLowerInvariant()
if ($hash -ne $gates.dataset_sha256) { throw 'Admitted dataset changed after its proof.' }
$schemaPath = Join-Path $runRoot 'schema.sql'
if ((Get-FileHash -LiteralPath $schemaPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $gates.schema_sha256) { throw 'Migration SQL changed after review.' }
Set-Location -LiteralPath $controlRoot
Step 'forge build' { avioniq services run core-forge-build }
Step 'API publish' { avioniq services run core-api-publish }
if ($preflight.last_migration -eq '20261025175918_EveryProposedSiteOfAPlaceIsHeld') {
    Step 'schema migration' { Invoke-Sql (Get-Content -LiteralPath $schemaPath -Raw -Encoding utf8) | Write-Output }
} elseif ($preflight.last_migration -ne '20261026155108_PeopleHaveCitedPresence') { throw 'Unexpected migration head; refusing unrelated schema changes.' }
if ((Get-FileHash -LiteralPath $datasetPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $gates.dataset_sha256) { throw 'Dataset changed before the writer.' }
Step 'load presence and integrity' { avioniq services run core-load --set only=person-presence }
$before = Invoke-Sql "SET statement_timeout='30s'; SELECT count(*)||'|'||coalesce(sum(hashtextextended(p::text,0)),0) FROM person_presence p;"
Step 'repeat new load step' { avioniq services run core-load --set only=person-presence }
$after = Invoke-Sql "SET statement_timeout='30s'; SELECT count(*)||'|'||coalesce(sum(hashtextextended(p::text,0)),0) FROM person_presence p;"
if ($before -ne $after) { throw 'Presence replay changed the loaded rows.' }
Step 'restart core' { avioniq services restart core }
$ready = $false
for ($attempt = 0; $attempt -lt 30; $attempt++) {
    try { $null = Invoke-RestMethod 'http://localhost:5279/v1/books' -TimeoutSec 5; $ready=$true; break } catch { Start-Sleep -Seconds 1 }
}
if (!$ready) { throw 'Core did not become ready.' }
$abner = Invoke-RestMethod 'http://localhost:5279/v1/entities/abner/places?language=ukr' -TimeoutSec 30
if (!($abner.items | Where-Object slug -eq 'bithron')) { throw 'Serving API did not return the supported Bithron visit.' }
$abner | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $runRoot 'abner-api.json') -Encoding utf8
$counts = Invoke-Sql @"
SET statement_timeout='30s';
SELECT jsonb_build_object('pairs',count(*),'persons',count(DISTINCT person_id),
'living',count(*) FILTER(WHERE category='present'),'burial',count(*) FILTER(WHERE category='buried'),
'wrong_kind',(SELECT count(*) FROM person_presence p JOIN entity a ON a.id=p.person_id JOIN entity b ON b.id=p.place_id WHERE a.kind<>'person' OR b.kind<>'place')) FROM person_presence;
"@
$counts | Set-Content -LiteralPath (Join-Path $runRoot 'loaded-counts.json') -Encoding utf8
Write-Output $counts
Step 'backup' { avioniq services run backup }
@{status='completed';at=(Get-Date -Format o);dataset_sha256=$hash;replay_fingerprint=$after;api_bithron=$true;counts=($counts|ConvertFrom-Json)} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $runRoot 'completion.json') -Encoding utf8
