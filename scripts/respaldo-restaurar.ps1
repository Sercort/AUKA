# ============================================================
#  AUKA - Restaurar un respaldo (SOLO EMERGENCIAS)
#  Vuelve AukaProd al estado de un respaldo. Antes guarda un respaldo del estado actual.
# ============================================================
. "$PSScriptRoot\auka-comun.ps1"

$lista = Get-ChildItem $Respaldos -Filter "$BaseProd*.backup" | Sort-Object LastWriteTime -Descending | Select-Object -First 15
if (-not $lista) { throw 'No hay respaldos en C:\Auka\respaldos' }
for ($i = 0; $i -lt $lista.Count; $i++) { Write-Host ("[{0}] {1}  ({2:N1} MB)" -f $i, $lista[$i].Name, ($lista[$i].Length / 1MB)) }
$n = Read-Host 'Número del respaldo a restaurar'
$elegido = $lista[[int]$n]
$ok = Read-Host "Se reemplazarán TODOS los datos del colegio por $($elegido.Name). Escribe RESTAURAR para seguir"
if ($ok -ne 'RESTAURAR') { Write-Host 'Cancelado.'; exit 0 }

& "$PSScriptRoot\respaldo.ps1"
if ($LASTEXITCODE -ne 0) { throw 'No pude respaldar el estado actual; no restauro.' }

$p = Get-ProcesoProd; if ($p) { $p | Stop-Process -Force; Start-Sleep -Seconds 2 }
$bin = Get-PgBin; Set-PgPassword
& "$bin\psql.exe" -h localhost -U postgres -c "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname='$BaseProd' AND pid <> pg_backend_pid();" | Out-Null
& "$bin\pg_restore.exe" -h localhost -U postgres --clean --if-exists --no-owner -d $BaseProd $elegido.FullName
Escribir "Restaurado $($elegido.Name) en $BaseProd" 'Green'
& "$PSScriptRoot\produccion-iniciar.ps1"
