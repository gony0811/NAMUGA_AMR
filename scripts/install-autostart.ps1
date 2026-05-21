# Register AMR auto-start in Task Scheduler.
# Run this script ONCE in an elevated PowerShell.

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

# Trigger: at logon (1-minute delay to let the system settle)
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
$trigger.Delay = "PT1M"

# Principal: current user, highest privileges
$principal = New-ScheduledTaskPrincipal `
    -UserId $env:USERNAME `
    -RunLevel Highest `
    -LogonType Interactive

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
    -Description "Auto-start Docker Desktop, docker compose, and AMR.Web on user logon"

Write-Host ""
Write-Host "============================================" -ForegroundColor Green
Write-Host "  Task Scheduler registered: $TaskName" -ForegroundColor Green
Write-Host "============================================" -ForegroundColor Green
Write-Host ""
Write-Host "Will auto-start from next logon."
Write-Host "To test immediately:"
Write-Host "    Start-ScheduledTask -TaskName `"$TaskName`""
Write-Host ""
Write-Host "Log: $RepoRoot\Logs\autostart\start-amr.log"
Write-Host ""
Write-Host "NOTE - For auto-start at PC boot, enable Windows auto-login:"
Write-Host "    netplwiz  ->  uncheck 'Users must enter a user name and password'"
