<#
.SYNOPSIS
    How long the rebuild's API takes to restart, split into building it and starting it.

.DESCRIPTION
    Builds the API of one checkout into its own output, then starts it on a private port three
    ways -- as `dotnet run` (what the core service runs), as `dotnet run --no-build`, and as the
    built program itself -- and times each from launch to the port listening, to /v1/health/live
    answering and to the first and second answer of /v1/books. A build after one source file of the
    API and one of the corpus library changed is timed too, which is what a merge costs.

    Every process it starts it stops, with the processes that one started; nothing else is touched.
    It refuses a port something already listens on. The API reads the corpus as it always does, so
    each start costs the database one warm-up count (about a second).

.PARAMETER Root
    The checkout, relative to essenthos-core or absolute.

.PARAMETER Output
    The build output label (bin/<Output>/), so the running service's output is never touched.

.PARAMETER Port
    A private port: 5288 or 5289.

.PARAMETER Runs
    How many starts of each kind.
#>
[CmdletBinding()]
param(
    [string]$Root = '.',
    [Parameter(Mandatory)][string]$Output,
    [ValidateSet(5288, 5289)][int]$Port = 5289,
    [int]$Runs = 2
)

$ErrorActionPreference = 'Stop'
$api = Join-Path (Resolve-Path $Root) 'Essenthos.Api'
$property = "-p:BaseOutputPath=bin/$Output/"
$runProperty = "--property:BaseOutputPath=bin/$Output/"
$url = "http://127.0.0.1:$Port"

if (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) {
    throw "Something already listens on $Port; this measures only an instance it starts itself."
}

function Measure-Build([string]$what) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & dotnet build $api $property --nologo -v q -clp:NoSummary | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "The build failed ($what)." }
    [pscustomobject]@{ Step = "build: $what"; Seconds = [math]::Round($watch.Elapsed.TotalSeconds, 2) }
}

function Stop-Tree([int]$id) {
    foreach ($child in @(Get-CimInstance Win32_Process -Filter "ParentProcessId = $id" -ErrorAction SilentlyContinue)) {
        Stop-Tree $child.ProcessId
    }

    Stop-Process -Id $id -Force -ErrorAction SilentlyContinue
}

function Wait-For([scriptblock]$ready, [double]$limit = 180) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    while ($watch.Elapsed.TotalSeconds -lt $limit) {
        if (& $ready) { return $true }
        Start-Sleep -Milliseconds 50
    }

    return $false
}

function Answers([string]$path) {
    try {
        (Invoke-WebRequest -Uri "$url$path" -UseBasicParsing -TimeoutSec 60).StatusCode -eq 200
    }
    catch {
        $false
    }
}

