# Alistamiento previo a la corrida del ETL.
#
#   powershell -NoProfile -File alistar-corrida.ps1              # solo informa
#   powershell -NoProfile -File alistar-corrida.ps1 -Aplicar     # ademas aparta los archivos
#   powershell -NoProfile -File alistar-corrida.ps1 -Aplicar -Avisar -Arrancar
#
# PROBLEMA QUE RESUELVE
#
# El paquete traduce nombre de archivo -> CarteraID con un Lookup configurado
# para FALLAR si no encuentra coincidencia. Un solo archivo de una cartera no
# registrada mata el flujo entero y se pierde la carga de todas las demas.
# Paso el 2026-09-23: aparecieron 'celucredit' y 'delmar_peru' y tiraron abajo
# las otras veinte carteras.
#
# Este script detecta esos archivos ANTES de correr, los aparta a una subcarpeta
# y avisa cuales quedaron sin procesar. La corrida deja de fallar por esta causa,
# y en vez de un error cripto a los 13 minutos hay una lista clara en segundos.
#
# OJO: hay que apartar el .csv Y el .xlsx. El Foreach itera sobre los .xlsx, asi
# que mover solo el csv no sirve si quedo el Excel de una corrida anterior.
#
# Esto NO reemplaza el arreglo de fondo, que es configurar el Lookup para que
# redirija las filas sin coincidencia en vez de matar el flujo. Eso necesita SSDT.

[CmdletBinding()]
param(
    [string]$Carpeta   = '\\10.0.0.89\bases\SISTEMAS\Apps\ReporteClientes',
    [string]$Prefijo   = 'Reporte Clientes de ',
    [switch]$Aplicar,      # sin esto, solo informa
    [switch]$Avisar,       # manda el resumen por WhatsApp
    [switch]$Arrancar,     # lanza el trabajo si quedo todo limpio
    [string]$CadenaConexion
)

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
function Fallar([string]$t, [int]$c = 1) { [Console]::Error.WriteLine($t); exit $c }

if (-not (Test-Path $Carpeta)) { Fallar "No llego a la carpeta $Carpeta" 2 }

# ── Credenciales del appsettings ignorado por git ───────────────────────────
if (-not $CadenaConexion) {
    $repo = Resolve-Path (Join-Path $dir '..\..\..') -ErrorAction SilentlyContinue
    $appsettings = Join-Path $repo 'src\Zentinela.API\appsettings.Development.json'
    if (-not (Test-Path $appsettings)) { Fallar "No encuentro $appsettings" 2 }
    $CadenaConexion = (Get-Content $appsettings -Raw -Encoding UTF8 | ConvertFrom-Json).ConnectionStrings.CallCenterConnection
}

# ── Carteras registradas ────────────────────────────────────────────────────
try {
    $cn = New-Object System.Data.SqlClient.SqlConnection $CadenaConexion
    $cn.Open()
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = 'SELECT DISTINCT NombreArchivo FROM dbo.Cartera WHERE NombreArchivo IS NOT NULL'
    $cmd.CommandTimeout = 60
    $rd = $cmd.ExecuteReader()
    $registradas = New-Object 'System.Collections.Generic.HashSet[string]' ([StringComparer]::OrdinalIgnoreCase)
    while ($rd.Read()) { [void]$registradas.Add($rd.GetString(0).Trim()) }
    $rd.Close(); $cn.Close()
} catch {
    Fallar "No se pudo leer dbo.Cartera: $($_.Exception.Message)" 1
}

if ($registradas.Count -eq 0) { Fallar 'dbo.Cartera no devolvio ningun NombreArchivo. Abortando por las dudas.' 1 }

# ── Clave de cartera a partir del nombre de archivo ─────────────────────────
# "Reporte Clientes de mercadeos_propia_parte1.xlsx" -> "mercadeos_propia"
# El conversor parte en dos los archivos grandes y les agrega _parteN.
function Clave([string]$nombre) {
    $s = [IO.Path]::GetFileNameWithoutExtension($nombre)
    if ($s.StartsWith($Prefijo, [StringComparison]::OrdinalIgnoreCase)) { $s = $s.Substring($Prefijo.Length) }
    $s = [regex]::Replace($s, '_parte\d+$', '')
    return $s.Trim()
}

