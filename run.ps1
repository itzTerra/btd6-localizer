#Requires -Version 5.1
$ErrorActionPreference = "Stop"

function Get-DotnetMajorVersion {
    try {
        $verString = & dotnet --version 2>$null
        if (-not $verString) { return -1 }
        return [int]($verString.Split('.')[0])
    } catch {
        return -1
    }
}

$dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
$major = if ($dotnetCmd) { Get-DotnetMajorVersion } else { -1 }

if ($major -lt 8) {
    Write-Host ".NET SDK 8 or newer is required but was not found."

    $wingetCmd = Get-Command winget -ErrorAction SilentlyContinue
    if ($wingetCmd) {
        Write-Host "About to run: winget install Microsoft.DotNet.SDK.8"
        $reply = Read-Host "Proceed? [y/N]"
        if ($reply -notmatch '^[Yy]$') {
            exit 1
        }
        winget install Microsoft.DotNet.SDK.8
    } else {
        Write-Host "winget was not found."
        Write-Host "Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet run --project (Join-Path $scriptDir "src/Btd6Localizer") -- @args
