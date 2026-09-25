# Despliegue del cargador de UltimoEstadoTitular (ADR-039). Se corre desde la maquina
# del desarrollador, con el repositorio al dia.
#
#   powershell -ExecutionPolicy Bypass -File etl\estado-titular\desplegar.ps1
#       1. copia el cargador a \\10.0.0.89\bases\...\cargador\
#       2. aplica las migraciones del ETL (idempotentes)
#       3. crea los trabajos del Agente (publicar y comparar)
#       4. corre el trabajo de COMPARAR y muestra el resultado. No escribe datos.
#
#   powershell -ExecutionPolicy Bypass -File etl\estado-titular\desplegar.ps1 -Publicar
#       solo corre el trabajo de PUBLICAR y espera a que termine. Escribe en
#       UltimoEstadoTitular e HistoricoEstadoTitular, con las compuertas de ADR-038.
#
# La contrasena de sa se pide por pantalla y vive solo en este proceso: no se guarda
# en ningun archivo ni aparece en la linea de comandos de sqlcmd.

[CmdletBinding()]
param(
    [string]$Servidor = '10.0.0.89,1433',
    [string]$Usuario  = 'sa',
    [string]$Destino  = '\\10.0.0.89\bases\SISTEMAS\Apps\ETL Carga UltimoEstadoTitular\cargador',
    [switch]$Publicar,
    [int]$EsperaMaximaMin = 120
)

$ErrorActionPreference = 'Stop'
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$migraciones = Join-Path $repo 'docs\sql\migrations'
function Paso([string]$t) { Write-Host ''; Write-Host "== $t" -ForegroundColor Cyan }

# ── sqlcmd y credencial ──────────────────────────────────────────────────────
$sqlcmd = (Get-Command sqlcmd.exe -ErrorAction SilentlyContinue).Source
if (-not $sqlcmd) {
    $sqlcmd = Get-ChildItem 'C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\*\Tools\Binn\sqlcmd.exe' -ErrorAction SilentlyContinue |
              Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (-not $sqlcmd) { throw 'No se encontro sqlcmd.exe' }

# Se valida el inicio de sesion ANTES de tocar nada, con hasta 3 intentos: una
# contrasena mal escrita no debe dejar el despliegue a medias.
for ($intento = 1; $true; $intento++) {
    if (-not $env:SQLCMDPASSWORD) {
        $seguro = Read-Host "Contrasena de $Usuario en $Servidor" -AsSecureString
        $env:SQLCMDPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [Runtime.InteropServices.Marshal]::SecureStringToBSTR($seguro))
    }
    & $sqlcmd -S $Servidor -U $Usuario -C -l 30 -b -h -1 -Q "SET NOCOUNT ON; SELECT 1" *> $null
    if ($LASTEXITCODE -eq 0) { Write-Host 'Inicio de sesion correcto.' -ForegroundColor Green; break }
    $env:SQLCMDPASSWORD = $null
    if ($intento -ge 3) { throw "No se pudo iniciar sesion como $Usuario en $Servidor. No se hizo nada." }
    Write-Warning "No se pudo iniciar sesion como $Usuario (intento $intento de 3). Vuelve a escribirla."
}

function Sql([string]$consulta, [string]$base = 'msdb') {
    $salida = & $sqlcmd -S $Servidor -U $Usuario -C -I -b -W -h -1 -l 60 -d $base -Q "SET NOCOUNT ON; $consulta"
    if ($LASTEXITCODE -ne 0) { throw "sqlcmd fallo: $salida" }
    return $salida
}

function Aplicar([string]$archivo) {
    # Ruta relativa a proposito: sqlcmd -i con rutas absolutas de este repo dio "Acceso denegado".
    Push-Location $migraciones
    try {
        & $sqlcmd -S $Servidor -U $Usuario -C -I -b -l 60 -t 600 -i $archivo
        if ($LASTEXITCODE -ne 0) { throw "La migracion $archivo fallo (codigo $LASTEXITCODE). No se siguio." }
    } finally { Pop-Location }
}

