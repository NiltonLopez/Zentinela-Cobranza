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

function EjecucionesActivas {
    [int](Escalar "SELECT COUNT(*) FROM SSISDB.catalog.executions WHERE project_name='ETL_UltimoEstadoTitular' AND status IN (1,2,5,8) AND start_time > DATEADD(DAY,-1,SYSDATETIMEOFFSET())" 60)
}

# Espera a que no haya ningun paquete del ETL vivo. Mover archivos o lanzar mientras
# uno corre lo contamina: ve los Excel de otro lote. El filtro de 1 dia descarta
# ejecuciones "creadas" que nunca arrancaron y quedaron colgadas en el catalogo.
function EsperarSinEjecucionActiva([int]$Minutos) {
    $hasta = (Get-Date).AddMinutes($Minutos)
    $avise = $false
    while ((EjecucionesActivas) -gt 0) {
        if ((Get-Date) -gt $hasta) { throw "Hay un paquete del ETL corriendo desde hace mas de $Minutos min. No se lanza nada encima." }
        if (-not $avise) { Write-Output "[$(Ahora)] hay un paquete del ETL corriendo; espero a que termine antes de seguir..."; $avise = $true }
        Start-Sleep -Seconds 20
    }
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

# El peso que importa es el del .xlsx, que es lo que el Foreach carga. Pero los .xlsx
# son intermedios y pueden no estar: amanecieron borrados el 24-sep, casi seguro por el
# proceso que regenera los CSV cada dia. (OJO: en un commit anterior se afirmo que los
# borra el ultimo paso del paquete aun cuando falla. Es FALSO: ese mismo dia 21 xlsx
# sobrevivieron seis corridas fallidas seguidas.) Sin xlsx, pesar solo por ellos daba
# CERO para todas las carteras: los 19 grupos entraban en un unico lote y se reproducia
# exactamente el fallo por memoria que esto viene a evitar.
#
# Cuando no hay xlsx se estima desde el CSV. El factor sale de medir la conversion real
# del 2026-09-23: Bertal 66,2->37,6 MB (0,57), Delmar_Cast 225->101,4 (0,45),
# mercadeos_propia 184->87,9 (0,48), Lumen_Propia 81,8->42,7 (0,52), grupo_ceiba
# 71,7->28,9 (0,40). Se toma 0,55, el extremo alto del rango: pasarse de grande es
# reventar por memoria, pasarse de chico solo cuesta un lote mas.
$FactorCsvAXlsx = 0.55

$grupos = $todos | Group-Object { Clave $_.Name } | ForEach-Object {
    $xlsx = ($_.Group | Where-Object Extension -eq '.xlsx' | Measure-Object Length -Sum).Sum
    $csv  = ($_.Group | Where-Object Extension -eq '.csv'  | Measure-Object Length -Sum).Sum
    $bytes = if ($xlsx -gt 0) { $xlsx } else { $csv * $FactorCsvAXlsx }
    [pscustomobject]@{
        Clave    = $_.Name
        Archivos = $_.Group
        PesoMB   = [math]::Round($bytes / 1MB, 1)
        Estimado = ($xlsx -le 0)
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
Write-Output ("Tope por lote: {0} MB de xlsx{1}" -f $TamanoLoteMB, $(if ($grupos | Where-Object Estimado) { "  (pesos estimados desde el CSV: todavia no hay xlsx)" } else { "" }))
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
$huboError = $false

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

        # Apartar todo lo que no sea de este lote.
        #
        # SE RE-ENUMERA LA CARPETA EN CADA VUELTA, no se usa el $todos del plan. Durante la
        # corrida aparecen .xlsx que no existian al armar el plan (los crea el conversor) y
        # que el paquete NO borra cuando falla. Con la lista vieja esos Excel no se apartaban
        # nunca: se acumulaban, y como el Foreach enumera ReporteClientes\*.xlsx*, cada lote
        # volvia a procesar los mismos archivos alfabeticamente primeros hasta reventar por
        # memoria. Los lotes 5 y 6 del 24-sep cargaron exactamente lo mismo por esto.
        # PRIMERO esperar a que no haya un paquete vivo, DESPUES tocar archivos: mover un
        # Excel que un paquete tiene abierto falla ("el archivo esta siendo utilizado"), y
        # moverlo si lo esta por leer lo deja cargando un lote a medias.
        EsperarSinEjecucionActiva -Minutos 30

        $clavesLote = @($lote | ForEach-Object Clave)
        $presentes = Get-ChildItem -LiteralPath $Carpeta -File | Where-Object { $_.Extension -in '.csv', '.xlsx' }
        foreach ($a in $presentes) {
            if ($clavesLote -notcontains (Clave $a.Name)) {
                Move-Item -LiteralPath $a.FullName -Destination (Join-Path $espera $a.Name) -Force
            }
        }

        # ── Lanzar y esperar ────────────────────────────────────────────────
        #
        # 1. Nunca lanzar sobre un paquete que sigue corriendo: veria archivos de otro
        #    lote y sp_start_job ademas fallaria con "el trabajo ya se esta ejecutando".
        EsperarSinEjecucionActiva -Minutos 30

        # 2. Anotar la ultima ejecucion ANTES de lanzar, y esperar a que aparezca una
        #    MAS NUEVA. Antes se esperaban 15 s fijos y se tomaba "la ultima": el 24-sep
        #    el trabajo tardo 23 s en crear la ejecucion SSIS, el script leyo la del lote
        #    anterior (ya fallida), creyo que este habia terminado y paso al siguiente con
        #    el paquete real todavia corriendo y los Excel abiertos.
        $ultimaAntes = [int](Escalar "SELECT ISNULL(MAX(execution_id),0) FROM SSISDB.catalog.executions WHERE project_name='ETL_UltimoEstadoTitular'" 60)
        [void](Escalar "EXEC msdb.dbo.sp_start_job @job_name = N'Ejecutar_ETL_UltimoEstadoTitular'" 60)

        $ej = 0
        $limiteAlta = (Get-Date).AddMinutes(3)
        while ($ej -eq 0 -and (Get-Date) -lt $limiteAlta) {
            Start-Sleep -Seconds 5
            $ej = [int](Escalar "SELECT ISNULL(MAX(execution_id),0) FROM SSISDB.catalog.executions WHERE project_name='ETL_UltimoEstadoTitular' AND execution_id > $ultimaAntes" 60)
        }
        if ($ej -eq 0) { throw "El trabajo no creo ninguna ejecucion SSIS en 3 minutos. Revisar el Agente antes de reintentar." }
        Write-Output "[$(Ahora)] ejecucion SSIS $ej lanzada"

        # 3. Esperar a que ESA ejecucion termine. Si se pasa del tiempo se ABORTA la
        #    corrida entera en vez de seguir: seguir con el paquete vivo es exactamente
        #    lo que ensucio la corrida del 24-sep.
        $limite = (Get-Date).AddMinutes($MinutosPorLote)
        do {
            Start-Sleep -Seconds 30
            $st = [int](Escalar "SELECT status FROM SSISDB.catalog.executions WHERE execution_id = $ej" 60)
            $filas = [int](Escalar "SELECT COUNT(*) FROM DBCallCenter.dbo.Staging_UltimoEstadoTitular" 120)
            Write-Output ("[{0}] status={1}  staging={2:N0}" -f (Ahora), $st, $filas)
        } while ($st -in @(1,2,5,8) -and (Get-Date) -lt $limite)

        if ($st -in @(1,2,5,8)) {
            throw "La ejecucion $ej sigue corriendo despues de $MinutosPorLote min. NO relanzar hasta que termine."
        }

        # El paquete termina en rojo porque la compuerta aborta el paso 4. Lo que
        # decide si el lote sirve es lo que quedo en el staging, no el status.
        $stg = Sql "SELECT COUNT(*) AS filas, COUNT(DISTINCT CarteraID) AS carteras FROM DBCallCenter.dbo.Staging_UltimoEstadoTitular" 120
        $filas    = [int]$stg.Rows[0].filas
        $carteras = [int]$stg.Rows[0].carteras

        # ── La comprobacion que faltaba ──────────────────────────────────────
        # No alcanza con que el staging tenga filas: hay que verificar que sean LAS DE
        # ESTE LOTE. El 24-sep los seis lotes publicaron siempre las mismas carteras
        # (cada uno borraba lo que habia insertado el anterior) y el script informo
        # "OK" seis veces, porque solo miraba que hubiera filas.
        $inClave = ($clavesLote | ForEach-Object { "'" + $_.Replace("'", "''") + "'" }) -join ','
        $espTabla = Sql "SELECT DISTINCT ID FROM DBCallCenter.dbo.Cartera WHERE NombreArchivo IN ($inClave)" 120
        $esperadas = @($espTabla.Rows | ForEach-Object { [int]$_.ID })
        $stgTabla = Sql "SELECT DISTINCT CarteraID FROM DBCallCenter.dbo.Staging_UltimoEstadoTitular" 120
        $enStaging = @($stgTabla.Rows | ForEach-Object { [int]$_.CarteraID })
        $intrusas = @($enStaging | Where-Object { $esperadas -notcontains $_ })

        if ($filas -eq 0) {
            Write-Output "[$(Ahora)] LOTE $n SIN DATOS en el staging. Se saltea; revisar los errores de la ejecucion $ej."
            $resumen += [pscustomobject]@{ Lote = $n; Carteras = $claves -join ','; Filas = 0; Estado = 'SIN DATOS' }
        }
        elseif ($intrusas.Count -gt 0) {
            Write-Output "[$(Ahora)] LOTE $n NO SE PUBLICA: el staging trae carteras ajenas al lote ($($intrusas -join ', '))."
            Write-Output "[$(Ahora)] Esperadas: $($esperadas -join ', ')  |  En staging: $($enStaging -join ', ')"
            Write-Output "[$(Ahora)] Sintoma tipico de Excel de otro lote sin apartar. Revisar la ejecucion $ej."
            $resumen += [pscustomobject]@{ Lote = $n; Carteras = $claves -join ','; Filas = $filas; Estado = 'CARTERAS AJENAS' }
        }
        else {
            # Faltan carteras del lote = el paquete revento a mitad. Publicar igual es
            # SEGURO: el reemplazo acotado solo toca las carteras presentes, asi que las
            # que faltan conservan su dato anterior en vez de quedar vacias. Pero se avisa,
            # porque esas carteras NO quedaron al dia.
            $faltantes = @($esperadas | Where-Object { $enStaging -notcontains $_ })
            if ($faltantes.Count -gt 0) {
                Write-Output "[$(Ahora)] ATENCION: faltan carteras del lote en el staging ($($faltantes -join ', ')). Se publican las que llegaron; esas quedan con su dato anterior."
            }
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
catch {
    # Un error ABORTA la corrida (el try envuelve todo el bucle). Es a proposito, y es lo
    # contrario de lo que decia una version anterior de este comentario ("se registra y
    # se sigue"): seguir con el paquete de un lote todavia vivo es exactamente lo que
    # ensucio la corrida del 24-sep. Se deja constancia del motivo y de donde retomar.
    $huboError = $true
    Write-Output ''
    Write-Output "[$(Ahora)] ERROR en el lote $($i + 1): $($_.Exception.Message)"
    Write-Output "[$(Ahora)] Corrida abortada. Retomar con:  -Ejecutar -DesdeLote $($i + 1)"
    $resumen += [pscustomobject]@{ Lote = $i + 1; Carteras = '-'; Filas = 0; Estado = 'ERROR' }
}
finally {
    # Antes de devolver nada, esperar a que no quede un paquete vivo: devolver los
    # archivos con un paquete corriendo le mete los Excel de todos los lotes. Eso paso
    # el 24-sep (la ejecucion 11247 termino cargando BERTAL en el lote de Delmar_Cast).
    try { EsperarSinEjecucionActiva -Minutos 30 }
    catch { Write-Output "[$(Ahora)] ATENCION: $($_.Exception.Message) Los archivos se devuelven igual; no relanzar hasta que ese paquete termine." }

    # Pase lo que pase, la carpeta queda entera. Cada archivo en su propio try: un
    # archivo bloqueado no puede cortar la devolucion de los demas (el 24-sep un Move-Item
    # fallido mato el script aca y dejo la corrida sin cierre), y el que no vuelva se
    # nombra, porque faltaria en la proxima corrida sin que nadie lo note.
    if (Test-Path $espera) {
        $noVolvieron = @()
        Get-ChildItem -LiteralPath $espera -File | ForEach-Object {
            try { Move-Item -LiteralPath $_.FullName -Destination (Join-Path $Carpeta $_.Name) -Force -ErrorAction Stop }
            catch { $noVolvieron += $_.TargetObject }
        }
        if ($noVolvieron.Count -gt 0) {
            Write-Output "[$(Ahora)] ATENCION: $($noVolvieron.Count) archivo(s) quedaron en _lote_en_espera y hay que devolverlos a mano antes de la proxima corrida."
        } else {
            Remove-Item -LiteralPath $espera -Force -ErrorAction SilentlyContinue
        }
    }

    # Los .xlsx que quedaron son DATOS DE HOY y envenenan la corrida de mañana: el
    # conversor omite ("OMITIDO") todo CSV cuyo Excel ya existe, asi que el Foreach
    # cargaria estos en vez de los CSV frescos, sin ningun aviso. En una corrida normal
    # los borra EliminarExcel.py; aca el paquete termina en rojo a proposito y ese paso
    # no siempre llega. Se apartan (no se borran) a una subcarpeta con fecha.
    $sobrantes = Get-ChildItem -LiteralPath $Carpeta -File -Filter '*.xlsx'
    if ($sobrantes) {
        $procesados = Join-Path $Carpeta ('_procesados_' + (Get-Date -Format 'yyyyMMdd_HHmm'))
        New-Item -ItemType Directory -Path $procesados -Force | Out-Null
        $sobrantes | ForEach-Object { Move-Item -LiteralPath $_.FullName -Destination (Join-Path $procesados $_.Name) -Force }
        Write-Output "[$(Ahora)] $($sobrantes.Count) Excel intermedio(s) apartado(s) a $(Split-Path $procesados -Leaf) para no contaminar la proxima corrida."
    }
}

# ── Historico, una sola vez, y solo si la corrida termino entera ─────────────
# Si abortó, NO se corre: ese paso vacia el staging al final, y el lote que quedo
# cargado sin publicar se perderia. Se retoma con -DesdeLote y el historico corre
# cuando la corrida si termine.
if ($huboError) {
    Write-Output "[$(Ahora)] Historico NO ejecutado porque la corrida aborto. Correra al retomar y terminar."
} else {
    Write-Output "[$(Ahora)] ===== HISTORICO ====="
    try {
        [void](Escalar "EXEC DBCallCenter.dbo.sp_Insertar_HistoricoEstadoTitular @Origen = 'Lote'" 3600)
        Write-Output "[$(Ahora)] historico actualizado y staging liberado."
    } catch {
        Write-Output "[$(Ahora)] el historico fallo: $($_.Exception.Message)"
    }
}

Write-Output ''
Write-Output '===== RESUMEN ====='
$resumen | Format-Table -AutoSize
$final = Sql "SELECT COUNT(*) AS filas, COUNT(DISTINCT CarteraID) AS carteras, MAX(FechaActualizacion) AS datos_al FROM DBCallCenter.dbo.UltimoEstadoTitular" 300
$final | Format-Table -AutoSize
exit 0