function Measure-Start([string]$what, [string]$file, [string[]]$arguments, [string]$directory = $api) {
    $log = Join-Path ([IO.Path]::GetTempPath()) "measure-api-start-$Port.log"
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $env:Logging__Console__FormatterOptions__TimestampFormat = 'HH:mm:ss.fff '
    $launched = (Get-Date).ToString('HH:mm:ss.fff')
    $watch = [Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath $file -ArgumentList $arguments -WorkingDirectory $directory `
        -RedirectStandardOutput $log -RedirectStandardError "$log.err" -PassThru -NoNewWindow
    try {
        $listening = Wait-For { $null -ne (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) }
        $toListen = $watch.Elapsed.TotalSeconds
        $live = Wait-For { Answers '/v1/health/live' } 30
        $toLive = $watch.Elapsed.TotalSeconds
        $first = [Diagnostics.Stopwatch]::StartNew()
        $books = (Answers '/v1/books') -and (Answers '/v1/settings')
        $firstBooks = $first.Elapsed.TotalSeconds
        $second = [Diagnostics.Stopwatch]::StartNew()
        Answers '/v1/books' | Out-Null
        $secondBooks = $second.Elapsed.TotalSeconds
        if (-not ($listening -and $live -and $books)) {
            throw "The API did not come up ($what); its output is in $log."
        }

        [pscustomobject]@{
            Step = "start: $what"
            Seconds = [math]::Round($toListen, 2)
            Live = [math]::Round($toLive, 2)
            FirstBooks = [math]::Round($firstBooks, 2)
            SecondBooks = [math]::Round($secondBooks, 3)
            Launched = $launched
            Log = ((Get-Content $log) -match '^\d\d:|migrations|Now listening|weights' | ForEach-Object { $_.Trim() }) -join ' | '
        }
    }
    finally {
        Stop-Tree $process.Id
        if (-not (Wait-For { -not (Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue) } 30)) {
            throw "Port $Port is still held after stopping the instance this started."
        }
    }
}

function Measure-Dotnet([string]$what, [string[]]$arguments) {
    $watch = [Diagnostics.Stopwatch]::StartNew()
    & dotnet @arguments | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "$what failed." }
    [pscustomobject]@{ Step = $what; Seconds = [math]::Round($watch.Elapsed.TotalSeconds, 2) }
}

$dll = Join-Path $api "bin/$Output/Debug/net10.0/Essenthos.Api.dll"
$published = Join-Path ([IO.Path]::GetTempPath()) "measure-api-start-$Output"
$results = @()
$results += Measure-Build 'first'
$results += Measure-Build 'nothing changed'
$results += Measure-Dotnet 'publish: nothing changed' @('publish', $api, $property, '-o', $published, '--nologo', '-v', 'q')
for ($run = 1; $run -le $Runs; $run++) {
    $results += Measure-Start "dotnet run, nothing changed ($run)" 'dotnet' @(
        'run', '--project', $api, '--launch-profile', 'http', $runProperty, '--', '--urls', $url)
    $results += Measure-Start "dotnet run --no-build ($run)" 'dotnet' @(
        'run', '--no-build', '--project', $api, '--launch-profile', 'http', $runProperty, '--', '--urls', $url)
    $results += Measure-Start "built program ($run)" 'dotnet' @($dll, '--urls', $url)
    $results += Measure-Start "published copy, content root named ($run)" 'dotnet' @(
        (Join-Path $published 'Essenthos.Api.dll'), '--contentRoot', $api, '--environment', 'Development', '--urls', $url) (Split-Path $api)
}

$checkout = Split-Path $api
$results += Measure-Dotnet 'publish into .run/api-next' @('publish', $api, $property, '-o', (Join-Path $checkout '.run/api-next'), '--nologo', '-v', 'q')
for ($run = 1; $run -le $Runs; $run++) {
    $swapping = Test-Path (Join-Path $checkout '.run/api-next')
    $results += Measure-Start "start script, $(if ($swapping) { 'taking the newer copy' } else { 'nothing waiting' }) ($run)" 'pwsh' @(
        '-NoProfile', '-File', (Join-Path $checkout 'scripts/start-core.ps1'), '-Port', $Port, '-Bind', '127.0.0.1') $checkout
}

$forge = Join-Path (Split-Path $api) 'Essenthos.Forge'
$forgeDll = Join-Path $forge "bin/$Output/Release/net10.0/Essenthos.Forge.dll"
$results += Measure-Dotnet 'forge build -c Release' @('build', $forge, '-c', 'Release', $property, '--nologo', '-v', 'q', '-clp:NoSummary')
for ($run = 1; $run -le $Runs; $run++) {
    $results += Measure-Dotnet "forge help as dotnet run -c Release, nothing changed ($run)" @(
        'run', '--project', $forge, '-c', 'Release', $runProperty, '--', 'help')
    $results += Measure-Dotnet "forge help as the built program ($run)" @($forgeDll, 'help')
}

$results += Measure-Dotnet 'forge publish -c Release into .run/forge' @(
    'publish', $forge, '-c', 'Release', $property, '-o', (Join-Path $checkout '.run/forge'), '--nologo', '-v', 'q')
Push-Location $checkout
try {
    $results += Measure-Dotnet 'forge help as the published copy, from the checkout' @('.run/forge/Essenthos.Forge.dll', 'help')
}
finally {
    Pop-Location
}

$changed = Join-Path $api 'Program.cs'
(Get-Item $changed).LastWriteTime = Get-Date
$results += Measure-Build 'one API file changed'
$library = Join-Path (Split-Path $api) 'Essenthos.Corpus/Corpus/LinkedOriginals.cs'
(Get-Item $library).LastWriteTime = Get-Date
$results += Measure-Build 'one corpus-library file changed'
$results += Measure-Dotnet 'forge build -c Release, one corpus-library file changed' @(
    'build', $forge, '-c', 'Release', $property, '--nologo', '-v', 'q', '-clp:NoSummary')

Remove-Item -Recurse -Force $published -ErrorAction SilentlyContinue
$results | Format-List
