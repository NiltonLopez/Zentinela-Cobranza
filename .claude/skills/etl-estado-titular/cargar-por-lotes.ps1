# Carga del ETL por lotes, para convivir con el techo de 2 GB de 32 bits.
#
#   powershell -NoProfile -File cargar-por-lotes.ps1 -SoloVer      muestra el plan
#   powershell -NoProfile -File cargar-por-lotes.ps1 -Ejecutar     lo corre
#   powershell -NoProfile -File cargar-por-lotes.ps1 -Ejecutar -TamanoLoteMB 60
#
# POR QUE EXISTE
#
# El paquete corre en 32 bits (ver SKILL.md: el proveedor ACE 16.0 esta
# hardcodeado en el Script Task, asi que no se puede pasar a 64 bits sin SSDT o
# sin instalar el ACE de 64 bits). Con todos los archivos juntos revienta por
# memoria despues de unas 4 carteras. En lotes chicos, entra.
#
# ESTO SOLO ES POSIBLE DESDE ADR-038. El procedimiento viejo hacia TRUNCATE de
# la tabla entera en cada publicacion, asi que cada lote borraba al anterior.
# Con el reemplazo acotado, cada lote reemplaza unicamente sus carteras y los
# anteriores sobreviven.
#
# COMO FUNCIONA CADA VUELTA
#
#   1. Aparta los archivos de los otros lotes (csv y xlsx).
#   2. Lanza el trabajo. El paso 1 del paquete vacia el staging, el Foreach
#      carga este lote, y el paso 4 ABORTA por la compuerta de carteras. Eso es
#      esperado: el paquete termina en rojo y el staging queda intacto.
#   3. Publica el lote a mano con @MinimoCarteras al valor real del lote.
#   4. Devuelve los archivos a su lugar.
#
# Al final, una sola pasada de sp_Insertar_HistoricoEstadoTitular, que ademas
# libera el staging.
#
# Si algo falla, el finally devuelve TODOS los archivos. Nunca deja la carpeta
# a medias.

[CmdletBinding()]
param(
    [string]$Carpeta       = '\\10.0.0.89\bases\SISTEMAS\Apps\ReporteClientes',
    [string]$Prefijo       = 'Reporte Clientes de ',
    [int]$TamanoLoteMB     = 80,     # medido: ~138 MB de xlsx ya reventaba
    [int]$MinutosPorLote   = 25,
    [int]$DesdeLote        = 1,      # retomar sin repetir los ya publicados
    [switch]$PublicarStagingPrimero, # publica lo que ya haya en el staging y sigue
    [switch]$Ejecutar,               # sin esto solo muestra el plan
    [switch]$SoloVer,
    [string]$CadenaConexion
)

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
function Fallar([string]$t, [int]$c = 1) { [Console]::Error.WriteLine($t); exit $c }
function Ahora { (Get-Date -Format 'HH:mm:ss') }

if (-not (Test-Path $Carpeta)) { Fallar "No llego a la carpeta $Carpeta" 2 }

if (-not $CadenaConexion) {
    $repo = Resolve-Path (Join-Path $dir '..\..\..') -ErrorAction SilentlyContinue
    $appsettings = Join-Path $repo 'src\Zentinela.API\appsettings.Development.json'
    if (-not (Test-Path $appsettings)) { Fallar "No encuentro $appsettings" 2 }
    $CadenaConexion = (Get-Content $appsettings -Raw -Encoding UTF8 | ConvertFrom-Json).ConnectionStrings.CallCenterConnection
}

function Sql([string]$texto, [int]$timeout = 300) {
    $cn = New-Object System.Data.SqlClient.SqlConnection $CadenaConexion
    $cn.Open()
    try {
        $cmd = $cn.CreateCommand(); $cmd.CommandText = $texto; $cmd.CommandTimeout = $timeout
        $t = New-Object System.Data.DataTable
        $rd = $cmd.ExecuteReader()
        if (-not $rd.IsClosed) { $t.Load($rd) }
        # La coma es imprescindible: al devolver un DataTable, PowerShell lo
        # DESENROLLA y entrega las filas sueltas, con lo que $resultado.Rows
        # queda nulo y el que llama revienta con "no se puede indizar en una
        # matriz nula". El operador coma lo envuelve y lo devuelve entero.
        return ,$t
    } finally { $cn.Close() }
}

function Escalar([string]$texto, [int]$timeout = 300) {
    $cn = New-Object System.Data.SqlClient.SqlConnection $CadenaConexion
    $cn.Open()
    try {
        $cmd = $cn.CreateCommand(); $cmd.CommandText = $texto; $cmd.CommandTimeout = $timeout
        return $cmd.ExecuteScalar()
    } finally { $cn.Close() }
}

