<#
.SYNOPSIS
Puts a build of the code on the server: the deploy/ files, and the image tags an environment runs.

.DESCRIPTION
The code and the corpus reach the server separately and on their own schedules. This moves the code:
the compose file, the Caddyfile and the scripts beside them as they stand in -Commit, and the API and
web image tags the environment runs. `forge publish` moves the corpus.

The deploy/ files are read out of the commit, never off the disk: an edit not yet committed, or a
checkout at another commit, would otherwise reach the server beside images built from -Commit.

Nothing is built on the server and nothing is pulled for our images: `scripts/send-images.ps1` builds
them on the workstation, after the local checks, and loads them into the server's Docker as
`essenthos-api:<commit>` and `essenthos-web:<commit>`. This refuses a tag the server does not have and
names that script. Images sent earlier stay on the server, so a rollback is running this again with
the previous hashes. -Registry names a registry to pull from instead; nothing uses one today.

Dev runs only on a machine whose .env turns it on (COMPOSE_PROFILES=dev, DEV_SITES=on). The production
server has no dev, and -Environment dev is refused there.

-WhatIf says what it would copy and run on the server, and touches nothing: no ssh, no scp.

.EXAMPLE
./scripts/deploy.ps1 -Server deploy@2.29.125.205 -Environment prod -Commit 1a2b3c4... -WebCommit 9f8e7d...
./scripts/deploy.ps1 -Server deploy@2.29.125.205 -Environment prod -Commit 1a2b3c4... -WhatIf
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
    # Empty: the images are the ones send-images.ps1 loaded on the server, by their local tags.
    [string] $Registry = ''
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
$shipped = @('deploy/compose.yaml', 'deploy/Caddyfile', 'deploy/backup.sh', 'deploy/backup-offsite.sh', 'deploy/initdb')
$files = @(git -C $repository ls-tree -r --name-only --full-tree $Commit -- @shipped |
    ForEach-Object { $_.Substring('deploy/'.Length) })

$prefix = if ($Registry) { "$Registry/" } else { '' }
$api = "${prefix}essenthos-api:$Commit"
$web = if ($WebCommit) { "${prefix}essenthos-web:$WebCommit" }
$suffix = if ($Environment -eq 'dev') { '_DEV' } else { '' }
$edits = @("s|^API${suffix}_IMAGE=.*|API${suffix}_IMAGE=$api|")
if ($web) {
    $edits += "s|^WEB${suffix}_IMAGE=.*|WEB${suffix}_IMAGE=$web|"
}

# Checked before anything is copied or edited, so a deploy that cannot run leaves the server as it was.
$preflight = @(
    "set -eu"
    "cd '$Root/deploy' 2>/dev/null || { echo 'No $Root/deploy on the server: see deploy/README.md, Once: the machine.' >&2; exit 1; }"
    "test -f .env || { echo 'No .env in $Root/deploy: copy env.example and fill it in first.' >&2; exit 1; }"
)
if ($Environment -eq 'dev') {
    $preflight += "grep -Eq '^COMPOSE_PROFILES=(.*,)?dev(,|`$)' .env || { echo 'No dev on this machine: its .env does not turn the dev profile on (deploy/README.md).' >&2; exit 1; }"
}
if (-not $Registry) {
    foreach ($image in @($api, $web) | Where-Object { $_ }) {
        $preflight += "docker image inspect '$image' >/dev/null 2>&1 || { echo 'The server has no $image. Build and send it first: scripts/send-images.ps1 -Server $Server' >&2; exit 1; }"
    }
}
$preflight = $preflight -join "`n"

# The dev tags are commented out in env.example; a line has to exist before sed can replace it.
$remote = @"
set -eu
cd '$Root/deploy'
grep -q '^API${suffix}_IMAGE=' .env || echo 'API${suffix}_IMAGE=' >> .env
grep -q '^WEB${suffix}_IMAGE=' .env || echo 'WEB${suffix}_IMAGE=' >> .env
$(($edits | ForEach-Object { "sed -i '$_' .env" }) -join "`n")
$(if ($Registry) { 'docker compose pull --quiet' })
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
    Write-Host "Would check on ${Server}:"
    $preflight -split "`n" | ForEach-Object { Write-Host "  $_" }
    Write-Host "Would run on ${Server}:"
    $remote -split "`n" | ForEach-Object { Write-Host "  $_" }
    Write-Host 'Nothing was copied and nothing ran on the server.'
    return
}

ssh @ssh $Server $preflight

# An archive keeps the files' bytes exactly as committed, which piping them through the shell would not.
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "essenthos-deploy-$Commit"
Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $staging | Out-Null
try {
    $archive = Join-Path $staging 'deploy.tar'
    git -C $repository archive --format=tar -o $archive $Commit -- @shipped
    tar -xf $archive -C $staging

    Write-Host "Copying $($files.Count) files of $Commit to ${Server}:$Root/deploy"
    # The folders the deploy user writes into later (the updating page's switch, what `forge publish`
    # sends) are made here, as that user: a folder Docker makes for a mount is root's, and refuses it.
    ssh @ssh $Server "mkdir -p '$Root/deploy/initdb' '$Root/maintenance' '$Root/releases' '$Root/downloads' '$Root/images/corpus'"
    foreach ($file in $files) {
        scp -q @ssh (Join-Path $staging 'deploy' $file) "${Server}:$Root/deploy/$file"
    }
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}

ssh @ssh $Server $remote
Write-Host "Deployed $Commit to $Environment. Check /v1/health/ready on it."
