# Cargador de UltimoEstadoTitular — reemplaza el flujo de datos del paquete SSIS.
#
#   powershell -NoProfile -ExecutionPolicy Bypass -File cargar.ps1 -Modo publicar
#   powershell -NoProfile -ExecutionPolicy Bypass -File cargar.ps1 -Modo comparar -Solo "Nova_Garant,Delmar_prej"
#
# QUE HACE
#   1. limpiar.py lee los CSV y los limpia con transformar() del conversor ORIGINAL
#      (importado, no copiado) y deja un .part por archivo.
#   2. Los .part se suben de corrido a #Carga con SqlBulkCopy (lector en streaming).
#   3. procesar.sql imita el flujo de datos de SSIS con operaciones sobre conjuntos:
#      las tres busquedas, que en el paquete eran ~6 millones de consultas sueltas por
#      carga (busquedas sin cache), son JOINs.
#   4. Modo 'publicar': llena el staging y corre las compuertas de siempre (ADR-038).
#      Modo 'comparar': contrasta el resultado con UltimoEstadoTitular sin escribir nada.
#
# QUE YA NO HACE FALTA
#   Excel, proveedor ACE, 32 bits, carga por lotes, mover archivos entre carpetas, y el
#   alistamiento de carteras sin registrar: una cartera sin alta ya no tumba la carga,
#   sus filas se apartan y se informan.
#
# DONDE CORRE
#   En el servidor, como paso CmdExec del Agente con el proxy ProxySSIS (la cuenta que
#   hoy ya lee E:\BASES). Ahi todo es disco y SQL locales, y la conexion es integrada:
#   sin contrasena de sa en ningun lado. Desde otra maquina tambien funciona, pero paga
#   la red en cada megabyte.

[CmdletBinding()]
param(
    [ValidateSet('comparar', 'publicar')]
    [string]$Modo = 'comparar',
    [string]$Entrada   = '\\10.0.0.89\bases\SISTEMAS\Apps\ReporteClientes',
    [string]$Trabajo   = (Join-Path $env:TEMP 'etl_estado_titular'),
    [string]$Python    = 'python',
    [string[]]$PythonArgs = @(),
    [string]$Conversor = '\\10.0.0.89\bases\SISTEMAS\Apps\ETL Carga UltimoEstadoTitular\Archivos .py',
    [string]$Libs      = '\\10.0.0.89\bases\SISTEMAS\Apps\ETL BlackList\libs',
    [string]$Excluir   = '',
    [string]$Solo      = '',
    [int]$Procesos     = 4,
    [switch]$PandasLocal,          # pruebas fuera del servidor: usar el pandas del interprete
    [switch]$SinLimpiar,           # reutilizar los .part ya generados en -Trabajo (iterar sobre el SQL)
    [string]$CadenaConexion
)

$ErrorActionPreference = 'Stop'
$dir = Split-Path -Parent $MyInvocation.MyCommand.Path
function Ahora { Get-Date -Format 'HH:mm:ss' }
function Fallar([string]$t, [int]$c = 1) { [Console]::Error.WriteLine($t); exit $c }
$reloj = [Diagnostics.Stopwatch]::StartNew()

# ── Conexion ─────────────────────────────────────────────────────────────────
# Sin -CadenaConexion: si esta el appsettings de desarrollo (maquina del desarrollador)
# se usa ese; si no (el servidor), autenticacion integrada con la cuenta del proxy.
if (-not $CadenaConexion) {
    $app = Join-Path $dir '..\..\src\Zentinela.API\appsettings.Development.json'
    if (Test-Path $app) {
        $CadenaConexion = (Get-Content $app -Raw -Encoding UTF8 | ConvertFrom-Json).ConnectionStrings.CallCenterConnection
    } else {
        $CadenaConexion = 'Server=10.0.0.89,1433;Database=DBCallCenter;Integrated Security=True;TrustServerCertificate=True;'
    }
}
$csb = New-Object System.Data.SqlClient.SqlConnectionStringBuilder $CadenaConexion
Write-Output ("[{0}] conexion: {1} / {2} ({3})" -f (Ahora), $csb.DataSource, $csb.InitialCatalog,
    $(if ($csb.IntegratedSecurity) { 'autenticacion integrada' } else { 'usuario ' + $csb.UserID }))

# ── 1. Limpieza en Python ────────────────────────────────────────────────────
if ($SinLimpiar) {
    Write-Output "[$(Ahora)] ===== LIMPIEZA omitida: se reutilizan los .part de $Trabajo ====="
    $codigoPy = 0
} else {
Write-Output "[$(Ahora)] ===== LIMPIEZA ====="
$argsPy = @($PythonArgs) + @((Join-Path $dir 'limpiar.py'), '--entrada', $Entrada, '--salida', $Trabajo,
          '--conversor', $Conversor, '--procesos', $Procesos)
$argsPy += $(if ($PandasLocal) { @('--sin-libs') } else { @('--libs', $Libs) })
if ($Excluir) { $argsPy += @('--excluir', $Excluir) }
if ($Solo)    { $argsPy += @('--solo', $Solo) }
& $Python @argsPy
$codigoPy = $LASTEXITCODE
if ($codigoPy -eq 2) { Fallar 'No habia CSV para procesar.' 2 }
}

