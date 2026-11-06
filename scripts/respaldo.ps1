# ============================================================
#  AUKA - Respaldo de la base del colegio (AukaProd)
#  Uso: clic derecho > Ejecutar con PowerShell
#  Se ejecuta solo todos los días si corriste respaldo-programar.ps1
#  Guarda en C:\Auka\respaldos y borra los de más de 30 días.
# ============================================================
. "$PSScriptRoot\auka-comun.ps1"

New-Item -ItemType Directory -Force -Path $Respaldos | Out-Null
$bin = Get-PgBin
Set-PgPassword

$archivo = Join-Path $Respaldos ("{0}_{1}.backup" -f $BaseProd, (Get-Date -Format 'yyyy-MM-dd_HHmm'))
& "$bin\pg_dump.exe" -h localhost -U postgres -F c -f $archivo $BaseProd
if ($LASTEXITCODE -ne 0 -or -not (Test-Path $archivo) -or (Get-Item $archivo).Length -lt 1024) {
    Escribir "ERROR: el respaldo falló ($archivo)." 'Red'
    exit 1
}
Escribir ("Respaldo OK: {0} ({1:N1} MB)" -f $archivo, ((Get-Item $archivo).Length / 1MB)) 'Green'

# Copia fuera del PC (opcional): si existe la carpeta de OneDrive "Auka-respaldos", se copia ahí también
if ($env:OneDrive -and (Test-Path "$env:OneDrive\Auka-respaldos")) {
    Copy-Item $archivo "$env:OneDrive\Auka-respaldos\" -Force
    Escribir 'Copia también en OneDrive\Auka-respaldos.' 'Green'
}

# Limpieza: se guardan 30 días
Get-ChildItem $Respaldos -Filter '*.backup' | Where-Object { $_.LastWriteTime -lt (Get-Date).AddDays(-30) } | Remove-Item -Force
exit 0
