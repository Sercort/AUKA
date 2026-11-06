# ============================================================
#  AUKA - Crear la copia de PRODUCCIÓN (se hace UNA sola vez)
#  - Crea la base AukaProd copiando tu base actual (AukaDb), con tu cuenta de dueño de la plataforma.
#  - Crea C:\Auka\prod (programa del colegio) y C:\Auka\respaldos.
#  - Publica AUKA en C:\Auka\prod.
#  Desde aquí en adelante: el colegio usa AukaProd; tú programas y pruebas en AukaDb.
# ============================================================
. "$PSScriptRoot\auka-comun.ps1"

if (Test-Path $ConfigProd) { Write-Host "Producción ya existe ($ConfigProd). No hago nada para no borrar datos del colegio." -ForegroundColor Yellow; exit 0 }

$bin = Get-PgBin
$clave = Read-Host 'Contraseña del usuario postgres (la de tu appsettings.json; Enter = 123456)'
if ([string]::IsNullOrWhiteSpace($clave)) { $clave = '123456' }
$env:PGPASSWORD = $clave

$existe = & "$bin\psql.exe" -h localhost -U postgres -tAc "SELECT 1 FROM pg_database WHERE datname='$BaseProd'"
if ($LASTEXITCODE -ne 0) { throw 'No pude conectarme a PostgreSQL. Revisa la contraseña y que PostgreSQL esté encendido.' }
if ($existe -eq '1') { throw "La base $BaseProd ya existe. No la toco." }

New-Item -ItemType Directory -Force -Path $Raiz, $Prod, $Respaldos | Out-Null

Escribir "Copiando $BaseDev -> $BaseProd ..." 'Cyan'
$tmp = Join-Path $Respaldos 'copia-inicial.backup'
& "$bin\pg_dump.exe" -h localhost -U postgres -F c -f $tmp $BaseDev
if ($LASTEXITCODE -ne 0) { throw 'Falló la copia de la base de pruebas.' }
& "$bin\createdb.exe" -h localhost -U postgres $BaseProd
if ($LASTEXITCODE -ne 0) { throw "No pude crear la base $BaseProd." }
& "$bin\pg_restore.exe" -h localhost -U postgres --no-owner -d $BaseProd $tmp
if ($LASTEXITCODE -ne 0) { Write-Host 'pg_restore terminó con avisos (normalmente no es grave).' -ForegroundColor Yellow }

# Configuración de producción (fuera del repositorio: no se sube a GitHub)
$config = [ordered]@{
    ConnectionStrings = @{ DefaultConnection = "Host=localhost;Port=5432;Database=$BaseProd;Username=postgres;Password=$clave;" }
    Logging           = @{ LogLevel = @{ Default = 'Warning'; 'Microsoft.AspNetCore' = 'Warning' } }
    Correo            = @{ UrlBase = $UrlNgrok }
}
$config | ConvertTo-Json -Depth 5 | Set-Content -Path $ConfigProd -Encoding UTF8
Escribir "Base $BaseProd creada y configuración guardada en $ConfigProd" 'Green'

& "$PSScriptRoot\produccion-publicar.ps1" -SinRespaldo
