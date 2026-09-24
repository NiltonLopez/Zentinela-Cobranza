"""
Limpieza de los CSV de ReporteClientes para la carga de UltimoEstadoTitular.

Reemplaza la conversion a Excel de Conversor_CargaUltimoP.py. La LIMPIEZA es exactamente
la misma porque no se copia: se IMPORTA transformar() del conversor original. Si alguien
cambia una regla de limpieza en el conversor, este cargador la hereda sin tocar nada.

Lo unico que cambia es la salida. En vez de un .xlsx por cartera --que obligaba al
proveedor ACE, el ACE a los 32 bits y los 32 bits a cargar por lotes--, cada CSV produce
un archivo .part con separadores de control, que el cargador de PowerShell sube de
corrido con SqlBulkCopy:

    campo  -> \\x1f  (separador de unidad)
    fila   -> \\x1e  (separador de registro)

Son caracteres de control que no aparecen en texto real, asi que no hace falta
entrecomillar nada: un ";" o una "," dentro de la gestion son solo datos. Si alguno de
esos tres caracteres apareciera en un valor, el archivo se rechaza en vez de escribir
una fila corrida.

Un valor vacio se escribe vacio y el cargador lo sube como NULL. Es lo mismo que pasaba
con el Excel: xlsxwriter no escribe celdas con texto vacio, y el ACE las leia como NULL.

La fecha de promesa se interpreta aca y no en SQL porque tiene que imitar exactamente al
componente de script del paquete: largo distinto de 10 -> NULL; si no, se prueban
"dd/MM/yyyy", "yyyy-MM-dd" y "MM/dd/yyyy" en ese orden, y el primero que sirve gana.

Uso:
    python limpiar.py --entrada <carpeta csv> --salida <carpeta de trabajo>
                      [--conversor <carpeta del Conversor_CargaUltimoP.py>]
                      [--libs <carpeta con pandas> | --sin-libs] [--excluir a,b] [--solo a,b]
                      [--procesos N]
"""
import argparse
import json
import sys
import time
from pathlib import Path

SEP_CAMPO = "\x1f"
SEP_FILA = "\x1e"
PROHIBIDOS = ("\x1d", "\x1e", "\x1f")

# Orden fijo de columnas del .part. El cargador de PowerShell lo espera tal cual.
COLUMNAS = [
    "Archivo", "Linea",
    "identificacion", "SaldoTotal", "fechagestion", "ultusugestion", "FechaPromesa",
    "valorpromesa", "mejorperfil", "ultimoperfil", "motvnopago", "rotacion",
    "Tipo_Cartera", "numeromarcado", "gestion",
]
REQUERIDAS = [c for c in COLUMNAS if c not in ("Archivo", "Linea", "FechaPromesa")] + ["fechapromesa"]

_conv = None
_pd = None


def _iniciar(libs, conversor):
    """Inicializador de cada proceso: rutas, pandas y el conversor original."""
    global _conv, _pd
    if libs:
        sys.path.insert(0, libs)
    # pandas ANTES que el conversor: el conversor mete su propia carpeta de librerias al
    # frente de sys.path al importarse; con pandas ya cargado, se usa el que corresponde.
    import pandas as pd
    _pd = pd
    sys.path.insert(0, conversor)
    import Conversor_CargaUltimoP as conv
    _conv = conv


def cartera_de_archivo(ruta: Path) -> str:
    """Igual que el Script Task del paquete: lo que va despues del primer ' de ', sin _parte1/_parte2."""
    stem = ruta.stem
    pos = stem.find(" de ")
    if pos < 0:
        return ""
    return stem[pos + 4:].replace("_parte1", "").replace("_parte2", "")


def _fecha_promesa(serie):
    """Imita el componente de script del paquete (ver docstring del modulo)."""
    pd = _pd
    s = serie.astype(object).where(serie.notna(), None)
    s = s.map(lambda v: "" if v is None else str(v).strip())
    resultado = pd.Series([None] * len(s), index=s.index, dtype=object)
    candidatas = s.str.len() == 10
    for formato in ("%d/%m/%Y", "%Y-%m-%d", "%m/%d/%Y"):
        pendientes = candidatas & resultado.isna()
        if not pendientes.any():
            break
        convertidas = pd.to_datetime(s[pendientes], format=formato, errors="coerce")
        ok = convertidas.notna()
        resultado.loc[convertidas.index[ok]] = convertidas[ok].dt.strftime("%Y-%m-%d")
    return resultado


