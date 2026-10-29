#Requires -Version 7.4
<#
.SYNOPSIS
Builds the API and web images on this machine from two commits and sends them to the server.

.DESCRIPTION
Nothing is built on the server and nothing is pulled from a registry: the images are built here, from
-Commit (essenthos-core) and -WebCommit (essenthos-web), and loaded into the server's Docker over ssh.
`scripts/deploy.ps1` then starts them by the same tags.

Each image is built from the commit as committed, never from a working tree: the commit is written out
with `git archive` into a temporary folder and built from there, so an edit not yet committed cannot
reach an image that names the commit. The web build also gets this checkout's `scripts/.cache`, which
holds the map's downloaded terrain and coast; without it the build downloads them again (~170 MB). The
tags are `essenthos-api:<commit>` and `essenthos-web:<commit>`, full hashes, built for -Platform.

Before building, each commit's checks run on a temporary worktree at that commit: `core-check-isolated`
and `web-check-isolated`, the same checks run while working. They are the only gate a release has.
-SkipChecks leaves them out, for a commit whose checks were just run.

A tag the server already has is refused, since a commit's image should not change once it is there;
-Force builds and sends it again, and -OnlyMissing leaves it as it is and sends the other, which is how
`deploy.ps1 -SendImages` and the owner's console call this.

-WhatIf says what it would check, build and send, and touches nothing: no ssh, no build.

.EXAMPLE
./scripts/send-images.ps1 -Server deploy@2.29.125.205 -Commit 1a2b3c4... -WebCommit 9f8e7d...
./scripts/send-images.ps1 -Server deploy@2.29.125.205 -Commit 1a2b3c4... -SkipChecks
./scripts/send-images.ps1 -Server deploy@2.29.125.205 -WebCommit 9f8e7d... -AnalyticsEndpoint /count -WhatIf
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $Server,
    # The essenthos-core commit whose API image to build.
    [string] $Commit,
    # The essenthos-web commit whose image to build.
    [string] $WebCommit,
    # The essenthos-web checkout; by default the one beside this repository's main checkout.
    [string] $WebRepository,
    # Where the reader's page counter listens: "/count" once counting is on (deploy/README.md, "Page
    # counts"), empty for a bundle that counts nothing.
    [string] $AnalyticsEndpoint = '',
    [string] $Platform = 'linux/amd64',
    [switch] $SkipChecks,
    [switch] $Force,
    # Leave a tag the server already has as it is, instead of refusing it.
    [switch] $OnlyMissing
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $true

if (-not $Commit -and -not $WebCommit) {
    throw 'Name at least one of -Commit (essenthos-core) and -WebCommit (essenthos-web).'
}

foreach ($hash in @($Commit, $WebCommit) | Where-Object { $_ }) {
    if ($hash -notmatch '^[0-9a-f]{40}$') {
        # A short hash or a branch name is a tag that can come to mean something else.
        throw "'$hash' is not a full 40-character commit hash: ``git rev-parse HEAD`` in the repository."
    }
}

# The main checkout, not a worktree this may run from: the temporary worktrees, the web repository and
# the map cache are found beside it.
function Get-MainCheckout([string] $Repository) {
    Split-Path -Parent (git -C $Repository rev-parse --path-format=absolute --git-common-dir)
}

$core = Get-MainCheckout (Join-Path $PSScriptRoot '..')
if (-not $WebRepository) {
    $WebRepository = Join-Path (Split-Path -Parent $core) 'essenthos-web'
}

$ssh = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15')

$images = @()
if ($Commit) {
    $images += [pscustomobject]@{
        Name = 'essenthos-api'; Commit = $Commit; Repository = $core
        Dockerfile = 'Essenthos.Api/Dockerfile'; Check = 'core-check-isolated'; BuildArgs = @()
    }
}
if ($WebCommit) {
    $images += [pscustomobject]@{
        Name = 'essenthos-web'; Commit = $WebCommit; Repository = (Get-MainCheckout $WebRepository)
        Dockerfile = 'Dockerfile'; Check = 'web-check-isolated'
        BuildArgs = @('--build-arg', "VITE_ANALYTICS_ENDPOINT=$AnalyticsEndpoint")
    }
}

foreach ($image in $images) {
    try {
        git -C $image.Repository cat-file -e "$($image.Commit)^{commit}" 2>$null
    }
    catch {
        throw "Commit $($image.Commit) is not in $($image.Repository). Run ``git fetch`` there and try again."
    }
    $image | Add-Member Tag "$($image.Name):$($image.Commit)"
}

$plan = $images | ForEach-Object { $_.Tag }
if (-not $PSCmdlet.ShouldProcess($Server, "Build and send $($plan -join ' and ')")) {
    foreach ($image in $images) {
        Write-Host "$($image.Tag):"
        if (-not $SkipChecks) {
            Write-Host "  check   avioniq services run $($image.Check) on a temporary worktree at the commit"
        }
        Write-Host "  build   docker build --platform $Platform -f $($image.Dockerfile) --build-arg SOURCE_COMMIT=$($image.Commit) $($image.BuildArgs -join ' ') <the commit, archived>"
        Write-Host "  send    docker save $($image.Tag) | ssh $Server docker load"
        if ($OnlyMissing) {
            Write-Host "  (none of it if the server already has $($image.Tag))"
        }
    }
    Write-Host 'Nothing was checked, built or sent.'
    return
}

