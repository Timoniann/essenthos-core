<#
.SYNOPSIS
Puts a build of the code on the server: the deploy/ files, and the image tags an environment runs.

.DESCRIPTION
The code and the corpus reach the server separately and on their own schedules. This moves the code:
the compose file, the Caddyfile and the scripts beside them as they stand in -Commit, and the API and
web image tags CI pushed for a commit. `forge publish` moves the corpus.

The deploy/ files are read out of the commit, never off the disk: an edit not yet committed, or a
checkout at another commit, would otherwise reach the server beside images built from -Commit.

Nothing is built on the server. The tags are full commit hashes, which CI pushes only from main and
only once the tests pass, so a rollback is running this again with the previous hash.

-WhatIf says what it would copy and run on the server, and touches nothing: no ssh, no scp.

.EXAMPLE
./scripts/deploy.ps1 -Server deploy@203.0.113.10 -Environment dev -Commit 1a2b3c4...
./scripts/deploy.ps1 -Server deploy@203.0.113.10 -Environment prod -Commit 1a2b3c4... -WebCommit 9f8e7d...
./scripts/deploy.ps1 -Server deploy@203.0.113.10 -Environment prod -Commit 1a2b3c4... -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $Server,
    [Parameter(Mandatory)] [ValidateSet('dev', 'prod')] [string] $Environment,
    # The essenthos-core commit whose API image to run.
    [Parameter(Mandatory)] [string] $Commit,
    # The essenthos-web commit whose image to run; the two repositories are released independently.
    [string] $WebCommit,
    [string] $Root = '/srv/essenthos',
    [string] $Registry = 'ghcr.io/timoniann'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if ($Commit -notmatch '^[0-9a-f]{40}$' -or ($WebCommit -and $WebCommit -notmatch '^[0-9a-f]{40}$')) {
    # A short hash or a branch name is a tag that can come to mean something else, which is the one
    # property a rollback cannot have.
    throw 'Commits are full 40-character hashes: `git rev-parse HEAD` in each repository.'
}

$repository = Join-Path $PSScriptRoot '..'
$ssh = @('-o', 'BatchMode=yes')

try {
    git -C $repository cat-file -e "$Commit^{commit}" 2>$null
}
catch {
    throw "Commit $Commit is not in this checkout. Run ``git fetch`` in essenthos-core and try again."
}

# Only what the commit tracks goes: .env and the rehearsal's files stay where they are.
$shipped = @('deploy/compose.yaml', 'deploy/Caddyfile', 'deploy/backup.sh', 'deploy/initdb')
$files = @(git -C $repository ls-tree -r --name-only --full-tree $Commit -- @shipped |
    ForEach-Object { $_.Substring('deploy/'.Length) })

$suffix = if ($Environment -eq 'dev') { '_DEV' } else { '' }
$edits = @("s|^API${suffix}_IMAGE=.*|API${suffix}_IMAGE=$Registry/essenthos-api:$Commit|")
if ($WebCommit) {
    $edits += "s|^WEB${suffix}_IMAGE=.*|WEB${suffix}_IMAGE=$Registry/essenthos-web:$WebCommit|"
}

# The dev tags are commented out in env.example; a line has to exist before sed can replace it.
$remote = @"
set -eu
cd '$Root/deploy'
test -f .env || { echo 'No .env in $Root/deploy: copy env.example and fill it in first.' >&2; exit 1; }
grep -q '^API${suffix}_IMAGE=' .env || echo 'API${suffix}_IMAGE=' >> .env
grep -q '^WEB${suffix}_IMAGE=' .env || echo 'WEB${suffix}_IMAGE=' >> .env
$(($edits | ForEach-Object { "sed -i '$_' .env" }) -join "`n")
docker compose pull --quiet
docker compose up -d --remove-orphans
# A bind-mounted Caddyfile that changed does not recreate the container, and a running Caddy keeps
# the configuration it started with: without this, a deploy that changed the edge has shipped nothing.
docker compose exec -T proxy caddy reload --config /etc/caddy/Caddyfile
docker compose ps --format '{{.Service}}\t{{.Image}}\t{{.Status}}'
"@

$remote = $remote -replace "`r", ''

if (-not $PSCmdlet.ShouldProcess("$Environment on $Server", "Deploy API $Commit$(if ($WebCommit) { " and web $WebCommit" })")) {
    Write-Host "Would copy $($files.Count) files to ${Server}:$Root/deploy:"
    $files | ForEach-Object { Write-Host "  $_" }
    Write-Host "Would run on ${Server}:"
    $remote -split "`n" | ForEach-Object { Write-Host "  $_" }
    Write-Host 'Nothing was copied and nothing ran on the server.'
    return
}

# An archive keeps the files' bytes exactly as committed, which piping them through the shell would not.
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "essenthos-deploy-$Commit"
Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $archive = Join-Path $staging 'deploy.tar'
    git -C $repository archive --format=tar -o $archive $Commit -- @shipped
    tar -xf $archive -C $staging

    Write-Host "Copying $($files.Count) files of $Commit to ${Server}:$Root/deploy"
    ssh @ssh $Server "mkdir -p '$Root/deploy/initdb'"
    foreach ($file in $files) {
        scp -q @ssh (Join-Path $staging 'deploy' $file) "${Server}:$Root/deploy/$file"
    }
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}

ssh @ssh $Server $remote
Write-Host "Deployed $Commit to $Environment. Check /v1/health/ready on it."
