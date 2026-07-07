# Register AMR auto-start in Task Scheduler.
# Run this script ONCE in an elevated PowerShell.
#
# Trigger:   At system startup (no user logon required)
# Identity:  Current user, S4U logon type (no password stored, no interactive desktop)
# Use case:  AMR.Web is a self-hosted ASP.NET Core console app — no UI dependency,
#            so it runs fine in a non-interactive session.
#
# Prerequisite: RabbitMQ must be installed as a native Windows service
#               (Docker Desktop dependency removed). See README.md.

#Requires -Version 5.1
#Requires -RunAsAdministrator

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent $PSScriptRoot
$ScriptPath = Join-Path $PSScriptRoot "start-amr.ps1"
$TaskName   = "AMR Auto Start"

if (-not (Test-Path $ScriptPath)) {
    throw "start-amr.ps1 not found at: $ScriptPath"
}

# Remove existing task
$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Existing task found - removing and re-registering"
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
}

# Action: powershell.exe -File start-amr.ps1
$action = New-ScheduledTaskAction `
    -Execute "powershell.exe" `
    -Argument "-ExecutionPolicy Bypass -NoProfile -WindowStyle Hidden -File `"$ScriptPath`""

# Trigger: at PC startup (30s delay so network/disk subsystems settle)
$trigger = New-ScheduledTaskTrigger -AtStartup
$trigger.Delay = "PT30S"

# Principal: current user, S4U (no password stored, non-interactive — runs without logon)
# Requires "Log on as a batch job" right; running as Administrator at install time
# is enough because the current user typically already has this right.
$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -RunLevel Highest `
    -LogonType S4U

# Settings
$settings = New-ScheduledTaskSettingsSet `
    -AllowStartIfOnBatteries `
    -DontStopIfGoingOnBatteries `
    -StartWhenAvailable `
    -RestartCount 3 `
    -RestartInterval (New-TimeSpan -Minutes 2) `
    -ExecutionTimeLimit (New-TimeSpan -Hours 0)

Register-ScheduledTask `
    -TaskName $TaskName `
    -Action $action `
    -Trigger $trigger `
    -Principal $principal `
    -Settings $settings `
    -Description "Auto-start AMR.Web at PC boot (no logon required). RabbitMQ runs as native Windows service."

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Task Scheduler registered: $TaskName" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Trigger : At system startup (no logon required)"
Write-Host "Identity: $env:USERNAME (S4U, non-interactive)"
Write-Host ""
Write-Host "Will auto-start at next PC boot — even with nobody logged in."
Write-Host "To test immediately:"
Write-Host "    Start-ScheduledTask -TaskName `"$TaskName`""
Write-Host ""
Write-Host "Log: $RepoRoot\Logs\autostart\start-amr.log"
Write-Host ""
Write-Host "REMINDER:"
Write-Host "  Make sure RabbitMQ Windows service is installed and running:"
Write-Host "    Get-Service RabbitMQ"
Write-Host "  And MQTT plugin is enabled:"
Write-Host "    rabbitmq-plugins list | findstr mqtt"
