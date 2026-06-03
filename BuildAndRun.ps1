<#
.SYNOPSIS
Builds and optionally runs the Win Codex Bar WinUI 3 app.

.DESCRIPTION
Builds with an explicit WinUI platform and launches through winapp run. Do not
run the built exe directly when validating the packaged app path.
#>

param(
    [Parameter(Position = 0)]
    [string]$Project = "WinCodexBar.UI\WinCodexBar.UI.csproj",
    [switch]$SkipRun,
    [switch]$Detach,
    [Parameter(ValueFromRemainingArguments)]
    [string[]]$ExtraArgs
)

$ErrorActionPreference = "Stop"

if ($ExtraArgs -contains "--detach") {
    $Detach = $true
    $ExtraArgs = $ExtraArgs | Where-Object { $_ -ne "--detach" }
}

if (-not (Test-Path $Project)) {
    Write-Error "Project file not found: $Project"
    exit 1
}

$devMode = $false
try {
    $regPath = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock"
    if (Test-Path $regPath) {
        $value = Get-ItemProperty $regPath -Name AllowDevelopmentWithoutDevLicense -ErrorAction SilentlyContinue
        $devMode = $value.AllowDevelopmentWithoutDevLicense -eq 1
    }
} catch {
    $devMode = $false
}

if (-not $devMode) {
    Write-Host "ERROR: Developer Mode is not enabled." -ForegroundColor Red
    Write-Host "Enable Developer Mode in Windows Settings before running packaged WinUI apps." -ForegroundColor Yellow
    exit 1
}

$platform = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") { "ARM64" } else { "x64" }
$configuration = "Debug"

$platformArg = $ExtraArgs | Where-Object { $_ -match "^[/|-]p:Platform=" } | Select-Object -First 1
$configurationArg = $ExtraArgs | Where-Object { $_ -match "^[/|-]p:Configuration=" } | Select-Object -First 1

if ($platformArg -and $platformArg -match "Platform=(\w+)") {
    $platform = $Matches[1]
}

if ($configurationArg -and $configurationArg -match "Configuration=(\w+)") {
    $configuration = $Matches[1]
}

$buildArgs = @(
    $Project,
    "-p:Platform=$platform",
    "-p:Configuration=$configuration",
    "-p:WindowsAppSDKSelfContained=true",
    "-p:WindowsAppSdkBootstrapInitialize=false",
    "-p:WindowsAppSdkDeploymentManagerInitialize=false",
    "-p:WindowsAppSdkUndockedRegFreeWinRTInitialize=false",
    "-r",
    "win-$($platform.ToLowerInvariant())"
)

foreach ($arg in $ExtraArgs) {
    if ($arg -notmatch "^[/|-]p:Platform=" -and $arg -notmatch "^[/|-]p:Configuration=") {
        $buildArgs += $arg
    }
}

Write-Host ""
Write-Host "Building $Project (Platform: $platform, Configuration: $configuration)" -ForegroundColor Cyan
& dotnet build @buildArgs
if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "BUILD SUCCEEDED" -ForegroundColor Green

if ($SkipRun) {
    Write-Host "Skipping run (-SkipRun)." -ForegroundColor DarkGray
    exit 0
}

$projectDir = Split-Path (Resolve-Path $Project) -Parent
$binDir = Join-Path $projectDir "bin\$platform\$configuration"
if (-not (Test-Path $binDir)) {
    Write-Error "Build output not found: $binDir"
    exit 1
}

$tfmDir = Get-ChildItem $binDir -Directory |
    Where-Object { $_.Name -match "^net\d" } |
    Sort-Object Name -Descending |
    Select-Object -First 1

if (-not $tfmDir) {
    Write-Error "No target framework output folder found in: $binDir"
    exit 1
}

$outputDir = Join-Path $tfmDir.FullName "win-$($platform.ToLowerInvariant())"
if (-not (Test-Path $outputDir)) {
    $outputDir = $tfmDir.FullName
}

$manifestPath = Join-Path $projectDir "Package.appxmanifest"
if (-not (Test-Path $manifestPath)) {
    Write-Error "Package manifest not found: $manifestPath"
    exit 1
}

$projectXml = [xml](Get-Content $Project -Raw)
$assemblyName = $projectXml.Project.PropertyGroup |
    Where-Object { $_.AssemblyName } |
    Select-Object -First 1 -ExpandProperty AssemblyName
if (-not $assemblyName) {
    $assemblyName = [System.IO.Path]::GetFileNameWithoutExtension($Project)
}
$executableName = "$assemblyName.exe"

$winapp = Get-Command winapp -ErrorAction SilentlyContinue
if (-not $winapp) {
    Write-Error "winapp CLI was not found in PATH. Install Microsoft.WinAppCLI or run the app from Visual Studio."
    exit 1
}

Write-Host ""
if ($Detach) {
    Write-Host "Launching app in background with winapp run." -ForegroundColor Cyan
    & winapp run $outputDir --manifest $manifestPath --executable $executableName --detach --json
} else {
    Write-Host "Launching app with winapp run --debug-output." -ForegroundColor Cyan
    & winapp run $outputDir --manifest $manifestPath --executable $executableName --debug-output
}
