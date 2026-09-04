<#
.SYNOPSIS
    Stops the rebuild's API, including the process `dotnet run` leaves behind.

.DESCRIPTION
    `dotnet run` is a launcher: it builds, then starts the application as a *child* process and
    waits. Stopping what avioniq started kills the launcher, and the child goes on holding port
    5279 — so `avioniq services list` says the service is stopped while something is still
    answering, and the next `start` fails to bind. What the next person sees is not "the port is
    taken": it is whatever that orphan happens to answer, which in the case this was written for
    was a 500 saying the database password was missing. A stale process from an earlier build,
    reporting a fault that had already been fixed.

    So this kills the listener on the port rather than a process avioniq has a handle on. That is
    the only thing that reliably finds an orphan, because an orphan is by definition the process
    nobody is holding.

    Port 5279 is this project's alone -- chosen to avoid the 3000 and 5173 that other work on this
    machine takes -- so killing what listens on it cannot reach anything else.

.PARAMETER Port
    The port to clear. Defaults to the rebuild's.
#>
[CmdletBinding()]
param([int]$Port = 5279)

$ErrorActionPreference = 'Stop'

$listeners = @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)
if ($listeners.Count -eq 0) {
    Write-Host "Nothing is listening on $Port."
    exit 0
}

foreach ($pid in ($listeners.OwningProcess | Sort-Object -Unique)) {
    $process = Get-Process -Id $pid -ErrorAction SilentlyContinue
    if (-not $process) { continue }

    Write-Host ("Stopping {0} (pid {1}, started {2})" -f $process.ProcessName, $process.Id, $process.StartTime)
    Stop-Process -Id $process.Id -Force -ErrorAction SilentlyContinue
}

# The socket is not free the instant the process dies, and the next start binds immediately.
for ($i = 0; $i -lt 20; $i++) {
    Start-Sleep -Milliseconds 250
    if (-not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue)) {
        Write-Host "Port $Port is free."
        exit 0
    }
}

throw "Port $Port is still held after stopping every listener on it."