function Clave([string]$nombre) {
    $s = [IO.Path]::GetFileNameWithoutExtension($nombre)
    if ($s.StartsWith($Prefijo, [StringComparison]::OrdinalIgnoreCase)) { $s = $s.Substring($Prefijo.Length) }
    return ([regex]::Replace($s, '_parte\d+$', '')).Trim()
}

# ── Armar los lotes ─────────────────────────────────────────────────────────
# Se agrupa por cartera: los archivos de una misma cartera (incluidos sus
# _parteN) van juntos, porque partirlos entre lotes dejaria una cartera a medias
# y el reemplazo acotado la publicaria incompleta.
$todos = Get-ChildItem -LiteralPath $Carpeta -File | Where-Object { $_.Extension -in '.csv', '.xlsx' }
if (-not $todos) { Fallar "No hay archivos en $Carpeta" 1 }

$grupos = $todos | Group-Object { Clave $_.Name } | ForEach-Object {
    [pscustomobject]@{
        Clave    = $_.Name
        Archivos = $_.Group
        PesoMB   = [math]::Round((($_.Group | Where-Object Extension -eq '.xlsx' | Measure-Object Length -Sum).Sum) / 1MB, 1)
    }
} | Sort-Object PesoMB -Descending

$lotes = @(); $actual = @(); $peso = 0.0
foreach ($g in $grupos) {
    # Una cartera mas pesada que el lote entero va sola: partirla no es opcion.
    if ($actual.Count -gt 0 -and ($peso + $g.PesoMB) -gt $TamanoLoteMB) {
        $lotes += ,@($actual); $actual = @(); $peso = 0.0
    }
    $actual += $g; $peso += $g.PesoMB
}
if ($actual.Count -gt 0) { $lotes += ,@($actual) }

Write-Output "Carpeta      : $Carpeta"
Write-Output "Carteras     : $($grupos.Count)   Archivos: $($todos.Count)"
Write-Output "Tope por lote: $TamanoLoteMB MB de xlsx"
Write-Output "Lotes         : $($lotes.Count)"
Write-Output ''
for ($i = 0; $i -lt $lotes.Count; $i++) {
    $mb = [math]::Round((($lotes[$i] | Measure-Object PesoMB -Sum).Sum), 1)
    Write-Output ("Lote {0}  ({1,6} MB)  {2}" -f ($i+1), $mb, (($lotes[$i] | ForEach-Object Clave) -join ', '))
}
Write-Output ''

if ($SoloVer -or -not $Ejecutar) {
    Write-Output 'Plan solamente. Volver a correr con -Ejecutar para cargarlos.'
    exit 0
}

# ── Ejecutar ────────────────────────────────────────────────────────────────
$espera = Join-Path $Carpeta '_lote_en_espera'
if (-not (Test-Path $espera)) { New-Item -ItemType Directory -Path $espera | Out-Null }

$resumen = @()

# Si una corrida anterior se corto despues de cargar pero antes de publicar, lo
# que quedo en el staging todavia sirve: el paquete lo va a borrar en el paso 1
# de la proxima vuelta, asi que hay que publicarlo AHORA o se pierde.
if ($PublicarStagingPrimero) {
    $prev = Sql "SELECT COUNT(*) AS filas, COUNT(DISTINCT CarteraID) AS carteras FROM DBCallCenter.dbo.Staging_UltimoEstadoTitular" 120
    $pf = [int]$prev.Rows[0].filas; $pc = [int]$prev.Rows[0].carteras
    if ($pf -gt 0) {
        Write-Output "[$(Ahora)] publicando lo que quedo en el staging: $pf filas, $pc cartera(s)"
        $r = Sql @"
EXEC DBCallCenter.dbo.sp_Insertar_UltimoEstadoTitular
     @MinimoCarteras = $pc, @ToleranciaFilasPct = 100, @Origen = 'Lote';
SELECT TOP 1 Resultado FROM DBCallCenter.dbo.EtlEstadoTitular_Ejecucion ORDER BY EjecucionID DESC;
"@ 900
        $res = if ($r.Rows.Count) { [string]$r.Rows[0].Resultado } else { '?' }
        Write-Output "[$(Ahora)] staging previo -> $res"
        $resumen += [pscustomobject]@{ Lote = 'previo'; Carteras = "$pc cartera(s)"; Filas = $pf; Estado = $res }
    }
    else { Write-Output "[$(Ahora)] el staging estaba vacio, no habia nada que publicar." }
    Write-Output ''
}