$probe = (($images | ForEach-Object { "docker image inspect $($_.Tag) >/dev/null 2>&1 && echo $($_.Tag);" }) -join ' ') + ' true'
$present = @(ssh @ssh $Server $probe | Where-Object { $_ })
if ($present -and $OnlyMissing -and -not $Force) {
    Write-Host "The server already has $($present -join ' and '); left as it is."
    $images = @($images | Where-Object { $present -notcontains $_.Tag })
    if (-not $images) {
        return
    }
}
elseif ($present -and -not $Force) {
    throw "The server already has $($present -join ' and '). Deploy it with scripts/deploy.ps1, or send it again with -Force."
}

function Invoke-Check($image) {
    $label = "ship-$($image.Commit.Substring(0, 12))"
    $worktree = Join-Path $image.Repository ".worktrees/$label"
    git -C $image.Repository worktree add --detach --quiet $worktree $image.Commit
    $junction = $null
    try {
        if ($image.Check -eq 'web-check-isolated') {
            # The main checkout's packages serve a commit with the same lockfile; another one installs its own.
            $lock = Join-Path $image.Repository 'package-lock.json'
            if ((Test-Path (Join-Path $image.Repository 'node_modules')) -and
                (Get-FileHash $lock).Hash -eq (Get-FileHash (Join-Path $worktree 'package-lock.json')).Hash) {
                $junction = Join-Path $worktree 'node_modules'
                New-Item -ItemType Junction -Path $junction -Target (Join-Path $image.Repository 'node_modules') | Out-Null
            }
            else {
                npm ci --prefix $worktree --no-audit --no-fund
            }
            avioniq services run $image.Check --set "root=.worktrees/$label"
        }
        else {
            # The corpus classes are left out of the check, but anything that reads Resources reads the
            # main checkout's: a worktree has none of the corpus.
            $env:Dataset__ResourcesPath = Join-Path $image.Repository 'Resources'
            avioniq services run $image.Check --set "root=.worktrees/$label" --set "output=$label"
        }
    }
    finally {
        Remove-Item Env:Dataset__ResourcesPath -ErrorAction SilentlyContinue
        if ($junction) {
            # Only the link, never what it points at.
            [System.IO.Directory]::Delete($junction, $false)
        }
        git -C $image.Repository worktree remove --force $worktree
    }
}

function Build-Image($image) {
    $context = Join-Path ([System.IO.Path]::GetTempPath()) "essenthos-image-$($image.Commit)"
    Remove-Item -Recurse -Force $context -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Path $context | Out-Null
    try {
        $archive = "$context.tar"
        git -C $image.Repository archive --format=tar -o $archive $image.Commit
        tar -xf $archive -C $context
        Remove-Item $archive
        $cache = Join-Path $image.Repository 'scripts/.cache'
        if ($image.Name -eq 'essenthos-web' -and (Test-Path $cache)) {
            Copy-Item -Recurse $cache (Join-Path $context 'scripts/.cache')
        }
        docker build --platform $Platform -f (Join-Path $context $image.Dockerfile) `
            --build-arg "SOURCE_COMMIT=$($image.Commit)" @($image.BuildArgs) -t $image.Tag $context
    }
    finally {
        Remove-Item -Recurse -Force $context -ErrorAction SilentlyContinue
    }
}

foreach ($image in $images) {
    if (-not $SkipChecks) {
        Write-Host "Checking $($image.Tag) with $($image.Check)"
        Invoke-Check $image
    }
}

foreach ($image in $images) {
    Write-Host "Building $($image.Tag) for $Platform"
    $clock = [System.Diagnostics.Stopwatch]::StartNew()
    Build-Image $image
    # The platform's manifest, which is what docker save sends and the server's tag names; the local
    # tag names an index around it.
    $id = docker image inspect --platform $Platform --format '{{.Id}}' $image.Tag
    Write-Host ("Built {0} in {1:n0} s" -f $image.Tag, $clock.Elapsed.TotalSeconds)

    # No compression: what docker save writes is the image's layers as stored, already gzip-compressed,
    # and gzip over it saved 0.6% (measured 2026-10-29). docker load would read a compressed stream too.
    Write-Host "Sending $($image.Tag) to $Server"
    $clock.Restart()
    docker save --platform $Platform $image.Tag | ssh @ssh $Server docker load
    $remote = ssh @ssh $Server "docker image inspect --format '{{.Id}}' $($image.Tag)"
    if ($remote -ne $id) {
        throw "The server's $($image.Tag) is $remote, not $id as built here. Send it again with -Force."
    }
    Write-Host ("Sent {0} in {1:n0} s" -f $image.Tag, $clock.Elapsed.TotalSeconds)
}

Write-Host "On ${Server}: $($plan -join ', '). Start them with scripts/deploy.ps1."