# Arranca un trabajo y espera a que termine. Se espera por el HISTORIAL (fila del
# trabajo completo, step_id 0) posterior a la hora de arranque tomada del servidor:
# sysjobactivity tarda en reflejar el arranque y ya nos hizo leer una corrida vieja.
function Correr([string]$trabajo) {
    $inicio = "$(Sql "SELECT CONVERT(VARCHAR(19), DATEADD(SECOND, -5, GETDATE()), 120);")".Trim()
    Sql "EXEC dbo.sp_start_job @job_name = N'$trabajo';" | Out-Null
    Write-Host "Arrancado $trabajo a las $inicio (hora del servidor). Esperando..."
    $limite = (Get-Date).AddMinutes($EsperaMaximaMin)
    do {
        Start-Sleep -Seconds 30
        $fila = Sql @"
SELECT TOP 1 CONCAT(h.run_status, '|', h.run_duration)
FROM dbo.sysjobhistory h JOIN dbo.sysjobs j ON j.job_id = h.job_id
WHERE j.name = N'$trabajo' AND h.step_id = 0
  AND msdb.dbo.agent_datetime(h.run_date, h.run_time) >= CONVERT(DATETIME, '$inicio', 120)
ORDER BY h.instance_id DESC;
"@
        $fila = "$fila".Trim()
        if (-not $fila) { Write-Host ("  {0:HH:mm:ss} sigue corriendo" -f (Get-Date)) }
    } while (-not $fila -and (Get-Date) -lt $limite)

    if (-not $fila) { Write-Warning "No termino en $EsperaMaximaMin min. Sigue corriendo en el servidor; el log queda en $Destino\logs\"; return $false }
    $estado, $dur = $fila.Split('|')
    $d = '{0:D6}' -f [int]$dur
    Write-Host ("Termino: {0} en {1}h {2}m {3}s" -f $(if ($estado -eq '1') { 'EXITO' } else { 'FALLO' }), $d.Substring(0,2), $d.Substring(2,2), $d.Substring(4,2))
    return ($estado -eq '1')
}

function MostrarLog([string]$modo) {
    $log = Join-Path $Destino "logs\$modo.log"
    Paso "Log de la corrida ($log)"
    if (Test-Path -LiteralPath $log) { Get-Content -LiteralPath $log -Encoding UTF8 | Write-Host }
    else { Write-Warning 'No aparecio el log.' }
}

if ($Publicar) {
    Paso 'PUBLICAR: carga UltimoEstadoTitular e HistoricoEstadoTitular'
    $ok = Correr 'ETL_UltimoEstadoTitular_Cargador'
    MostrarLog 'publicar'
    exit $(if ($ok) { 0 } else { 1 })
}

# ── 1. Copiar el cargador ────────────────────────────────────────────────────
Paso "1/4 Copiando el cargador a $Destino"
New-Item -ItemType Directory -Force -Path $Destino, (Join-Path $Destino 'logs') | Out-Null
foreach ($f in 'cargar.ps1', 'limpiar.py', 'procesar.sql', 'README.md') {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $f) -Destination $Destino -Force
    Write-Host "  $f"
}

# ── 2. Migraciones ───────────────────────────────────────────────────────────
Paso '2/4 Migraciones del ETL'
foreach ($m in '2026-09-23_rediseno_etl_estado_titular.sql',
               '2026-09-25_cargador_estado_titular.sql',
               '2026-09-23_avisos_etl_estado_titular.sql') {
    Write-Host "  $m"
    Aplicar $m
}

# ── 3. Trabajos del Agente ───────────────────────────────────────────────────
Paso '3/4 Trabajos del Agente'
Aplicar '2026-09-25_trabajos_cargador_estado_titular.sql'

# ── 4. Comparar ──────────────────────────────────────────────────────────────
Paso '4/4 COMPARAR (no escribe datos)'
$ok = Correr 'ETL_UltimoEstadoTitular_Cargador_Comparar'
MostrarLog 'comparar'
Write-Host ''
if ($ok) {
    Write-Host 'Despliegue listo. Para publicar:' -ForegroundColor Green
    Write-Host "  powershell -ExecutionPolicy Bypass -File `"$PSCommandPath`" -Publicar"
} else {
    Write-Host 'La comparacion fallo: NO publicar. Revisar el log de arriba.' -ForegroundColor Red
    exit 1
}
