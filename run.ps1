#Requires -Version 5.1
$ErrorActionPreference = "Stop"

function Sync-EnvPath {
    # winget/MSI installers update the registry PATH, but the current process
    # (and any process it spawns) keeps its own stale copy in memory until a
    # new session picks up the change. Re-read Machine + User PATH so this
    # process sees an install that just happened, without needing a restart.
    $machinePath = [System.Environment]::GetEnvironmentVariable("Path", "Machine")
    $userPath = [System.Environment]::GetEnvironmentVariable("Path", "User")
    $env:Path = @($machinePath, $userPath) -join ";"

    # Fall back to well-known install locations in case the registry PATH
    # itself hasn't propagated yet (seen after some winget installs).
    $wellKnownDirs = @(
        (Join-Path $env:ProgramFiles "dotnet"),
        (Join-Path $env:LOCALAPPDATA "Microsoft\dotnet")
    )
    foreach ($dir in $wellKnownDirs) {
        if ((Test-Path (Join-Path $dir "dotnet.exe")) -and ($env:Path -notlike "*$dir*")) {
            $env:Path = "$env:Path;$dir"
        }
    }
}

function Get-DotnetMajorVersion {
    try {
        $verString = & dotnet --version 2>$null
        if (-not $verString) { return -1 }
        return [int]($verString.Split('.')[0])
    } catch {
        return -1
    }
}

Sync-EnvPath

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

        Sync-EnvPath
        $dotnetCmd = Get-Command dotnet -ErrorAction SilentlyContinue
        $major = if ($dotnetCmd) { Get-DotnetMajorVersion } else { -1 }

        if ($major -lt 8) {
            Write-Host ".NET SDK was installed, but this terminal still can't see it."
            Write-Host "Close this terminal, open a new one, and run this script again."
            exit 1
        }
    } else {
        Write-Host "winget was not found."
        Write-Host "Install the .NET 8 SDK manually: https://dotnet.microsoft.com/download/dotnet/8.0"
        exit 1
    }
}

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
dotnet run --project (Join-Path $scriptDir "src/Btd6Localizer") -- @args
