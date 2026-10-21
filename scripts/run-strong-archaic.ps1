param(
    [ValidateSet('prepare','pilot','read','repair','stage','status')]
    [string] $Mode = 'status'
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
$run = Join-Path $main 'Resources/Essenthos/generation/strong-archaic-repair-2026-10-21-v1'
if ($Mode -in @('pilot','read','repair')) {
    $env:ESSENTHOS_CODEX = ResolveCodex
    $version = & $env:ESSENTHOS_CODEX --version 2>&1
    if ($LASTEXITCODE -ne 0) { throw 'The resolved Codex executable failed its version check.' }
    $launchFolder = Join-Path $run 'runtime-launches'
    [IO.Directory]::CreateDirectory($launchFolder) | Out-Null
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffffffZ')
    $receipt = @{mode=$Mode;codex=$env:ESSENTHOS_CODEX;version=($version -join "`n");launcher_sha256=(Get-FileHash -LiteralPath $PSCommandPath).Hash;started_at=[DateTime]::UtcNow.ToString('o')}
    $receipt | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $launchFolder "$stamp.json") -Encoding utf8
    Write-Output ("Codex runtime: " + ($version -join ' '))
}
& python -u (Join-Path $PSScriptRoot 'repair-strong-archaic.py') --mode $Mode
exit $LASTEXITCODE