$resumenPy = Get-Content (Join-Path $Trabajo 'resumen.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$partes = @(Get-ChildItem -LiteralPath $Trabajo -Filter '*.part' | Sort-Object Name | ForEach-Object FullName)
if ($partes.Count -eq 0) { Fallar 'La limpieza no produjo ningun archivo.' 1 }
if ($codigoPy -ne 0) {
    # Un CSV que no se pudo leer NO frena al resto: se carga lo que hay y la compuerta de
    # carteras decide si alcanza. Pero se dice cual fallo y por que.
    $resumenPy.archivos | Where-Object error | ForEach-Object { Write-Output "[$(Ahora)] ATENCION: $($_.archivo): $($_.error)" }
}

# ── 2. Lector en streaming para SqlBulkCopy ──────────────────────────────────
# C# 5 a proposito: es lo que compila Add-Type en Windows PowerShell 5.1.
if (-not ('LectorPart' -as [type])) {
    Add-Type -ReferencedAssemblies System.Data, System.Xml -TypeDefinition @'
using System;
using System.Data;
using System.IO;
using System.Text;

public sealed class LectorPart : IDataReader
{
    private readonly string[] _archivos;
    private readonly int _n;
    private readonly StringBuilder _sb = new StringBuilder(1024);
    private readonly char[] _buf = new char[1 << 16];
    private int _i = -1, _len, _pos;
    private StreamReader _sr;
    private string[] _campos;
    public long Filas;

    public LectorPart(string[] archivos, int columnas) { _archivos = archivos; _n = columnas; }

    private bool SiguienteArchivo()
    {
        if (_sr != null) { _sr.Dispose(); _sr = null; }
        if (++_i >= _archivos.Length) return false;
        _sr = new StreamReader(_archivos[_i], new UTF8Encoding(false), false, 1 << 20);
        _len = _pos = 0;
        return true;
    }

    // Lee hasta el separador de registro (U+001E). Devuelve null al terminar el archivo.
    private string LeerRegistro()
    {
        _sb.Length = 0;
        while (true)
        {
            if (_pos >= _len)
            {
                _len = _sr.Read(_buf, 0, _buf.Length);
                _pos = 0;
                if (_len <= 0) return _sb.Length > 0 ? _sb.ToString() : null;
            }
            int ini = _pos;
            while (_pos < _len && _buf[_pos] != '\u001e') _pos++;
            _sb.Append(_buf, ini, _pos - ini);
            if (_pos < _len) { _pos++; return _sb.ToString(); }
        }
    }

    public bool Read()
    {
        while (true)
        {
            if (_sr == null && !SiguienteArchivo()) return false;
            string reg = LeerRegistro();
            if (reg == null) { if (!SiguienteArchivo()) return false; continue; }
            if (reg.Length == 0) continue;
            _campos = reg.Split('\u001f');
            if (_campos.Length != _n)
                throw new InvalidDataException(string.Format("{0}: un registro trae {1} campos y se esperaban {2}",
                    Path.GetFileName(_archivos[_i]), _campos.Length, _n));
            Filas++;
            return true;
        }
    }

    // Vacio = NULL, igual que una celda de Excel vacia para el ACE.
    public object GetValue(int i)
    {
        string v = _campos[i];
        if (v.Length == 0) return DBNull.Value;
        if (i == 1) return int.Parse(v);
        return v;
    }
    public bool IsDBNull(int i) { return _campos[i].Length == 0; }
    public int FieldCount { get { return _n; } }
    public void Dispose() { if (_sr != null) { _sr.Dispose(); _sr = null; } }
    public void Close() { Dispose(); }
    public bool IsClosed { get { return _sr == null && _i >= _archivos.Length; } }
    public int Depth { get { return 0; } }
    public int RecordsAffected { get { return -1; } }
    public bool NextResult() { return false; }
    public DataTable GetSchemaTable() { throw new NotSupportedException(); }
    public object this[int i] { get { return GetValue(i); } }
    public object this[string name] { get { throw new NotSupportedException(); } }
    public string GetName(int i) { return "c" + i; }
    public int GetOrdinal(string name) { throw new NotSupportedException(); }
    public Type GetFieldType(int i) { return i == 1 ? typeof(int) : typeof(string); }
    public string GetDataTypeName(int i) { return GetFieldType(i).Name; }
    public string GetString(int i) { return _campos[i]; }
    public int GetInt32(int i) { return int.Parse(_campos[i]); }
    public int GetValues(object[] values) { int k = Math.Min(values.Length, _n); for (int j = 0; j < k; j++) values[j] = GetValue(j); return k; }
    public bool GetBoolean(int i) { throw new NotSupportedException(); }
    public byte GetByte(int i) { throw new NotSupportedException(); }
    public long GetBytes(int i, long o, byte[] b, int bo, int l) { throw new NotSupportedException(); }
    public char GetChar(int i) { throw new NotSupportedException(); }
    public long GetChars(int i, long o, char[] b, int bo, int l) { throw new NotSupportedException(); }
    public IDataReader GetData(int i) { throw new NotSupportedException(); }
    public DateTime GetDateTime(int i) { throw new NotSupportedException(); }
    public decimal GetDecimal(int i) { throw new NotSupportedException(); }
    public double GetDouble(int i) { throw new NotSupportedException(); }
    public float GetFloat(int i) { throw new NotSupportedException(); }
    public Guid GetGuid(int i) { throw new NotSupportedException(); }
    public short GetInt16(int i) { throw new NotSupportedException(); }
    public long GetInt64(int i) { throw new NotSupportedException(); }
}
'@
}

# ── 3. Subir y procesar, en UNA sesion (las #temp viven lo que la conexion) ──
$cn = New-Object System.Data.SqlClient.SqlConnection $CadenaConexion
$cn.Open()
try {
    $crear = $cn.CreateCommand()
    $crear.CommandText = @'
CREATE TABLE #Carga (
    Archivo        NVARCHAR(100) NOT NULL,
    Linea          INT           NOT NULL,
    identificacion NVARCHAR(255) NULL,
    SaldoTotal     NVARCHAR(255) NULL,
    fechagestion   NVARCHAR(255) NULL,
    ultusugestion  NVARCHAR(255) NULL,
    FechaPromesa   NVARCHAR(20)  NULL,
    valorpromesa   NVARCHAR(255) NULL,
    mejorperfil    NVARCHAR(255) NULL,
    ultimoperfil   NVARCHAR(255) NULL,
    motvnopago     NVARCHAR(255) NULL,
    rotacion       NVARCHAR(255) NULL,
    Tipo_Cartera   NVARCHAR(255) NULL,
    numeromarcado  NVARCHAR(255) NULL,
    gestion        NVARCHAR(1000) NULL
);
'@
    [void]$crear.ExecuteNonQuery()

    Write-Output "[$(Ahora)] ===== SUBIDA ($($partes.Count) archivo(s)) ====="
    # ::new y no New-Object -ArgumentList: con dos argumentos, PowerShell convierte el
    # arreglo de rutas en UN texto separado por espacios ("C:.part C:b.part") y el
    # StreamReader falla con "no se admite el formato de la ruta".
    $lector = [LectorPart]::new([string[]]$partes, 15)
    $bulk = New-Object System.Data.SqlClient.SqlBulkCopy($cn, [System.Data.SqlClient.SqlBulkCopyOptions]::TableLock, $null)
    $bulk.DestinationTableName = '#Carga'
    $bulk.BatchSize = 100000
    $bulk.BulkCopyTimeout = 0
    $bulk.EnableStreaming = $true
    for ($k = 0; $k -lt 15; $k++) { [void]$bulk.ColumnMappings.Add($k, $k) }
    $t = [Diagnostics.Stopwatch]::StartNew()
    $bulk.WriteToServer($lector)
    $bulk.Close(); $lector.Dispose()
    Write-Output ("[{0}] {1:N0} filas subidas en {2:N1} s" -f (Ahora), $lector.Filas, $t.Elapsed.TotalSeconds)

    Write-Output "[$(Ahora)] ===== PROCESO (modo $Modo) ====="
    $cmd = $cn.CreateCommand()
    $cmd.CommandText = Get-Content (Join-Path $dir 'procesar.sql') -Raw -Encoding UTF8
    $cmd.CommandTimeout = 0
    [void]$cmd.Parameters.AddWithValue('@Modo', $Modo)
    $ds = New-Object System.Data.DataSet
    $t = [Diagnostics.Stopwatch]::StartNew()
    [void](New-Object System.Data.SqlClient.SqlDataAdapter $cmd).Fill($ds)
    Write-Output ("[{0}] procesado en {1:N1} s" -f (Ahora), $t.Elapsed.TotalSeconds)

    foreach ($tabla in $ds.Tables) {
        if ($tabla.Rows.Count -eq 0) { continue }
        Write-Output ''
        Write-Output ('--- ' + $tabla.Rows[0].bloque + ' ---')
        $tabla.Columns.Remove('bloque')
        ($tabla | Format-Table -AutoSize | Out-String -Width 400).TrimEnd()
    }
}
catch {
    Write-Output ''
    Write-Output "[$(Ahora)] ERROR: $($_.Exception.Message)"
    exit 1
}
finally { $cn.Close() }

Write-Output ''
Write-Output ("[{0}] TOTAL {1:mm\:ss}" -f (Ahora), $reloj.Elapsed)
exit 0
