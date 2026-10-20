<#
.SYNOPSIS
    Builds MYU STATS for Windows: runs the tests, publishes the app and zips it.

.DESCRIPTION
    Each build is self-contained: the zip holds the .NET runtime and the Windows App SDK, so it runs on
    any Windows 10 (1809) or 11 PC without installing anything. Needs the .NET 10 SDK.

.EXAMPLE
    ./build.ps1
    Tests, then build\MYU-STATS-windows-x64.zip and build\MYU-STATS-windows-arm64.zip.

.EXAMPLE
    ./build.ps1 -Run
    Builds for this PC only and starts it.
#>
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]] $Runtime,
    [switch] $Run,
    [switch] $SkipTests
)

$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$native = if ($env:PROCESSOR_ARCHITECTURE -eq 'ARM64') { 'win-arm64' } else { 'win-x64' }
if (-not $Runtime) {
    $Runtime = if ($Run) { @($native) } else { @('win-x64', 'win-arm64') }
}

if (-not $SkipTests) {
    dotnet test --project MYUStats.Tests
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

# Stop a running copy, so its files can be replaced.
Get-Process MYUStats -ErrorAction SilentlyContinue | Stop-Process -Force

foreach ($rid in $Runtime) {
    $arch = $rid.Split('-')[1]
    $folder = Join-Path 'build' "$arch\MYU STATS"
    if (Test-Path $folder) { Remove-Item $folder -Recurse -Force }

    dotnet publish MYUStats\MYUStats.csproj -c Release -r $rid -p:PublishReadyToRun=true -o $folder
    if ($LASTEXITCODE -ne 0) { throw "Publishing for $rid failed." }

    $zip = Join-Path 'build' "MYU-STATS-windows-$arch.zip"
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path $folder -DestinationPath $zip
    Write-Host "Built $zip"
}

if ($Run) {
    $arch = $native.Split('-')[1]
    Start-Process (Join-Path $PSScriptRoot "build\$arch\MYU STATS\MYUStats.exe")
}
