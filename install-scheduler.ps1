$ErrorActionPreference='Stop'
Import-Module ScheduledTasks
$taskName='LED Sign Studio - Scheduled Updates'
$root=$PSScriptRoot
$python=Join-Path $root 'runtime\pythonw.exe'
$script=Join-Path $root 'sign_schedule.py'
if (!(Test-Path -LiteralPath $python)) { throw 'The bundled Python runtime is missing.' }
$action=New-ScheduledTaskAction -Execute $python -Argument ('"'+$script+'" --run-due') -WorkingDirectory $root
$minute=New-ScheduledTaskTrigger -Once -At (Get-Date).AddMinutes(1) -RepetitionInterval (New-TimeSpan -Minutes 1)
$login=New-ScheduledTaskTrigger -AtLogOn -User ([Security.Principal.WindowsIdentity]::GetCurrent().Name)
$principal=New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().User.Value) -LogonType Interactive -RunLevel Limited
$settings=New-ScheduledTaskSettingsSet -StartWhenAvailable -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Minutes 30)
$settings.Hidden=$true
Register-ScheduledTask -TaskName $taskName -Action $action -Trigger @($minute,$login) -Principal $principal -Settings $settings -Description 'Send locally planned LED sign updates over Bluetooth while this user is signed in. Checks once per minute.' -Force | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Output 'Background sender enabled.'
