param(
    [ValidateSet('prepare','pilot','read','repair','stage','status')]
    [string] $Mode = 'status'
)
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

function ResolveCodex {
    if ($env:ESSENTHOS_CODEX) {
        $override = Get-Command $env:ESSENTHOS_CODEX -CommandType Application -ErrorAction SilentlyContinue
        if (-not $override) { throw 'Configured ESSENTHOS_CODEX does not resolve to an executable.' }
        return $override.Source
    }
    $onPath = Get-Command codex -CommandType Application -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $userPath = [Environment]::GetEnvironmentVariable('Path','User')
    $machinePath = [Environment]::GetEnvironmentVariable('Path','Machine')
    foreach ($directory in (($userPath + ';' + $machinePath) -split ';')) {
        if (-not $directory) { continue }
        $candidate = Join-Path ([Environment]::ExpandEnvironmentVariables($directory)) 'codex.exe'
        if (Test-Path -LiteralPath $candidate -PathType Leaf) { return $candidate }
    }
    if ($env:LOCALAPPDATA) {
        $standalone = Join-Path $env:LOCALAPPDATA 'Programs/OpenAI/Codex/bin/codex.exe'
        if (Test-Path -LiteralPath $standalone -PathType Leaf) { return $standalone }
        $desktopBin = Join-Path $env:LOCALAPPDATA 'OpenAI/Codex/bin'
        if (Test-Path -LiteralPath $desktopBin -PathType Container) {
            $candidates = @(Get-ChildItem -LiteralPath $desktopBin -Directory | ForEach-Object {
                $candidate = Join-Path $_.FullName 'codex.exe'
                if (Test-Path -LiteralPath $candidate -PathType Leaf) { Get-Item -LiteralPath $candidate }
            } | Sort-Object LastWriteTimeUtc -Descending)
            if ($candidates.Count) { return $candidates[0].FullName }
        }
    }
    throw 'Codex CLI was not found on PATH or in the installed user locations. Set ESSENTHOS_CODEX to a valid executable.'
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
