<#
.SYNOPSIS
    Copies the server's encrypted backups of readers' accounts to this machine, checked byte for byte.

.DESCRIPTION
    The backup service on the server writes one encrypted dump of each database a night into
    /srv/essenthos/backups, on the same volume as the database itself. This brings the ones this
    machine does not have yet into -Folder, so losing the server's volume loses nothing.

    Only files the server wrote encrypted (*.dump.gpg) are ever fetched, and only as the server wrote
    them: nothing is decrypted on the way, and a file that does not begin as an OpenPGP message is
    refused. Each file is fetched into <name>.partial and kept only once its size and SHA-256 equal
    the server's own; a run stopped halfway leaves the finished files and a .partial, and the next run
    fetches only what is still missing.

    What is kept is what the owner's console keeps (its "Keep" setting, in
    Resources/Essenthos/server-backups.json): the newest -Keep copies of each database, and none but
    the newest older than -KeepDays, which is how long the server keeps its own. The newest of each
    database stays whatever its age, so a server that stopped making backups never leaves this machine
    with none. The console copies into the same folder by the same rule, so neither undoes the other.

    Exit 0 when every wanted file is here and checked; 1 when the server could not be read or a file
    could not be fetched or checked, with the reason. A file already here and equal to the server's
    is never fetched again.

.PARAMETER Server
    The ssh address, signed in with the owner's key; never a password or a prompt.

.PARAMETER Remote
    The folder on the server the backup service writes into.

.PARAMETER Folder
    Where the copies are kept on this machine: the second drive by default, outside every repository.

.PARAMETER Keep
    How many copies of each database to keep, newest first. Defaults to the console's setting, else 7.

.PARAMETER KeepDays
    The longest any copy but the newest of each database is kept: the server's own window.

.PARAMETER Ssh
    The ssh program and any leading arguments. The rehearsal passes a stand-in that runs the command
    in a local shell against a folder.

.EXAMPLE
    pwsh -NoProfile -File scripts/pull-account-backups.ps1
    pwsh -NoProfile -File scripts/pull-account-backups.ps1 -Folder D:\backups\essenthos -Keep 14
#>
[CmdletBinding()]
param(
    [string] $Server = 'deploy@2.29.125.205',
    [string] $Remote = '/srv/essenthos/backups',
    [string] $Folder = 'E:\Projects\Essenthos\server-backups',
    [int] $Keep = 0,
    [int] $KeepDays = 14,
    [string[]] $Ssh = @('ssh')
)

$ErrorActionPreference = 'Stop'
$encryptedSuffix = '.dump.gpg'
$partialSuffix = '.partial'
$backupName = '^(?<database>[a-z0-9_]+)-(?<stamp>\d{8}T\d{4}Z)\.dump\.gpg$'
$defaultKeep = 7
$abandonedAfter = [TimeSpan]::FromHours(1)

if ($Remote -notmatch '^/[A-Za-z0-9._/-]+$') { throw "$Remote is not a folder this script can name on the server." }
if ($KeepDays -lt 1) { throw '-KeepDays must be at least 1.' }

if ($Keep -le 0) {
    $Keep = $defaultKeep
    $settings = Join-Path $PSScriptRoot '..\Resources\Essenthos\server-backups.json'
    if (Test-Path $settings) {
        try {
            $read = Get-Content $settings -Raw | ConvertFrom-Json
            if ($read.keep -is [long] -or $read.keep -is [int]) { $Keep = [int]$read.keep }
        }
        catch {
            Write-Warning "The console's backup settings at $settings could not be read; keeping $defaultKeep."
        }
    }
}
$Keep = [Math]::Clamp($Keep, 1, $KeepDays)