$archivos = Get-ChildItem -LiteralPath $Carpeta -File |
            Where-Object { $_.Extension -in '.csv', '.xlsx' }

$sinCartera = $archivos | Where-Object { -not $registradas.Contains((Clave $_.Name)) }

Write-Output "Carpeta        : $Carpeta"
Write-Output "Archivos       : $($archivos.Count)  ($(($archivos | ? Extension -eq '.csv').Count) csv, $(($archivos | ? Extension -eq '.xlsx').Count) xlsx)"
Write-Output "Carteras en BD : $($registradas.Count)"
Write-Output ''

if (-not $sinCartera) {
    Write-Output 'Todo archivo tiene su cartera registrada. La corrida no deberia fallar por el Lookup.'
} else {
    $claves = $sinCartera | ForEach-Object { Clave $_.Name } | Sort-Object -Unique
    Write-Output "SIN CARTERA REGISTRADA -> $($claves -join ', ')"
    foreach ($a in $sinCartera | Sort-Object Name) {
        Write-Output ("  {0,-58} {1,12:N0} bytes" -f $a.Name, $a.Length)
    }
    Write-Output ''

    if ($Aplicar) {
        $destino = Join-Path $Carpeta '_sin_cartera'
        if (-not (Test-Path $destino)) { New-Item -ItemType Directory -Path $destino | Out-Null }
        # Subcarpeta, no borrado: los datos siguen ahi para procesarlos cuando
        # se registren las carteras. El conversor hace glob('*.csv') sin recursion,
        # asi que no los vuelve a ver.
        foreach ($a in $sinCartera) {
            Move-Item -LiteralPath $a.FullName -Destination (Join-Path $destino $a.Name) -Force
            Write-Output "apartado -> _sin_cartera\$($a.Name)"
        }
        Write-Output ''
        Write-Output "$($sinCartera.Count) archivo(s) apartados en $destino"
    } else {
        Write-Output 'Modo informe. Volver a correr con -Aplicar para apartarlos.'
    }
}

# ── Aviso ───────────────────────────────────────────────────────────────────
if ($Avisar -and $sinCartera) {
    $claves = $sinCartera | ForEach-Object { Clave $_.Name } | Sort-Object -Unique
    $verbo  = if ($Aplicar) { 'Se apartaron y NO se van a procesar' } else { 'NO se van a procesar y van a hacer fallar la corrida' }
    $msg = @(
        'ETL de estado de titulares: carteras sin registrar'
        ''
        "$verbo :"
        ($claves | ForEach-Object { "  - $_" })
        ''
        'Estan en la carpeta _sin_cartera, no se borro nada.'
        'Para incorporarlas hay que darlas de alta en Cartera con su NombreArchivo.'
    ) -join "`n"
    & powershell -NoProfile -File (Join-Path $dir 'notificar.ps1') -Mensaje $msg
}

# ── Arranque ────────────────────────────────────────────────────────────────
if ($Arrancar) {
    if ($sinCartera -and -not $Aplicar) {
        Fallar 'No arranco: hay archivos sin cartera y no se pidio -Aplicar. Se caeria igual que la vez pasada.' 1
    }
    try {
        $cn = New-Object System.Data.SqlClient.SqlConnection $CadenaConexion
        $cn.Open()
        $c = $cn.CreateCommand()
        $c.CommandText = "EXEC msdb.dbo.sp_start_job @job_name = N'Ejecutar_ETL_UltimoEstadoTitular'"
        $c.CommandTimeout = 60
        [void]$c.ExecuteNonQuery()
        $cn.Close()
        Write-Output ''
        Write-Output 'Trabajo Ejecutar_ETL_UltimoEstadoTitular lanzado.'
    } catch {
        Fallar "No se pudo lanzar el trabajo: $($_.Exception.Message)" 1
    }
}

exit 0
