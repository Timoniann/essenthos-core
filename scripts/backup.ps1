<#
.SYNOPSIS
    Copies everything this project cannot rebuild onto a second drive.

.DESCRIPTION
    Three kinds of thing live here and only one of them is safe.

    Rebuildable: the database. Every row of it is derived from Resources/, the loaders and the
    migrations, so losing it costs hours of loading and nothing else. It is copied only with
    -Database, because 6.7 GB of dump is the difference between a backup somebody runs weekly and
    one they stop running.

    Held elsewhere: essenthos-web and essenthos-api are on GitHub. Their bundles are cheap and are
    taken anyway -- a remote is somebody else's promise, and this costs three megabytes.

    Held nowhere else, which is the reason this script exists:

      .avioniq        every finding, decision, rule, mistake and measurement this project has made.
                      5 MB, no git remote. The single most expensive thing here to lose and the
                      cheapest to copy, because none of it can be re-derived from anything.
      essenthos-core  the rebuild itself, with no git remote either.
      Resources/      1.2 GB of third-party corpus sources. Thirteen of nineteen folders have no
                      fetch script and exist because somebody downloaded them once; several are one
                      repository deletion from unrecoverable.

    Resources is mirrored rather than archived, so a second run copies only what changed and takes
    seconds. Everything else is a git bundle: one file, the whole history, and `git bundle verify`
    says whether it is intact -- which a directory copy of .git never does.

.PARAMETER Destination
    Where to write. Defaults to E:\Projects\Essenthos, mirroring the workspace.

.PARAMETER Database
    Also dump essenthos_core. Large, and the only part that is rebuildable without it.

.EXAMPLE
    avioniq services run backup
    avioniq services run backup -Database
#>
[CmdletBinding()]
param(
    [string]$Destination = 'E:\Projects\Essenthos',
    [switch]$Database
)

$ErrorActionPreference = 'Stop'
$workspace = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$startedAt = Get-Date

if (-not (Test-Path (Split-Path $Destination -Qualifier))) {
    throw "The drive $(Split-Path $Destination -Qualifier) is not there. Plug it in, or pass -Destination."
}

New-Item -ItemType Directory -Path $Destination -Force | Out-Null
Write-Host "Backing up to $Destination"
$receipt = [ordered]@{ startedAt = $startedAt.ToString('o'); source = $workspace; parts = @() }

function Save-Bundle([string]$repo, [string]$name) {
    $path = Join-Path $workspace $repo
    if (-not (Test-Path (Join-Path $path '.git'))) { return }

    $head = (git -C $path rev-parse HEAD).Trim()
    $dirty = (git -C $path status --porcelain) | Measure-Object -Line
    $file = Join-Path $Destination "$name.bundle"

    # --all rather than HEAD: every branch and tag, so a bundle is the repository and not one line
    # through it.
    git -C $path bundle create $file --all 2>&1 | Out-Null
    git bundle verify $file 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The bundle for $repo did not verify. Nothing was replaced." }

    $size = (Get-Item $file).Length
    $note = if ($dirty.Lines -gt 0) { "$($dirty.Lines) uncommitted, NOT in this bundle" } else { 'clean' }
    Write-Host ("  {0,-16} {1,8:N1} MB  {2}  {3}" -f $name, ($size / 1MB), $head.Substring(0, 8), $note)
    $script:receipt.parts += [ordered]@{
        kind = 'bundle'; name = $name; head = $head; uncommitted = $dirty.Lines; bytes = $size
    }
}

# The store first, because it is the part with no other copy anywhere.
Save-Bundle '.avioniq' 'avioniq'
Save-Bundle 'essenthos-core' 'essenthos-core'
Save-Bundle 'essenthos-web' 'essenthos-web'
Save-Bundle 'essenthos-api' 'essenthos-api'

# The store is mirrored whole as well as bundled, and that is not belt and braces.
#
# A bundle carries committed history and nothing else, and this store does not commit on every
# write: its last commit when this script was written was three days old, with 331 changed files
# sitting in the working tree. Every finding, decision and measurement of those three days would
# have been missed by a backup that reported success. The attachments and the run state are outside
# its git repository on purpose -- a screenshot in a history is a history every clone pays for
# forever -- so those were never in the bundle either.
#
# Five megabytes. There is no version of this worth being clever about.
$store = Join-Path $workspace '.avioniq'
$to = Join-Path $Destination 'avioniq'
robocopy $store $to /MIR /XD .git /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Mirroring the avioniq store failed with robocopy code $LASTEXITCODE." }
$stored = (Get-ChildItem $to -Recurse -File | Measure-Object -Property Length -Sum)
Write-Host ("  {0,-16} {1,8:N1} MB  {2} files, working tree and all" -f 'avioniq store', ($stored.Sum / 1MB), $stored.Count)
$receipt.parts += [ordered]@{ kind = 'mirror'; name = 'avioniq'; files = $stored.Count; bytes = $stored.Sum }

# Mirrored, not archived: a second run copies what changed. /MIR deletes what is no longer here,
# which is what makes it a copy of the corpus rather than an attic of every file that ever was.
$resources = Join-Path $workspace 'essenthos-core\Resources'
$to = Join-Path $Destination 'essenthos-core\Resources'
Write-Host '  Resources        mirroring...'
robocopy $resources $to /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "Mirroring Resources failed with robocopy code $LASTEXITCODE." }

$files = (Get-ChildItem $to -Recurse -File | Measure-Object -Property Length -Sum)
Write-Host ("  {0,-16} {1,8:N1} MB  {2} files" -f 'Resources', ($files.Sum / 1MB), $files.Count)
$receipt.parts += [ordered]@{ kind = 'mirror'; name = 'Resources'; files = $files.Count; bytes = $files.Sum }

# The manifest travels with the data and is what makes the copy checkable rather than merely present.
$manifest = Join-Path $resources 'MANIFEST.json'
if (Test-Path $manifest) {
    $recorded = (Get-Content $manifest -Raw | ConvertFrom-Json).PSObject.Properties.Name.Count
    Write-Host ("  {0,-16} {1} folders fingerprinted" -f 'manifest', $recorded)
}

if ($Database) {
    $dump = Join-Path $Destination ('essenthos_core-{0}.dump' -f $startedAt.ToString('yyyyMMdd'))
    Write-Host '  database         dumping...'
    # Custom format: compressed, and pg_restore can take one table out of it without the rest.
    docker exec essenthos-api-db-1 pg_dump -U essenthos -d essenthos_core -Fc | Set-Content -Path $dump -AsByteStream
    if ($LASTEXITCODE -ne 0) { throw 'pg_dump failed. The dump on the drive is incomplete; delete it.' }
    $size = (Get-Item $dump).Length
    Write-Host ("  {0,-16} {1,8:N1} MB" -f 'database', ($size / 1MB))
    $receipt.parts += [ordered]@{ kind = 'dump'; name = 'essenthos_core'; bytes = $size }
}

$receipt.finishedAt = (Get-Date).ToString('o')
$receipt.seconds = [math]::Round(((Get-Date) - $startedAt).TotalSeconds)
$receipt | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $Destination 'BACKUP.json')

Write-Host ''
Write-Host ("Done in {0}s. What is here and nowhere else: the avioniq store, essenthos-core, and the corpus sources." -f $receipt.seconds)
if (-not $Database) {
    Write-Host 'The database was not dumped. It is the one part that can be rebuilt; pass -Database to take it anyway.'
}
