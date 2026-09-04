<#
.SYNOPSIS
  Publishes devlog to a stable location and puts the `devlog` command on PATH.

.DESCRIPTION
  Both executables are published side by side into %LOCALAPPDATA%\devlog\bin:

      devlog.exe        the command you type
      Devlog.Host.exe   the collector, launched at logon

  That folder is chosen because it is where the database already lives, and
  because it survives a rebuild, a branch switch, and a Debug/Release change.
  Pointing PATH at bin\Debug instead would break on all three -- and the logon
  registry entry, which StartupRegistration derives from the collector's own
  folder, would break with it.

  Publishing both to one folder is what makes that entry correct: run
  `devlog startup --enable` afterwards and it registers the published
  collector, not one buried in a build output directory.

.PARAMETER Configuration
  Debug (default) or Release.

.PARAMETER SkipPath
  Publish, but leave the user PATH alone.

.PARAMETER SkipFrontend
  Publish without rebuilding the dashboard. For backend-only iteration, or on a
  machine with no Node -- the collector then serves whatever wwwroot it last had.

.EXAMPLE
  .\scripts\install.ps1
  .\scripts\install.ps1 -Configuration Release
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [switch]$SkipPath,

    [switch]$SkipFrontend
)

$ErrorActionPreference = 'Stop'

$repo    = Split-Path -Parent $PSScriptRoot
$target  = Join-Path $env:LOCALAPPDATA 'devlog\bin'
$cliProj = Join-Path $repo 'backend\src\Devlog.Cli\Devlog.Cli.csproj'
$hostProj= Join-Path $repo 'backend\src\Devlog.Host\Devlog.Host.csproj'

# The collector holds its own DLLs open. Publishing over a running instance
# fails with a file lock that reads like a build error, so stop it first and
# say so -- silently killing the user's tracker would be worse.
$running = Get-Process Devlog.Host -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "Stopping the running collector so its files can be replaced..." -ForegroundColor Yellow
    $running | Stop-Process -Force
    Start-Sleep -Milliseconds 500
}

# The dashboard is served by the collector out of its own wwwroot, so it has to
# be built before the publish that copies it. Deliberately here and not as an
# MSBuild target on Devlog.Host: that would make `dotnet build` and `dotnet test`
# require Node, which nothing else about the backend does.
if (-not $SkipFrontend) {
    $frontend = Join-Path $repo 'frontend'

    Write-Host "Building the dashboard..." -ForegroundColor Cyan

    Push-Location $frontend
    try {
        if (-not (Test-Path (Join-Path $frontend 'node_modules'))) {
            npm ci
            if ($LASTEXITCODE -ne 0) { throw "npm ci failed." }
        }

        npm run build
        if ($LASTEXITCODE -ne 0) { throw "Building the frontend failed." }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Publishing $Configuration -> $target" -ForegroundColor Cyan

dotnet publish $hostProj -c $Configuration -o $target --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Publishing Devlog.Host failed." }

dotnet publish $cliProj  -c $Configuration -o $target --nologo -v q
if ($LASTEXITCODE -ne 0) { throw "Publishing Devlog.Cli failed." }

if (-not $SkipPath) {
    # User PATH only. A machine-wide change needs elevation and this is a
    # per-user tool storing per-user data.
    $userPath = [Environment]::GetEnvironmentVariable('Path', 'User')

    $already = $userPath -split ';' | Where-Object { $_.TrimEnd('\') -eq $target.TrimEnd('\') }

    if ($already) {
        Write-Host "PATH already contains $target" -ForegroundColor DarkGray
    }
    else {
        $updated = if ([string]::IsNullOrWhiteSpace($userPath)) { $target } else { "$userPath;$target" }
        [Environment]::SetEnvironmentVariable('Path', $updated, 'User')
        Write-Host "Added $target to your user PATH." -ForegroundColor Green
    }

    # SetEnvironmentVariable does not touch the current session.
    if (-not ($env:Path -split ';' | Where-Object { $_.TrimEnd('\') -eq $target.TrimEnd('\') })) {
        $env:Path = "$env:Path;$target"
    }
}

Write-Host ""
Write-Host "Installed. Try:" -ForegroundColor Green
Write-Host "  devlog                  # what it can do, and whether capture is alive"
Write-Host "  devlog stats"
Write-Host "  devlog startup --enable # register the published collector for logon"
Write-Host ""
Write-Host "New terminals pick up PATH automatically; this one has been updated in place."
