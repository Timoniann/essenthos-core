<#
.SYNOPSIS
    Starts the rebuild's API from its published copy, taking a newer one first if it is waiting.

.DESCRIPTION
    `dotnet run` checks the whole build before every start, and compiles whatever changed while
    the API is down: about 3 seconds when nothing did and 6 to 19 after a merge, on top of the
    2 to 3 the API takes to start. Published once, the API starts in those 2 to 3.

    The core-api-publish action publishes into .run/api-next while the running API keeps serving
    from .run/api. Starting takes .run/api-next in place of .run/api first -- the files of a running
    program cannot be replaced, which is why it happens here, between a stop and a start.

    The content root stays the project folder, so appsettings.json, the site settings and the
    pictures are found exactly where `dotnet run` finds them.

.PARAMETER Port
    The port to listen on.

.PARAMETER Bind
    The address to listen on: every interface for the owner's other devices, or loopback.
#>
[CmdletBinding()]
param(
    [int]$Port = 5279,
    [string]$Bind = '0.0.0.0'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$run = Join-Path $root '.run/api'
$next = Join-Path $root '.run/api-next'

if (Test-Path $next) {
    for ($attempt = 1; Test-Path $run; $attempt++) {
        try {
            Remove-Item -Recurse -Force $run
        }
        catch {
            if ($attempt -ge 20) { throw "The previous copy at $run is still in use: $_" }
            Start-Sleep -Milliseconds 250
        }
    }

    Move-Item $next $run
}

$program = Join-Path $run 'Essenthos.Api.dll'
if (-not (Test-Path $program)) {
    throw "There is no published API at $run. Run the core-api-publish action first."
}

& dotnet $program --contentRoot (Join-Path $root 'Essenthos.Api') --environment Development --urls "http://${Bind}:$Port"
exit $LASTEXITCODE
