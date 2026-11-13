<#
.SYNOPSIS
    Restores an encrypted backup of readers' accounts, whole, into a database on this machine, and
    proves every row arrived.

.DESCRIPTION
    The file is decrypted here by gpg, which asks the owner for the key's passphrase itself; this
    script never sees, stores or logs the passphrase or the key. The plain dump is streamed straight
    into Postgres inside a local container and never written to a file.

    Everything comes back as it was: every table and row, the sequences (the revision counter
    included) and the data-protection keys, which is what keeps readers' sessions and sign-in state
    valid. Ownership and grants are left out, because the server's roles are not this machine's.

    It is restored into <Into>_restoring first. Only once the rows of every table equal the dump's
    own count, every table the dump lists is there, and every sequence stands where the dump left it,
    is it renamed to -Into; a database already called that is replaced only with -Replace, and only
    then. A failed or short restore drops the candidate and leaves whatever was there before as it was.

    Refused: the corpus databases (essenthos_core and its seed, next and test copies, and prod's
    corpus), postgres, the templates, and a Docker that is not this machine's.

.PARAMETER File
    The encrypted dump. Defaults to the newest copy of -Database in -Folder.

.PARAMETER Database
    Which database's newest copy to restore when -File is not given.

.PARAMETER Folder
    Where pull-account-backups.ps1 keeps the copies.

.PARAMETER Into
    The database to restore into, made new.

.PARAMETER Container
    The local Postgres container that holds it: core's test database by default, or the rehearsal
    stack's essenthos-rehearsal-db-1 to sign in to a rehearsal site with the restored accounts.

.PARAMETER Replace
    Replace -Into if it exists, once the new copy has proved whole.

.PARAMETER Gpg
    The gpg that holds the owner's key: Gpg4win's by default, not Git's.

.PARAMETER GnuPGHome
    A keyring other than the owner's: only the rehearsal's throwaway key.

.EXAMPLE
    pwsh -NoProfile -File scripts/restore-account-backup.ps1
    pwsh -NoProfile -File scripts/restore-account-backup.ps1 -File E:\Projects\Essenthos\server-backups\essenthos_app-20261113T0300Z.dump.gpg -Into essenthos_app_restored -Replace
    pwsh -NoProfile -File scripts/restore-account-backup.ps1 -Container essenthos-rehearsal-db-1 -Into essenthos_app -Replace
#>
[CmdletBinding()]
param(
    [string] $File,
    [string] $Database = 'essenthos_app',
    [string] $Folder = 'E:\Projects\Essenthos\server-backups',
    [string] $Into = 'essenthos_app_restored',
    [string] $Container = 'essenthos-core-test-db-1',
    [switch] $Replace,
    [string] $Gpg,
    [string] $GnuPGHome
)

$ErrorActionPreference = 'Stop'
$candidate = "${Into}_restoring"
$keysTable = 'public.data_protection_key'

if ($Into -notmatch '^[a-z][a-z0-9_]{0,49}$') { throw "-Into must be a plain lower-case database name of at most 50 characters." }
if ($Into -match '^(essenthos_core.*|corpus.*|postgres|template.*)$') { throw "$Into is not a database this script restores into." }
if ($env:DOCKER_HOST -and $env:DOCKER_HOST -notmatch '^(npipe|unix):') { throw "DOCKER_HOST points at $env:DOCKER_HOST; restores go only into this machine's Docker." }

if (-not $File) {
    if ($Database -notmatch '^[a-z0-9_]+$') { throw "-Database must be a plain database name." }
    $newest = Get-ChildItem -LiteralPath $Folder -Filter "$Database-*.dump.gpg" -File -ErrorAction SilentlyContinue |
        Where-Object Name -match "^$Database-\d{8}T\d{4}Z\.dump\.gpg$" | Sort-Object Name -Descending | Select-Object -First 1
    if (-not $newest) { throw "There is no copy of $Database in $Folder. Run scripts/pull-account-backups.ps1 first, or pass -File." }
    $File = $newest.FullName
}
$File = (Resolve-Path -LiteralPath $File).Path

if (-not $Gpg) {
    $installed = Join-Path ${env:ProgramFiles} 'GnuPG\bin\gpg.exe'
    $Gpg = if (Test-Path $installed) { $installed } else { (Get-Command gpg -ErrorAction Stop).Source }
}

function Invoke-Container([string[]] $arguments, [string] $sql) {
    $output = if ($null -ne $sql) { $sql | docker exec -i $Container @arguments 2>&1 } else { docker exec $Container @arguments 2>&1 }
    [pscustomobject]@{ Code = $LASTEXITCODE; Output = ($output | Out-String).Trim() }
}

function Invoke-Psql([string] $on, [string] $sql) {
    $run = Invoke-Container @('sh', '-c', "psql -X -q -At -v ON_ERROR_STOP=1 -U `"`$POSTGRES_USER`" -d $on") $sql
    if ($run.Code -ne 0) { throw "psql on $on in $Container failed: $($run.Output)" }
    $run.Output
}

