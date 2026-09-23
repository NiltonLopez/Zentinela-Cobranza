# Vigia de frescura del ETL de estado de titulares.
#
#   powershell -NoProfile -File vigia.ps1                 chequeo diario (26 h / 42 h)
#   powershell -NoProfile -File vigia.ps1 -HorasMaximas 3.5   vigilar una corrida en progreso
#   powershell -NoProfile -File vigia.ps1 -SoloVer        diagnostica y NO envia nada
#
# Es deliberadamente tonto: mira la vista de alerta, y si hay algo que decir lo
# dice. No diagnostica causas — para eso esta la skill, que la invoca una persona.
# Este script existe para correr solo, cada 30 minutos, sin que nadie tenga nada
# abierto. Ver "Como programarlo" al final.
#
# Requiere: 2026-09-23_rediseno_etl_estado_titular.sql y
#           2026-09-23_avisos_etl_estado_titular.sql aplicados.

[CmdletBinding()]
param(
    [double]$HorasMaximas = 0,       # 0 = usar los umbrales de la vista (diarios)
    [int]$HorasSilencio   = 12,      # no repetir el mismo aviso antes de esto
    [switch]$SoloVer,
    [string]$CadenaConexion          # solo para probar contra el harness de LocalDB
)

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path

function Fallar([string]$t, [int]$c = 1) { [Console]::Error.WriteLine($t); exit $c }

# ── Credenciales: del appsettings ignorado por git, nunca hardcodeadas ───────
if ($CadenaConexion) {
    $cadena = $CadenaConexion
} else {
    $repo = Resolve-Path (Join-Path $dir '..\..\..') -ErrorAction SilentlyContinue
    $appsettings = Join-Path $repo 'src\Zentinela.API\appsettings.Development.json'
    if (-not (Test-Path $appsettings)) { Fallar "No encuentro $appsettings" 2 }

    $cadena = (Get-Content $appsettings -Raw -Encoding UTF8 | ConvertFrom-Json).ConnectionStrings.CallCenterConnection
    if (-not $cadena) { Fallar 'No encuentro ConnectionStrings:CallCenterConnection' 2 }
}

$consulta = @'
SET NOCOUNT ON;
SELECT Estado, ISNULL(HorasDesdeCargaOk,-1) AS Horas, ISNULL(FilasActuales,0) AS Filas,
       ISNULL(CONVERT(VARCHAR(20), UltimaCargaOk, 120),'nunca') AS UltimaOk,
       ISNULL(ResultadoUltimoIntento,'-') AS UltimoResultado,
       ISNULL(LEFT(MotivoUltimoIntento,300),'') AS Motivo
FROM dbo.vw_EtlEstadoTitularAlerta;
'@

try {
    $cn = New-Object System.Data.SqlClient.SqlConnection $cadena
    $cn.Open()
    $cmd = $cn.CreateCommand(); $cmd.CommandText = $consulta; $cmd.CommandTimeout = 120
    $tabla = New-Object System.Data.DataTable
    $tabla.Load($cmd.ExecuteReader())
    $cn.Close()
} catch {
    # Si no se puede ni consultar, eso ya es noticia.
    $texto = "ETL de estado de titulares: no se pudo consultar la base para verificar frescura.`n$($_.Exception.Message)"
    Write-Output $texto
    if (-not $SoloVer) { & powershell -NoProfile -File (Join-Path $dir 'notificar.ps1') -Mensaje $texto }
    exit 1
}

if ($tabla.Rows.Count -eq 0) {
    Write-Output 'La vista no devolvio filas: todavia no hay ninguna corrida registrada. Nada que avisar.'
    exit 0
}

$r = $tabla.Rows[0]
$estado = [string]$r.Estado
$horas  = [int]$r.Horas
$filas  = [int]$r.Filas

# El umbral corto sirve para vigilar una corrida puntual, no la frescura diaria.
if ($HorasMaximas -gt 0 -and $horas -ge 0 -and $horas -gt $HorasMaximas -and $estado -eq 'OK') {
    $estado = 'ATENCION'
}

