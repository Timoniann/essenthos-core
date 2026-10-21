param(
    [ValidateSet('es')][string] $Language = 'es',
    [ValidateSet('prepare','smoke','pilot','repair','full','check-pilot','check-full','status','publish','judge-smoke','preflight','transport-smoke','finish-prepare','finish-recover','finish-repair','finish-check','finish-status','finish-publish')]
    [string] $Mode = 'status',
    [ValidateRange(1,8)][int] $Workers = 6
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function ResolveCodex {
    # One rule for every script: scripts/codex_cli.py (Codex moves its CLI on each update).
    $found = & python (Join-Path $PSScriptRoot 'codex_cli.py')
    if ($LASTEXITCODE -ne 0 -or -not $found) { throw 'Codex CLI not found; see scripts/codex_cli.py.' }
    return ($found | Select-Object -Last 1).Trim()
}

$repo = Split-Path $PSScriptRoot
$main = if ((Split-Path (Split-Path $repo) -Leaf) -eq '.worktrees') { Split-Path (Split-Path $repo) } else { $repo }
$run = Join-Path $main 'Resources/Essenthos/generation/strong-codex-sol61-2026-10-17-v3/es'
$finish = $Mode.StartsWith('finish-')
if ($finish) {
    $run = Join-Path $main 'Resources/Essenthos/generation/strong-es-finish-codex-2026-10-21-v1'
    if ($Mode -ne 'finish-repair') {
        & python -u (Join-Path $PSScriptRoot 'finish-strong-es-codex.py') --mode $Mode.Substring(7)
        exit $LASTEXITCODE
    }
}
$env:ESSENTHOS_CODEX = ResolveCodex
$version = & $env:ESSENTHOS_CODEX --version 2>&1
if ($LASTEXITCODE -ne 0) { throw 'The resolved Codex executable failed its version check.' }
$launchFolder = Join-Path $run 'runtime-launches'
[IO.Directory]::CreateDirectory($launchFolder) | Out-Null
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')
$receipt = @{mode=$Mode;language='es';codex=$env:ESSENTHOS_CODEX;version=($version -join "`n");launcher_sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;started_at=[DateTime]::UtcNow.ToString('o')}
$receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $launchFolder "$stamp.json") -Encoding utf8
Write-Output ("Codex runtime: " + ($version -join ' '))
if ($Mode -eq 'preflight') { exit 0 }

if ($Mode -eq 'transport-smoke') {
    $check = Join-Path $run "runtime-checks/$stamp"
    [IO.Directory]::CreateDirectory($check) | Out-Null
    foreach ($file in @('source.json','manifest.json')) {
        Copy-Item -LiteralPath (Join-Path $run "smoke/$file") -Destination (Join-Path $check $file)
    }
    Copy-Item -LiteralPath (Join-Path $run 'smoke/batches') -Destination (Join-Path $check 'batches') -Recurse
    & python -u (Join-Path $run 'code/lexicon-codex.py') ask --dir $check --model gpt-6.1-sol --effort medium --workers 1
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    & python -u (Join-Path $run 'code/lexicon-codex.py') check --dir $check
    exit $LASTEXITCODE
}
if ($finish) {
    & python -u (Join-Path $PSScriptRoot 'finish-strong-es-codex.py') --mode repair
} elseif ($Mode -eq 'judge-smoke') {
    & python -u (Join-Path $run 'code/lexicon-codex.py') judge --dir (Join-Path $run 'smoke') --model gpt-6.1-sol --effort medium --workers 1
} else {
    & python -u (Join-Path $PSScriptRoot 'generate-strong-codex.py') --language $Language --mode $Mode --workers $Workers
}
exit $LASTEXITCODE
