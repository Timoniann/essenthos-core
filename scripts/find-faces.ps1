<#
.SYNOPSIS
    Finds the face in every picture of a person and writes it to
    Essenthos.Forge/Loading/Encyclopedia/PortraitFaces.json, which the image loader turns into the
    head-and-shoulders crop every small rendering of the picture shows.

.DESCRIPTION
    Windows' own offline face detector (Windows.Media.FaceAnalysis) reads each picture the generated
    manifest and PublicImages.json list. Nothing is downloaded and nothing leaves the machine.

    Where it finds several faces the one kept is the largest, weighed towards the middle and the
    top: a portrait's subject stands in front, and the faces around him are the crowd behind.

    An entry written by hand, with "by": "eye", is kept as it is: that is somebody having looked
    at a picture the detector got wrong or found nothing in. A picture it finds no face in is written
    with "face": null, so the gap is visible and can be filled by eye.

    Each entry carries the digest of the file it was measured on. A picture replaced under the same
    name no longer matches it, and the loader leaves its crop out until this is run again.

    Runs under Windows PowerShell 5.1 (powershell.exe), which is what can call the Windows Runtime;
    PowerShell 7 cannot.

.EXAMPLE
    powershell.exe -NoProfile -ExecutionPolicy Bypass -File scripts/find-faces.ps1
#>

[CmdletBinding()]
param(
    [string] $ResourcesPath = (Join-Path (Join-Path $PSScriptRoot '..') 'Resources')
)

$ErrorActionPreference = 'Stop'

$DigestLength = 12
$Places = 4
$Forge = Join-Path (Join-Path (Join-Path $PSScriptRoot '..') 'Essenthos.Forge') 'Loading\Encyclopedia'
$Curated = Join-Path $Forge 'PublicImages.json'
$Output = Join-Path $Forge 'PortraitFaces.json'
$Images = Join-Path $ResourcesPath 'Images'
$Generated = Join-Path (Join-Path $Images 'generated') 'manifest.json'

Add-Type -AssemblyName System.Runtime.WindowsRuntime
$null = [Windows.Storage.StorageFile, Windows.Storage, ContentType = WindowsRuntime]
$null = [Windows.Graphics.Imaging.BitmapDecoder, Windows.Graphics.Imaging, ContentType = WindowsRuntime]
$null = [Windows.Media.FaceAnalysis.FaceDetector, Windows.Media.FaceAnalysis, ContentType = WindowsRuntime]

$asTask = [System.WindowsRuntimeSystemExtensions].GetMethods() |
    Where-Object { $_.Name -eq 'AsTask' -and $_.GetParameters().Count -eq 1 -and
                   $_.GetParameters()[0].ParameterType.Name -eq 'IAsyncOperation`1' } |
    Select-Object -First 1

function Await($operation, [Type] $type) {
    $task = $asTask.MakeGenericMethod($type).Invoke($null, @($operation))
    $null = $task.Wait(-1)
    $task.Result
}

function Round([double] $value) { [Math]::Round($value, $Places).ToString([Globalization.CultureInfo]::InvariantCulture) }

function Read-Json([string] $path) {
    (Get-Content -LiteralPath $path -Raw -Encoding UTF8) -replace '(?m)^\s*//.*$', '' | ConvertFrom-Json
}

$listed = @()
foreach ($manifest in @($Curated, $Generated)) {
    if (Test-Path -LiteralPath $manifest) {
        $listed += @((Read-Json $manifest).images | ForEach-Object { [string] $_.file })
    }
}
$listed = @($listed | Where-Object { $_ } | Sort-Object -Unique)

$kept = @{}
if (Test-Path -LiteralPath $Output) {
    $previous = Read-Json $Output
    foreach ($property in $previous.PSObject.Properties) {
        if ($property.Value.by -eq 'eye') {
            $kept[$property.Name] = $property.Value
        }
    }
}

$detector = Await ([Windows.Media.FaceAnalysis.FaceDetector]::CreateAsync()) ([Windows.Media.FaceAnalysis.FaceDetector])
$lines = New-Object System.Collections.Generic.List[string]
$found = 0; $none = 0; $byEye = 0; $missing = 0

foreach ($file in $listed) {
    $path = Join-Path $Images $file
    if (-not (Test-Path -LiteralPath $path)) {
        $missing++
        continue
    }
    $digest = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant().Substring(0, $DigestLength)

    if ($kept.ContainsKey($file)) {
        $entry = $kept[$file]
        $face = if ($null -eq $entry.face) { 'null' } else { '[' + (($entry.face | ForEach-Object { Round $_ }) -join ', ') + ']' }
        $lines.Add("  `"$file`": { `"digest`": `"$($entry.digest)`", `"face`": $face, `"by`": `"eye`" }")
        $byEye++
        continue
    }

    $storage = Await ([Windows.Storage.StorageFile]::GetFileFromPathAsync((Resolve-Path -LiteralPath $path).Path)) ([Windows.Storage.StorageFile])
    $stream = Await ($storage.OpenAsync([Windows.Storage.FileAccessMode]::Read)) ([Windows.Storage.Streams.IRandomAccessStream])
    try {
        $decoder = Await ([Windows.Graphics.Imaging.BitmapDecoder]::CreateAsync($stream)) ([Windows.Graphics.Imaging.BitmapDecoder])
        $bitmap = Await ($decoder.GetSoftwareBitmapAsync()) ([Windows.Graphics.Imaging.SoftwareBitmap])
        $gray = [Windows.Graphics.Imaging.SoftwareBitmap]::Convert($bitmap, [Windows.Graphics.Imaging.BitmapPixelFormat]::Gray8)
        $faces = Await ($detector.DetectFacesAsync($gray)) ([System.Collections.Generic.IList[Windows.Media.FaceAnalysis.DetectedFace]])
        $width = [double] $bitmap.PixelWidth
        $height = [double] $bitmap.PixelHeight
    }
    finally {
        $stream.Dispose()
    }

    $best = $null; $bestScore = -1.0
    foreach ($detected in $faces) {
        $box = $detected.FaceBox
        $across = ($box.X + $box.Width / 2) / $width
        $down = ($box.Y + $box.Height / 2) / $height
        $score = $box.Width * $box.Height * (1 - [Math]::Abs($across - 0.5)) * (1 - $down / 2)
        if ($score -gt $bestScore) {
            $best = $box; $bestScore = $score
        }
    }

    if ($null -eq $best) {
        $lines.Add("  `"$file`": { `"digest`": `"$digest`", `"face`": null, `"by`": `"detector`" }")
        $none++
    }
    else {
        $face = @(($best.X / $width), ($best.Y / $height), ($best.Width / $width), ($best.Height / $height)) |
            ForEach-Object { Round $_ }
        $lines.Add("  `"$file`": { `"digest`": `"$digest`", `"face`": [$($face -join ', ')], `"by`": `"detector`" }")
        $found++
    }
}

$json = "{`n" + ($lines -join ",`n") + "`n}`n"
[IO.File]::WriteAllText($Output, $json, (New-Object Text.UTF8Encoding $false))
Write-Host "$found faces found, $none pictures with none, $byEye kept as set by eye, $missing listed files not on disk; written to $Output"