$drive = Split-Path $Folder -Qualifier -ErrorAction SilentlyContinue
if ($drive -and -not (Test-Path "$drive\")) { throw "The drive $drive is not there. Plug it in, or pass -Folder." }
New-Item -ItemType Directory -Path $Folder -Force | Out-Null
$Folder = (Resolve-Path $Folder).Path

# Runs ssh with the command, its output into a file where one is named (bytes, never text), and gives
# back the exit code with what it printed and said.
function Invoke-Ssh([string] $command, [string] $into) {
    $start = [System.Diagnostics.ProcessStartInfo]::new($Ssh[0])
    foreach ($argument in @($Ssh | Select-Object -Skip 1) + @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=15', $Server, $command)) {
        $start.ArgumentList.Add($argument)
    }
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.RedirectStandardInput = $true
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    try { $process = [System.Diagnostics.Process]::Start($start) }
    catch { return [pscustomobject]@{ Code = -1; Output = ''; Error = "$($Ssh[0]) could not be started: $($_.Exception.Message)" } }
    try {
        $process.StandardInput.Close()
        $said = $process.StandardError.ReadToEndAsync()
        $output = ''
        if ($into) {
            $file = [System.IO.File]::Create($into)
            try { $process.StandardOutput.BaseStream.CopyTo($file) } finally { $file.Dispose() }
        }
        else {
            $output = $process.StandardOutput.ReadToEnd()
        }
        $process.WaitForExit()
        [pscustomobject]@{ Code = $process.ExitCode; Output = $output; Error = $said.GetAwaiter().GetResult().Trim() }
    }
    finally { $process.Dispose() }
}

function Get-Sha256([string] $path) { (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant() }

# An OpenPGP message begins with a packet tag, whose high bit is set; a custom-format dump begins
# with PGDMP. A dump in the clear given the encrypted name is never kept as if it were one.
function Test-Encrypted([string] $path) {
    $stream = [System.IO.File]::OpenRead($path)
    try {
        $head = [byte[]]::new(5)
        $read = $stream.Read($head, 0, 5)
        $read -gt 0 -and ($head[0] -band 0x80) -ne 0 -and [System.Text.Encoding]::ASCII.GetString($head, 0, $read) -ne 'PGDMP'
    }
    finally { $stream.Dispose() }
}

function Get-Copies {
    foreach ($file in Get-ChildItem -LiteralPath $Folder -Filter "*$encryptedSuffix" -File) {
        if ($file.Name -match $backupName) {
            [pscustomobject]@{ Name = $file.Name; Database = $Matches.database; Stamp = $Matches.stamp }
        }
    }
}

function ConvertFrom-Stamp([string] $stamp) {
    [datetime]::ParseExact($stamp, "yyyyMMdd'T'HHmm'Z'", [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::AdjustToUniversal -bor [Globalization.DateTimeStyles]::AssumeUniversal)
}

# The copies to keep of each database: the newest, and after it the next newest up to -Keep that are
# inside the window.
function Select-Kept($backups, [datetime] $now) {
    $limit = $now.AddDays(-$KeepDays)
    foreach ($database in $backups | Group-Object Database) {
        $newestFirst = @($database.Group | Sort-Object Stamp -Descending)
        for ($i = 0; $i -lt $newestFirst.Count; $i++) {
            if ($i -eq 0 -or ($i -lt $Keep -and (ConvertFrom-Stamp $newestFirst[$i].Stamp) -ge $limit)) { $newestFirst[$i] }
        }
    }
}

# The same listing the console runs: each encrypted dump with its size and checksum, the plain ones by name.
$list = "cd '$Remote' 2>/dev/null || { echo `"There is no folder $Remote on the server.`" >&2; exit 3; }; " +
    'for f in *.dump.gpg; do [ -f "$f" ] || continue; ' +
    'printf ''encrypted\t%s\t%s\t%s\n'' "$f" "$(stat -c %s -- "$f")" "$(sha256sum -- "$f" | cut -d'' '' -f1)"; done; ' +
    'for f in *.dump; do [ -f "$f" ] && printf ''plain\t%s\n'' "$f"; done; exit 0'

$now = [datetime]::UtcNow
$listed = Invoke-Ssh $list $null
if ($listed.Code -ne 0) {
    Write-Error -ErrorAction Continue "The server's backups could not be listed (exit $($listed.Code)): $($listed.Error)"
    exit 1
}

$onServer = @()
$plain = 0
foreach ($line in $listed.Output -split "`n") {
    $fields = $line.Trim() -split "`t"
    if ($fields.Count -eq 2 -and $fields[0] -eq 'plain') { $plain++; continue }
    if ($fields.Count -ne 4 -or $fields[0] -ne 'encrypted' -or $fields[2] -notmatch '^\d+$' -or $fields[3] -notmatch '^[a-f0-9]{64}$') { continue }
    if ($fields[1] -match $backupName) {
        $onServer += [pscustomobject]@{
            Name = $fields[1]; Database = $Matches.database; Stamp = $Matches.stamp
            Bytes = [long]$fields[2]; Sha = $fields[3]
        }
    }
}
if ($plain -gt 0) { Write-Warning "The server holds $plain dump(s) in the clear; they are never copied." }
if ($onServer.Count -eq 0) {
    Write-Error -ErrorAction Continue "The server has no encrypted backups in $Remote."
    exit 1
}

$wanted = @(Select-Kept $onServer $now)

$copied = @()
$failed = @()
foreach ($backup in $wanted | Sort-Object Stamp, Database) {
    $target = Join-Path $Folder $backup.Name
    if ((Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target).Length -eq $backup.Bytes -and (Get-Sha256 $target) -eq $backup.Sha) {
        continue
    }

    $partial = $target + $partialSuffix
    $fetched = Invoke-Ssh "cat -- '$Remote/$($backup.Name)'" $partial
    $why = $null
    if ($fetched.Code -ne 0) { $why = "the copy broke off (exit $($fetched.Code)): $($fetched.Error)" }
    elseif ((Get-Item -LiteralPath $partial).Length -ne $backup.Bytes) { $why = "it arrived as $((Get-Item -LiteralPath $partial).Length) bytes, not the server's $($backup.Bytes)" }
    elseif ((Get-Sha256 $partial) -ne $backup.Sha) { $why = 'its SHA-256 differs from the server''s' }
    elseif (-not (Test-Encrypted $partial)) { $why = 'it does not begin as an encrypted file' }

    if ($why) {
        Remove-Item -LiteralPath $partial -Force -ErrorAction SilentlyContinue
        Write-Warning "$($backup.Name) was not kept: $why."
        $failed += $backup.Name
        continue
    }
    Move-Item -LiteralPath $partial -Destination $target -Force
    $copied += $backup.Name
    Write-Host ("  copied  {0}  {1:N0} bytes, SHA-256 checked" -f $backup.Name, $backup.Bytes)
}

# What is kept here now: the same rule over what the folder holds, so a copy the server has already
# let go of ages out here too.
$here = @(Get-Copies)
$kept = @(Select-Kept $here $now | ForEach-Object Name)
foreach ($old in $here | Where-Object { $kept -notcontains $_.Name }) {
    Remove-Item -LiteralPath (Join-Path $Folder $old.Name) -Force
    Write-Host "  removed $($old.Name), past what is kept"
}
Get-ChildItem -LiteralPath $Folder -Filter "*$partialSuffix" -File |
    Where-Object { $_.LastWriteTimeUtc -lt $now - $abandonedAfter } |
    Remove-Item -Force

$summary = Get-ChildItem -LiteralPath $Folder -Filter "*$encryptedSuffix" -File | Measure-Object -Property Length -Sum
Write-Host ("Server backups: {0} copied, {1} here ({2:N1} MB) in {3}; keeping {4} of each database, {5} days." -f
    $copied.Count, $summary.Count, ($summary.Sum / 1MB), $Folder, $Keep, $KeepDays)
if ($failed.Count -gt 0) {
    Write-Error -ErrorAction Continue "$($failed.Count) backup(s) could not be copied: $($failed -join ', '). The next run tries them again."
    exit 1
}
exit 0
