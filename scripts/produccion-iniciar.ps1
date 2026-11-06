# ============================================================
#  AUKA - Iniciar AUKA del colegio + ngrok
#  Se ejecuta solo al prender el PC si corriste respaldo-programar.ps1
# ============================================================
. "$PSScriptRoot\auka-comun.ps1"

if (-not (Test-Path "$Prod\Auka.Web.exe")) { throw 'No existe C:\Auka\prod\Auka.Web.exe. Primero ejecuta produccion-crear.ps1' }

if (Get-ProcesoProd) {
    Write-Host 'AUKA del colegio ya está funcionando.' -ForegroundColor Green
} else {
    $env:ASPNETCORE_ENVIRONMENT = 'Production'
    Start-Process -FilePath "$Prod\Auka.Web.exe" -ArgumentList "--urls http://localhost:$PuertoProd" -WorkingDirectory $Prod -WindowStyle Minimized
    Escribir "AUKA del colegio iniciado en http://localhost:$PuertoProd" 'Green'
}

# ngrok siempre apunta a PRODUCCIÓN (el colegio). Tus pruebas en Visual Studio quedan solo en tu PC.
$ngrok = Get-Command ngrok -ErrorAction SilentlyContinue
if (-not $ngrok) {
    Write-Host 'No encontré ngrok en el PATH. Ábrelo a mano con:' -ForegroundColor Yellow
    Write-Host "  ngrok http http://localhost:$PuertoProd --url=$UrlNgrok" -ForegroundColor Yellow
} else {
    Get-Process -Name ngrok -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Process -FilePath $ngrok.Source -ArgumentList "http http://localhost:$PuertoProd --url=$UrlNgrok" -WindowStyle Minimized
    Escribir "ngrok publicando $UrlNgrok -> producción" 'Green'
}

# Comprobación
Start-Sleep -Seconds 8
try {
    $r = Invoke-WebRequest "http://localhost:$PuertoProd/Account/Login" -UseBasicParsing -TimeoutSec 20
    Escribir "Comprobación OK (respuesta $($r.StatusCode)). Abre $UrlNgrok" 'Green'
} catch {
    Escribir "AUKA no respondió todavía: $($_.Exception.Message). Espera unos segundos y abre $UrlNgrok" 'Yellow'
}
