# ============================================================
#  AUKA - datos comunes de los scripts de producción
#  (no ejecutar solo: lo usan los otros scripts)
# ============================================================
$ErrorActionPreference = 'Stop'

$Repo        = Split-Path -Parent $PSScriptRoot          # C:\Users\sergi\source\repos\EduNexus
$Raiz        = 'C:\Auka'
$Prod        = 'C:\Auka\prod'                            # copia publicada que usa el colegio
$Respaldos   = 'C:\Auka\respaldos'
$Registro    = 'C:\Auka\respaldos\registro.txt'
$BaseDev     = 'AukaDb'                                  # tu base de pruebas (Visual Studio)
$BaseProd    = 'AukaProd'                                # la base del colegio
$PuertoProd  = 5100
$UrlNgrok    = 'https://deflate-ranked-able.ngrok-free.dev'
$ConfigProd  = Join-Path $Prod 'appsettings.Production.json'

function Escribir([string]$texto, [string]$color = 'Gray') {
    $linea = "{0}  {1}" -f (Get-Date -Format 'yyyy-MM-dd HH:mm'), $texto
    Write-Host $texto -ForegroundColor $color
    if (Test-Path $Respaldos) { Add-Content -Path $Registro -Value $linea -Encoding UTF8 }
}

# Carpeta bin de PostgreSQL (pg_dump, pg_restore, psql)
function Get-PgBin {
    $c = Get-ChildItem 'C:\Program Files\PostgreSQL\*\bin\pg_dump.exe' -ErrorAction SilentlyContinue |
         Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $c) { throw 'No encontré PostgreSQL en C:\Program Files\PostgreSQL. ¿Está instalado en otra carpeta?' }
    return $c.DirectoryName
}

# Contraseña de postgres: la saca de appsettings.Production.json
function Set-PgPassword {
    if (-not (Test-Path $ConfigProd)) { throw "No existe $ConfigProd. Primero ejecuta produccion-crear.ps1" }
    $cfg = Get-Content $ConfigProd -Raw | ConvertFrom-Json
    $cs  = $cfg.ConnectionStrings.DefaultConnection
    if ($cs -match 'Password=([^;]*)') { $env:PGPASSWORD = $Matches[1] } else { throw 'No encontré Password= en la conexión de producción.' }
}

function Get-ProcesoProd {
    Get-Process -Name 'Auka.Web' -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Prod\*" }
}
