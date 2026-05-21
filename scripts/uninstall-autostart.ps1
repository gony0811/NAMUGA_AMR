# Remove AMR auto-start task.

#Requires -Version 5.1
#Requires -RunAsAdministrator

$TaskName = "AMR Auto Start"

$existing = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
if ($existing) {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false
    Write-Host "Task Scheduler entry removed: $TaskName" -ForegroundColor Green
} else {
    Write-Host "No task registered: $TaskName" -ForegroundColor Yellow
}