Write-Output "estado=$estado  horas=$horas  filas=$filas  ultima_ok=$($r.UltimaOk)  ultimo_intento=$($r.UltimoResultado)"

if ($estado -eq 'OK') { exit 0 }

$titulo = switch ($estado) {
    'CRITICO' { 'ETL de estado de titulares: INCIDENTE' }
    'RANCIO'  { 'ETL de estado de titulares: datos rancios' }
    default   { 'ETL de estado de titulares: atencion' }
}

$lineas = @(
    $titulo
    ''
    "Ultima carga buena: $($r.UltimaOk)  (hace $horas h)"
    "UltimoEstadoTitular tiene $('{0:N0}' -f $filas) filas."
    "Ultimo intento: $($r.UltimoResultado)"
)
if ($r.Motivo) { $lineas += "Motivo: $($r.Motivo)" }
$lineas += ''
$lineas += if ($filas -gt 0) { 'Los datos existen, solo estan desactualizados.' }
           else              { 'La tabla esta VACIA. El Comparador de Carteras no va a responder.' }
$lineas += 'Para el diagnostico completo, correr la skill etl-estado-titular.'
$mensaje = $lineas -join "`n"

if ($SoloVer) { Write-Output "`n--- mensaje que se enviaria ---`n$mensaje"; exit 0 }

# ── ¿Corresponde avisar, o ya se aviso? ─────────────────────────────────────
$cn = New-Object System.Data.SqlClient.SqlConnection $cadena
$cn.Open()
$c = $cn.CreateCommand()
$c.CommandText = 'SELECT dbo.fn_EtlEstadoTitular_DebeAvisar(@e, @h)'
[void]$c.Parameters.AddWithValue('@e', $estado)
[void]$c.Parameters.AddWithValue('@h', $HorasSilencio)
$debe = [int]$c.ExecuteScalar()

if ($debe -ne 1) {
    Write-Output "Ya se aviso por esto hace menos de $HorasSilencio h y el estado no cambio. No se reenvia."
    $cn.Close(); exit 0
}

& powershell -NoProfile -File (Join-Path $dir 'notificar.ps1') -Mensaje $mensaje
$ok = ($LASTEXITCODE -eq 0)

$reg = $cn.CreateCommand()
$reg.CommandText = 'dbo.sp_EtlEstadoTitular_RegistrarAviso'
$reg.CommandType = [System.Data.CommandType]::StoredProcedure
[void]$reg.Parameters.AddWithValue('@Estado', $estado)
[void]$reg.Parameters.AddWithValue('@Mensaje', $mensaje)
[void]$reg.Parameters.AddWithValue('@HorasAtraso', $horas)
[void]$reg.Parameters.AddWithValue('@Enviado', [int]$ok)
[void]$reg.Parameters.AddWithValue('@DetalleError', $(if ($ok) { [DBNull]::Value } else { 'notificar.ps1 devolvio ' + $LASTEXITCODE }))
[void]$reg.Parameters.AddWithValue('@Origen', 'Vigia')
[void]$reg.ExecuteScalar()
$cn.Close()

if (-not $ok) { Fallar 'El aviso no se pudo enviar. Queda registrado como fallido.' 1 }
exit 0

# ── Como programarlo ────────────────────────────────────────────────────────
#
# Tarea programada de Windows, cada 30 minutos (una sola linea, como Admin):
#
#   schtasks /Create /TN "Vigia ETL UltimoEstadoTitular" /SC MINUTE /MO 30 ^
#     /TR "powershell -NoProfile -ExecutionPolicy Bypass -File \"C:\AgenticWorkspace\Zentinela\.claude\skills\etl-estado-titular\vigia.ps1\"" ^
#     /RL LIMITED
#
# Tambien sirve como paso CmdExec de un trabajo del Agente, pero entonces corre
# con la cuenta del servicio y hay que darle acceso al appsettings y a la red.
# La tarea programada es mas simple y no toca infraestructura compartida.