function Remove-Candidate { Invoke-Psql 'postgres' "DROP DATABASE IF EXISTS $candidate WITH (FORCE);" | Out-Null }

$alive = Invoke-Container @('true') $null
if ($alive.Code -ne 0) { throw "The container $Container is not running here: $($alive.Output)" }
$exists = (Invoke-Psql 'postgres' "SELECT count(*) FROM pg_database WHERE datname = '$Into';") -eq '1'
if ($exists -and -not $Replace) { throw "$Into already exists in $Container. Pass -Replace to replace it once the restore has proved whole, or choose another -Into." }

Write-Host "Restoring $(Split-Path $File -Leaf) into $Into in $Container."
Write-Host 'gpg asks for the passphrase of the backup key itself.'
Remove-Candidate
Invoke-Psql 'postgres' "CREATE DATABASE $candidate TEMPLATE template0;" | Out-Null

# Inside the container, the plain dump goes once through pg_restore into SQL, through awk, which
# passes every line on to psql and counts on the way what the dump itself holds: each TABLE DATA entry
# of its table of contents, the rows of each COPY block and each sequence's setval. One transaction:
# a dump that breaks off restores nothing.
$restore = @'
set -euo pipefail
counts=$(mktemp)
trap 'rm -f "$counts"' EXIT
pg_restore --no-owner --no-privileges -f - |
  awk -v out="$counts" '
    /^-- Data for Name: .*; Type: TABLE DATA; Schema: / {
      name = $0; sub(/^-- Data for Name: /, "", name); sub(/; Type: TABLE DATA.*$/, "", name)
      schema = $0; sub(/^.*; Schema: /, "", schema); sub(/; Owner: .*$/, "", schema)
      toc[++entries] = schema "." name
    }
    copying && $0 == "\\." { copying = 0; print; next }
    copying { rows[table]++; print; next }
    /^COPY [^ ]+ \(.*\) FROM stdin;$/ { table = $2; copying = 1; rows[table] = 0; order[++tables] = table; print; next }
    /^SELECT pg_catalog\.setval\(/ {
      line = $0; sub(/^SELECT pg_catalog\.setval\(\047/, "", line)
      sequence = line; sub(/\047, .*$/, "", sequence)
      rest = line; sub(/^[^,]*, /, "", rest); value = rest; sub(/,.*$/, "", value)
      called = rest; sub(/^[^,]*, /, "", called); sub(/\);$/, "", called)
      sequences[++settings] = sequence "\t" value "\t" called
    }
    { print }
    END {
      for (i = 1; i <= entries; i++) print "toc\t" toc[i] > out
      for (i = 1; i <= tables; i++) print "rows\t" order[i] "\t" rows[order[i]] > out
      for (i = 1; i <= settings; i++) print "sequence\t" sequences[i] > out
    }' |
  psql -X -q -v ON_ERROR_STOP=1 --single-transaction -U "$POSTGRES_USER" -d "$1" >/dev/null
cat "$counts"
'@
# A checkout with Windows line endings would hand bash a carriage return on every line.
$restore = $restore -replace "`r", ''

$gpgStart = [System.Diagnostics.ProcessStartInfo]::new($Gpg)
$gpgArguments = @('--batch', '--quiet', '--decrypt')
if ($GnuPGHome) { $gpgArguments = @('--homedir', $GnuPGHome) + $gpgArguments }
foreach ($argument in $gpgArguments + @($File)) { $gpgStart.ArgumentList.Add($argument) }
$gpgStart.RedirectStandardOutput = $true
$gpgStart.UseShellExecute = $false

$restoreStart = [System.Diagnostics.ProcessStartInfo]::new('docker')
foreach ($argument in @('exec', '-i', $Container, 'bash', '-c', $restore, 'restore', $candidate)) { $restoreStart.ArgumentList.Add($argument) }
$restoreStart.RedirectStandardInput = $true
$restoreStart.RedirectStandardOutput = $true
$restoreStart.RedirectStandardError = $true
$restoreStart.UseShellExecute = $false

$restoring = [System.Diagnostics.Process]::Start($restoreStart)
$decrypting = $null
try {
    $counted = $restoring.StandardOutput.ReadToEndAsync()
    $said = $restoring.StandardError.ReadToEndAsync()
    try {
        $decrypting = [System.Diagnostics.Process]::Start($gpgStart)
        $decrypting.StandardOutput.BaseStream.CopyTo($restoring.StandardInput.BaseStream)
    }
    catch [System.IO.IOException] {
        # The restore stopped reading, which its own exit and message below say why.
    }
    finally {
        try { $restoring.StandardInput.Close() } catch [System.IO.IOException] { }
    }
    if ($decrypting) { $decrypting.WaitForExit() }
    $restoring.WaitForExit()
    $decrypted = if ($decrypting) { $decrypting.ExitCode } else { -1 }
    $restored = $restoring.ExitCode
    $counts = $counted.GetAwaiter().GetResult()
    $restoreSaid = $said.GetAwaiter().GetResult().Trim()
}
finally {
    if ($decrypting) { $decrypting.Dispose() }
    $restoring.Dispose()
}

if ($decrypted -ne 0 -or $restored -ne 0) {
    Remove-Candidate
    $why = if ($decrypted -ne 0) { "gpg could not decrypt it (exit $decrypted)" } else { "the restore failed (exit $restored): $restoreSaid" }
    Write-Error -ErrorAction Continue "Nothing was restored: $why. $(if ($exists) { "$Into is as it was." } else { '' })"
    exit 1
}

$toc = @()
$expected = [ordered]@{}
$sequences = @()
foreach ($line in $counts -split "`n") {
    $fields = $line.TrimEnd("`r") -split "`t"
    switch ($fields[0]) {
        'toc' { $toc += $fields[1] }
        'rows' { $expected[$fields[1]] = [long]$fields[2] }
        'sequence' { $sequences += [pscustomobject]@{ Name = $fields[1]; Value = [long]$fields[2]; Called = $fields[3] -eq 'true' } }
    }
}

$problems = @()
$plainName = { param($qualified) $qualified -replace '"', '' }
foreach ($entry in $toc) {
    if (-not ($expected.Keys | Where-Object { (& $plainName $_) -eq $entry })) { $problems += "the dump lists $entry but holds no rows block for it" }
}
if ($expected.Count -ne $toc.Count) { $problems += "the dump lists $($toc.Count) tables and holds $($expected.Count) rows blocks" }

$identifier = '^[a-z_][a-z0-9_]*\.("[A-Za-z0-9_]+"|[a-z_][a-z0-9_]*)$'
$query = foreach ($table in $expected.Keys) {
    if ($table -notmatch $identifier) { throw "The dump names a table this script will not query: $table" }
    "SELECT '$($table -replace "'", "''")' || chr(9) || count(*) FROM $table;"
}
$query += "SELECT 'tables' || chr(9) || count(*) FROM pg_tables WHERE schemaname NOT IN ('pg_catalog', 'information_schema');"
foreach ($sequence in $sequences) {
    if ($sequence.Name -notmatch $identifier) { throw "The dump names a sequence this script will not query: $($sequence.Name)" }
    $query += "SELECT 'sequence' || chr(9) || '$($sequence.Name -replace "'", "''")' || chr(9) || last_value || chr(9) || is_called FROM $($sequence.Name);"
}
$found = @{}
$foundSequences = @{}
$tablesThere = 0
foreach ($line in (Invoke-Psql $candidate ($query -join "`n")) -split "`n") {
    $fields = $line.Trim() -split "`t"
    if ($fields[0] -eq 'tables') { $tablesThere = [long]$fields[1] }
    elseif ($fields[0] -eq 'sequence') { $foundSequences[$fields[1]] = "$($fields[2]) $($fields[3])" }
    elseif ($fields.Count -eq 2) { $found[$fields[0]] = [long]$fields[1] }
}

Write-Host ''
Write-Host ('  {0,-40} {1,10} {2,10}' -f 'table', 'in dump', 'restored')
foreach ($table in $expected.Keys) {
    $mark = if ($found[$table] -eq $expected[$table]) { '' } else { '  <-- differs' }
    Write-Host ('  {0,-40} {1,10:N0} {2,10:N0}{3}' -f $table, $expected[$table], $found[$table], $mark)
    if ($found[$table] -ne $expected[$table]) { $problems += "$table has $($found[$table]) rows, the dump $($expected[$table])" }
}
if ($tablesThere -ne $expected.Count) { $problems += "the database has $tablesThere tables, the dump $($expected.Count)" }
foreach ($sequence in $sequences) {
    $want = "$($sequence.Value) $(if ($sequence.Called) { 'true' } else { 'false' })"
    if ($foundSequences[$sequence.Name] -ne $want) { $problems += "sequence $($sequence.Name) stands at $($foundSequences[$sequence.Name]), the dump at $want" }
}
$keys = if ($expected.Contains($keysTable)) { $found[$keysTable] } else { $null }
if ($null -eq $keys) { $problems += "the dump has no $keysTable, so sessions and sign-in state could not be read back" }

if ($problems.Count -gt 0) {
    Remove-Candidate
    Write-Error -ErrorAction Continue ("The restore did not prove whole, so it was dropped$(if ($exists) { " and $Into is as it was" }):`n  " + ($problems -join "`n  "))
    exit 1
}

if ($exists) { Invoke-Psql 'postgres' "DROP DATABASE $Into WITH (FORCE);" | Out-Null }
Invoke-Psql 'postgres' "ALTER DATABASE $candidate RENAME TO $Into;" | Out-Null

$rows = ($expected.Values | Measure-Object -Sum).Sum
Write-Host ''
Write-Host ("Restored {0} into {1} in {2}: {3} tables, {4:N0} rows, {5} sequences, {6} data-protection key(s), all equal to the dump." -f
    (Split-Path $File -Leaf), $Into, $Container, $expected.Count, $rows, $sequences.Count, $keys)
if ($keys -eq 0) { Write-Warning "The dump holds no data-protection keys: sessions signed in on the server will not be readable here." }
exit 0
