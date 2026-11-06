# AUKA: producción (colegio) y desarrollo (tus pruebas)

| | Desarrollo (tú) | Producción (colegio) |
|---|---|---|
| Base de datos | AukaDb | AukaProd |
| Programa | Visual Studio (F5), https://localhost:7157 | C:\Auka\prod, http://localhost:5100 |
| Entrada desde internet | No | https://deflate-ranked-able.ngrok-free.dev (ngrok) |

## Una sola vez
1. `scripts\produccion-crear.ps1`: crea AukaProd (copia de AukaDb), C:\Auka\prod y lo publica.
2. `scripts\respaldo-programar.ps1`: respaldo diario 14:00 y 21:45, y AUKA del colegio se inicia solo al prender el PC.

## Cada noche que cambias algo (22:00 a 03:00)
1. Programas y pruebas en Visual Studio (AukaDb). Update-Database solo contra AukaDb, como siempre.
2. Cuando está probado: `scripts\produccion-publicar.ps1` (respalda, detiene, publica, aplica migraciones, inicia).

## Emergencias
- Volver a un respaldo: `scripts\respaldo-restaurar.ps1`
- AUKA del colegio no abre: `scripts\produccion-iniciar.ps1`

Cómo ejecutar un script: en la terminal de Visual Studio (desde la carpeta EduNexus):
`powershell -ExecutionPolicy Bypass -File scripts\NOMBRE.ps1`
