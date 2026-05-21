# AMR auto-start script
# - Launch Docker Desktop -> wait for engine ready
# - docker compose up -d (RabbitMQ/MQTT)
# - Run AMR.Web (published exe if available, otherwise `dotnet run`)
#
# Logs: <repo>/Logs/autostart/start-amr.log
#       AMR.Web stdout/stderr -> web-stdout.log / web-stderr.log

#Requires -Version 5.1

$ErrorActionPreference = 'Continue'

# scripts/start-amr.ps1 -> parent is repo root
$RepoRoot      = Split-Path -Parent $PSScriptRoot
$ComposeFile   = Join-Path $RepoRoot "docker\docker-compose.yml"
$WebProject    = Join-Path $RepoRoot "AMR.Web\AMR.Web.csproj"
$PublishedExe  = Join-Path $RepoRoot "AMR.Web\bin\Release\net8.0\publish\AMR.Web.exe"
$LogDir        = Join-Path $RepoRoot "Logs\autostart"
$DockerDesktop = "C:\Program Files\Docker\Docker\Docker Desktop.exe"

New-Item -ItemType Directory -Path $LogDir -Force | Out-Null
$LogFile      = Join-Path $LogDir "start-amr.log"
$WebStdoutLog = Join-Path $LogDir "web-stdout.log"
$WebStderrLog = Join-Path $LogDir "web-stderr.log"

function Log([string]$Message) {
    $ts = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $line = "$ts  $Message"
    Write-Host $line
    Add-Content -Path $LogFile -Value $line -Encoding UTF8
}

Log "===== AMR auto-start ====="
Log "RepoRoot    = $RepoRoot"
Log "ComposeFile = $ComposeFile"
Log "WebProject  = $WebProject"

# 1) Launch Docker Desktop if not running
if (-not (Get-Process -Name "Docker Desktop" -ErrorAction SilentlyContinue)) {
    if (Test-Path $DockerDesktop) {
        Log "Launching Docker Desktop"
        Start-Process -FilePath $DockerDesktop
    } else {
        Log "WARN: Docker Desktop.exe not found at $DockerDesktop"
    }
} else {
    Log "Docker Desktop already running"
}

# 2) Wait for Docker engine (up to 120s)
$dockerReady = $false
for ($i = 1; $i -le 60; $i++) {
    docker info 2>$null | Out-Null
    if ($LASTEXITCODE -eq 0) {
        $dockerReady = $true
        Log "Docker engine ready after $i attempt(s)"
        break
    }
    if (($i % 5) -eq 0) { Log "Waiting for Docker engine... ($i/60)" }
    Start-Sleep -Seconds 2
}

if (-not $dockerReady) {
    Log "ERROR: Docker engine timed out - skipping docker compose, starting AMR.Web only"
} else {
    # 3) docker compose up -d
    Log "Running: docker compose up -d"
    $composeOut = & docker compose -f $ComposeFile up -d 2>&1
    foreach ($ln in $composeOut) { Log "[compose] $ln" }
}

# 4) Run AMR.Web
if (Test-Path $PublishedExe) {
    Log "Using published AMR.Web: $PublishedExe"
    $webExe  = $PublishedExe
    $webArgs = "--urls http://0.0.0.0:5200"
} else {
    Log "No published build - falling back to 'dotnet run'"
    $webExe  = "dotnet"
    $webArgs = "run --project `"$WebProject`" --no-launch-profile --urls http://0.0.0.0:5200"
}

try {
    $proc = Start-Process `
        -FilePath $webExe `
        -ArgumentList $webArgs `
        -WorkingDirectory $RepoRoot `
        -RedirectStandardOutput $WebStdoutLog `
        -RedirectStandardError $WebStderrLog `
        -NoNewWindow `
        -PassThru

    Log "AMR.Web started (PID=$($proc.Id))  stdout -> $WebStdoutLog"
    $proc.WaitForExit()
    Log "AMR.Web exited (ExitCode=$($proc.ExitCode))"
}
catch {
    Log "ERROR: AMR.Web failed to start - $_"
    exit 1
}
