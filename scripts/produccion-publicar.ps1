# ============================================================
#  AUKA - Publicar tus cambios al COLEGIO (en tu horario de 22:00 a 03:00)
#  1. Respaldo de AukaProd (si falla, no sigue)
#  2. Detiene AUKA del colegio
#  3. Publica la versión nueva en C:\Auka\prod (las migraciones nuevas se aplican solas al iniciar)
#  4. Vuelve a iniciar AUKA y ngrok
#  Antes de publicar: compila y prueba en Visual Studio (tu base AukaDb).
# ============================================================
param([switch]$SinRespaldo)
. "$PSScriptRoot\auka-comun.ps1"

if (-not $SinRespaldo) {
    & "$PSScriptRoot\respaldo.ps1"
    if ($LASTEXITCODE -ne 0) { Escribir 'No publico porque el respaldo falló. El colegio sigue con la versión anterior.' 'Red'; exit 1 }
}

$p = Get-ProcesoProd
if ($p) { Escribir 'Deteniendo AUKA del colegio...' 'Cyan'; $p | Stop-Process -Force; Start-Sleep -Seconds 2 }

Escribir 'Publicando la versión nueva...' 'Cyan'
dotnet publish "$Repo\Auka.Web\Auka.Web.csproj" -c Release -o $Prod --nologo
if ($LASTEXITCODE -ne 0) {
    Escribir 'ERROR al publicar (revisa los errores arriba). Vuelvo a iniciar la versión anterior.' 'Red'
    & "$PSScriptRoot\produccion-iniciar.ps1"
    exit 1
}
Escribir 'Versión nueva publicada.' 'Green'
& "$PSScriptRoot\produccion-iniciar.ps1"