try {
    for ($i = $DesdeLote - 1; $i -lt $lotes.Count; $i++) {
        $n = $i + 1
        $lote = $lotes[$i]
        $claves = ($lote | ForEach-Object Clave)
        Write-Output "[$(Ahora)] ===== LOTE $n de $($lotes.Count): $($claves -join ', ') ====="

        # Apartar todo lo que no sea de este lote
        $deEsteLote = $lote | ForEach-Object { $_.Archivos } | ForEach-Object { $_.Name }
        foreach ($a in $todos) {
            if ($deEsteLote -notcontains $a.Name -and (Test-Path $a.FullName)) {
                Move-Item -LiteralPath $a.FullName -Destination (Join-Path $espera $a.Name) -Force
            }
        }

        # Lanzar y esperar
        [void](Escalar "EXEC msdb.dbo.sp_start_job @job_name = N'Ejecutar_ETL_UltimoEstadoTitular'" 60)
        Start-Sleep -Seconds 15
        $ej = [int](Escalar "SELECT TOP 1 execution_id FROM SSISDB.catalog.executions WHERE project_name='ETL_UltimoEstadoTitular' ORDER BY execution_id DESC" 60)
        Write-Output "[$(Ahora)] ejecucion SSIS $ej lanzada"

        $limite = (Get-Date).AddMinutes($MinutosPorLote)
        do {
            Start-Sleep -Seconds 30
            $st = [int](Escalar "SELECT status FROM SSISDB.catalog.executions WHERE execution_id = $ej" 60)
            $filas = [int](Escalar "SELECT COUNT(*) FROM DBCallCenter.dbo.Staging_UltimoEstadoTitular" 120)
            Write-Output ("[{0}] status={1}  staging={2:N0}" -f (Ahora), $st, $filas)
        } while ($st -in @(1,2,5,8) -and (Get-Date) -lt $limite)

        # El paquete termina en rojo porque la compuerta aborta el paso 4. Lo que
        # decide si el lote sirve es lo que quedo en el staging, no el status.
        $stg = Sql "SELECT COUNT(*) AS filas, COUNT(DISTINCT CarteraID) AS carteras FROM DBCallCenter.dbo.Staging_UltimoEstadoTitular" 120
        $filas    = [int]$stg.Rows[0].filas
        $carteras = [int]$stg.Rows[0].carteras

        if ($filas -eq 0) {
            Write-Output "[$(Ahora)] LOTE $n SIN DATOS en el staging. Se saltea; revisar los errores de la ejecucion $ej."
            $resumen += [pscustomobject]@{ Lote = $n; Carteras = $claves -join ','; Filas = 0; Estado = 'SIN DATOS' }
        }
        else {
            Write-Output "[$(Ahora)] publicando $filas filas de $carteras cartera(s)..."
            # @ToleranciaFilasPct = 100 desactiva la compuerta de volumen: un lote
            # trae a proposito una fraccion del total. La compuerta de carteras
            # sigue viva, con el valor real del lote.
            $r = Sql @"
EXEC DBCallCenter.dbo.sp_Insertar_UltimoEstadoTitular
     @MinimoCarteras = $carteras, @ToleranciaFilasPct = 100, @Origen = 'Lote';
SELECT TOP 1 Resultado, ISNULL(Motivo,'') AS Motivo, FilasInsertadas
FROM DBCallCenter.dbo.EtlEstadoTitular_Ejecucion ORDER BY EjecucionID DESC;
"@ 900
            $res = if ($r.Rows.Count) { [string]$r.Rows[0].Resultado } else { '?' }
            Write-Output "[$(Ahora)] lote $n -> $res"
            $resumen += [pscustomobject]@{ Lote = $n; Carteras = $claves -join ','; Filas = $filas; Estado = $res }
        }

        # Devolver los archivos apartados
        Get-ChildItem -LiteralPath $espera -File | ForEach-Object {
            Move-Item -LiteralPath $_.FullName -Destination (Join-Path $Carpeta $_.Name) -Force
        }
        Write-Output ''
    }
}
finally {
    # Pase lo que pase, la carpeta queda entera.
    if (Test-Path $espera) {
        Get-ChildItem -LiteralPath $espera -File | ForEach-Object {
            Move-Item -LiteralPath $_.FullName -Destination (Join-Path $Carpeta $_.Name) -Force
        }
        Remove-Item -LiteralPath $espera -Force -ErrorAction SilentlyContinue
    }
}

# ── Historico, una sola vez ─────────────────────────────────────────────────
Write-Output "[$(Ahora)] ===== HISTORICO ====="
try {
    [void](Escalar "EXEC DBCallCenter.dbo.sp_Insertar_HistoricoEstadoTitular @Origen = 'Lote'" 3600)
    Write-Output "[$(Ahora)] historico actualizado y staging liberado."
} catch {
    Write-Output "[$(Ahora)] el historico fallo: $($_.Exception.Message)"
}

Write-Output ''
Write-Output '===== RESUMEN ====='
$resumen | Format-Table -AutoSize
$final = Sql "SELECT COUNT(*) AS filas, COUNT(DISTINCT CarteraID) AS carteras, MAX(FechaActualizacion) AS datos_al FROM DBCallCenter.dbo.UltimoEstadoTitular" 300
$final | Format-Table -AutoSize
exit 0
