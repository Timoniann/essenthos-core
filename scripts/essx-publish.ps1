# Publishes the essx tool as a Native AOT executable: <Root>/<Out>/essx.exe.
#
# Visual Studio 18's vcvarsall.bat calls vswhere.exe by its bare name, and the ILCompiler's linker
# discovery reads that script's output as the linker's path, so without vswhere on PATH the link
# fails with "'vswhere.exe' is not recognized". vswhere ships with the Visual Studio Installer;
# its folder goes on PATH for this process only.
param([string]$Root = ".", [string]$Out = ".run/essx")
$ErrorActionPreference = "Stop"
$installer = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if (-not (Get-Command vswhere.exe -ErrorAction SilentlyContinue) -and (Test-Path (Join-Path $installer "vswhere.exe"))) {
    $env:PATH = "$env:PATH;$installer"
}
dotnet publish (Join-Path $Root "Essenthos.Essx/Essenthos.Essx.csproj") -c Release -o (Join-Path $Root $Out) --nologo -v q
exit $LASTEXITCODE
