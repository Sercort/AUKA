# ============================================================
#  AUKA - Programar tareas automáticas de Windows (se hace UNA vez)
#  - Respaldo de AukaProd todos los días a las 14:00 y a las 21:45 (antes de tu horario de cambios)
#  - Iniciar AUKA del colegio + ngrok cada vez que inicias sesión en el PC
# ============================================================
. "$PSScriptRoot\auka-comun.ps1"

$ps = 'powershell.exe'
$arg = { param($f) "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File `"$PSScriptRoot\$f`"" }

$acc1 = New-ScheduledTaskAction -Execute $ps -Argument (& $arg 'respaldo.ps1')
$t1 = @( (New-ScheduledTaskTrigger -Daily -At '14:00'), (New-ScheduledTaskTrigger -Daily -At '21:45') )
$conf = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopIfGoingOnBatteries -AllowStartIfOnBatteries
Register-ScheduledTask -TaskName 'AUKA - Respaldo diario' -Action $acc1 -Trigger $t1 -Settings $conf -Force | Out-Null

$acc2 = New-ScheduledTaskAction -Execute $ps -Argument (& $arg 'produccion-iniciar.ps1')
$t2 = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
Register-ScheduledTask -TaskName 'AUKA - Iniciar al prender' -Action $acc2 -Trigger $t2 -Settings $conf -Force | Out-Null

Write-Host 'Listo: "AUKA - Respaldo diario" (14:00 y 21:45) y "AUKA - Iniciar al prender".' -ForegroundColor Green
Write-Host 'Las ves en: Inicio > Programador de tareas > Biblioteca del Programador de tareas.' -ForegroundColor Gray
