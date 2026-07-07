# AMR auto-start script
# - Verify RabbitMQ Windows service is running (MQTT broker dependency)
# - Run AMR.Web (published exe if available, otherwise `dotnet run`)
#
# Docker Desktop dependency removed — RabbitMQ now runs as a native Windows
# service so the whole stack starts at PC boot without anyone logged in.
#
# Logs: <repo>/Logs/autostart/start-amr.log
#       AMR.Web stdout/stderr -> web-stdout.log / web-stderr.log

#Requires -Version 5.1

$ErrorActionPreference = 'Continue'

# scripts/start-amr.ps1 -> parent is repo root
$RepoRoot      = Split-Path -Parent $PSScriptRoot
$WebProject    = Join-Path $RepoRoot "AMR.Web\AMR.Web.csproj"
$PublishedExe  = Join-Path $RepoRoot "AMR.Web\bin\Release\net8.0\publish\AMR.Web.exe"
$LogDir        = Join-Path $RepoRoot "Logs\autostart"
$RabbitSvcName = "RabbitMQ"

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
Log "RepoRoot   = $RepoRoot"
Log "WebProject = $WebProject"

# 1) Verify RabbitMQ Windows service is running (best-effort — don't block AMR.Web)
try {
    $svc = Get-Service -Name $RabbitSvcName -ErrorAction Stop
    Log "RabbitMQ service status: $($svc.Status)"
    if ($svc.Status -ne 'Running') {
        Log "Attempting to start RabbitMQ service..."
        Start-Service -Name $RabbitSvcName -ErrorAction Stop
        # Wait up to 30s for the broker to actually accept connections
        for ($i = 1; $i -le 15; $i++) {
            Start-Sleep -Seconds 2
            $svc.Refresh()
            if ($svc.Status -eq 'Running') {
                Log "RabbitMQ service running after $($i*2)s"
                break
            }
        }
    }
} catch {
    Log "WARN: RabbitMQ service check failed — $_"
    Log "WARN: AMR.Web will start anyway, but MQTT features may not work until RabbitMQ is up"
}

# 2) Run AMR.Web
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
