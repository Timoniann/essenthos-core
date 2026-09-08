<#
.SYNOPSIS
    Starts the rebuild's API, after making sure nothing stale is holding its port.

.DESCRIPTION
    `dotnet run` is a launcher: it builds, then starts the application as a *child* and waits.
    Anything that kills the launcher without killing the child leaves an orphan holding port 5279 —
    a crash, a closed terminal, a machine that went to sleep, a stop that did not go through
    stop-core.ps1. avioniq then says the service is stopped while something is still answering, the
    next start cannot bind, and what the owner sees is not "the port is taken": it is whatever that
    orphan happens to answer.

    That is not hypothetical and it is not rare. The orphan is an *older build*, so it reports
    faults that have since been fixed — the one this project has seen twice is a 500 saying the
    database password is missing, from a build that predates the password being set. A person then
    spends an hour on a bug that no longer exists in the code they are reading.

    So starting clears the port first, the same way stopping does, and for the same reason: an
    orphan is by definition the process nobody is holding a handle to, and the only thing that
    reliably finds it is the port it is listening on. Port 5279 is this project's alone, so killing
    what listens on it cannot reach anything else.

    Starting is therefore idempotent: run it twice and the second run replaces the first rather than
    failing to bind behind it.

.PARAMETER Port
    The port to clear before starting. Defaults to the rebuild's.
#>
[CmdletBinding()]
param([int]$Port = 5279)

$ErrorActionPreference = 'Stop'

$stop = Join-Path $PSScriptRoot 'stop-core.ps1'
& $stop -Port $Port | Write-Host

dotnet run --project Essenthos.Core --launch-profile http