def procesar(tarea):
    """Un CSV -> un .part. Devuelve el resumen; nunca se traga un error en silencio."""
    ruta, salida = Path(tarea[0]), Path(tarea[1])
    pd = _pd
    t0 = time.time()
    resumen = {"archivo": ruta.name, "cartera": cartera_de_archivo(ruta), "filas": 0,
               "error": None, "segundos": 0.0, "part": None}
    try:
        # Lectura y limpieza IDENTICAS al conversor original.
        df = pd.read_csv(ruta, sep=";", encoding="latin1", dtype=str, engine="c")
        df = _conv.transformar(df, ruta).reset_index(drop=True)

        faltan = [c for c in REQUERIDAS if c not in df.columns]
        if faltan:
            raise ValueError(f"faltan columnas despues de transformar: {', '.join(faltan)}")

        out = pd.DataFrame({
            "Archivo": resumen["cartera"],
            "Linea": range(2, len(df) + 2),   # linea del CSV (la 1 es el encabezado)
            "identificacion": df["identificacion"],
            "SaldoTotal": df["SaldoTotal"],
            "fechagestion": df["fechagestion"],
            "ultusugestion": df["ultusugestion"],
            "FechaPromesa": _fecha_promesa(df["fechapromesa"]),
            "valorpromesa": df["valorpromesa"],
            "mejorperfil": df["mejorperfil"],
            "ultimoperfil": df["ultimoperfil"],
            "motvnopago": df["motvnopago"],
            "rotacion": df["rotacion"],
            "Tipo_Cartera": df["Tipo_Cartera"],
            "numeromarcado": df["numeromarcado"],
            "gestion": df["gestion"],
        }, columns=COLUMNAS)

        # Todo a texto; None / NaN / "" -> vacio (el cargador lo sube como NULL).
        # fillna + astype por columna, vectorizado: un .map por celda son decenas de
        # millones de llamadas a Python con los archivos grandes.
        out = out.astype(object).fillna("").astype(str)

        for c in PROHIBIDOS:
            sucias = out.apply(lambda col: col.str.contains(c, regex=False)).any(axis=1)
            if sucias.any():
                raise ValueError(f"{int(sucias.sum())} fila(s) contienen el caracter de control U+{ord(c):04X}")

        destino = salida / (ruta.stem + ".part")
        with open(destino, "w", encoding="utf-8", newline="") as f:
            for fila in out.itertuples(index=False, name=None):
                f.write(SEP_CAMPO.join(fila))
                f.write(SEP_FILA)
        resumen["filas"] = len(out)
        resumen["part"] = destino.name
    except Exception as e:  # se informa, no se traga
        resumen["error"] = f"{type(e).__name__}: {e}"
    resumen["segundos"] = round(time.time() - t0, 1)
    return resumen


def main():
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--entrada", required=True)
    ap.add_argument("--salida", required=True)
    ap.add_argument("--conversor", default=r"\\10.0.0.89\bases\SISTEMAS\Apps\ETL Carga UltimoEstadoTitular\Archivos .py")
    ap.add_argument("--libs", default=r"\\10.0.0.89\bases\SISTEMAS\Apps\ETL BlackList\libs",
                    help="carpeta con pandas (la misma que usa el conversor original)")
    ap.add_argument("--sin-libs", action="store_true",
                    help="usar el pandas del interprete en vez de --libs (pruebas fuera del servidor). "
                         "PowerShell 5.1 descarta los argumentos vacios, por eso es un interruptor y no --libs ''")
    ap.add_argument("--excluir", default="", help="carteras (nombre de archivo) a saltear, separadas por coma")
    ap.add_argument("--solo", default="", help="procesar solo estas carteras (para pruebas)")
    ap.add_argument("--procesos", type=int, default=4)
    a = ap.parse_args()

    entrada, salida = Path(a.entrada), Path(a.salida)
    salida.mkdir(parents=True, exist_ok=True)
    for viejo in salida.glob("*.part"):
        viejo.unlink()

    excluir = {x.strip().lower() for x in a.excluir.split(",") if x.strip()}
    solo = {x.strip().lower() for x in a.solo.split(",") if x.strip()}
    archivos = []
    for f in sorted(entrada.glob("*.csv")):
        c = cartera_de_archivo(f).lower()
        if c in excluir or (solo and c not in solo):
            continue
        archivos.append(f)
    if not archivos:
        print("No hay CSV para procesar.", file=sys.stderr)
        sys.exit(2)

    t0 = time.time()
    tareas = [(str(f), str(salida)) for f in archivos]
    libs = "" if a.sin_libs else a.libs
    procesos = max(1, min(a.procesos, len(tareas)))
    if procesos == 1:
        _iniciar(libs, a.conversor)
        resumenes = [procesar(t) for t in tareas]
    else:
        from multiprocessing import Pool
        with Pool(procesos, initializer=_iniciar, initargs=(libs, a.conversor)) as pool:
            resumenes = pool.map(procesar, tareas)

    total = sum(r["filas"] for r in resumenes)
    errores = [r for r in resumenes if r["error"]]
    informe = {"archivos": resumenes, "filas": total, "errores": len(errores),
               "segundos": round(time.time() - t0, 1)}
    (salida / "resumen.json").write_text(json.dumps(informe, ensure_ascii=False, indent=2), encoding="utf-8")

    for r in resumenes:
        estado = "ERROR " + r["error"] if r["error"] else f"{r['filas']:>9,} filas"
        print(f"  {r['archivo']:48s} {estado}  ({r['segundos']}s)")
    print(f"TOTAL {total:,} filas en {informe['segundos']}s, {len(errores)} archivo(s) con error")
    sys.exit(1 if errores else 0)


if __name__ == "__main__":
    main()
