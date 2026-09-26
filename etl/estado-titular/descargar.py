"""
Descarga de los "Reporte Clientes" de ControlNext, sin RPA.

Reemplaza al flujo de Power Automate Desktop. Hace, por peticiones HTTP, lo mismo que la
pagina Reports/clients.php de cada cartera:

    1. POST /assets/controller/users.php                        type=login -> cookie de sesion
    2. GET  /{cartera}/Reports/clients.php                      (como al abrir la pagina)
    3. POST /{cartera}/Reports/assets/controller/ReportController.php
            type=obtenerDataReporteClientes, mes en curso, estatus=1 (activos)
            -> {"result":"1","response":<id>}                   el informe queda en cola
    4. POST /{cartera}/Upload/assets/controller/UploadController.php
            type=getStatusReportGen, id=<id>
            -> [{"estado":3,"respuesta":"terminado","nombrearchivo":...,"sql":...}] cuando esta listo
    5. GET  /Downloads/<carpeta del campo sql> + nombrearchivo    el CSV (la misma URL que arma
                                                                downloadfilereport() con url_campaign2)

Cada CSV se baja a <salida>\\_descarga\\, se valida (encabezado con las columnas que usa la
limpieza, al menos una fila) y SOLO entonces reemplaza a "Reporte Clientes de <Cartera>.csv".
El cargador nunca ve un archivo a medio bajar. Si una cartera falla, su CSV anterior queda
como estaba y se informa: el proceso sigue con las demas y termina con codigo 1.

Credenciales: se guardan UNA vez, cifradas con DPAPI de la maquina, con --guardar-credencial
(controlnext.credencial, fuera de git). Tambien sirven CONTROLNEXT_USUARIO / CONTROLNEXT_CLAVE. Si
no hay ninguna y hay consola, se piden por pantalla. La clave nunca va en el codigo, en texto
plano ni en la salida.

Solo biblioteca estandar: el Python del servidor no tiene requests.

Uso:
    python descargar.py --salida <carpeta> [--solo Nova_Garant,Rio_Verde] [--simultaneos 4]
"""
import argparse
import calendar
import concurrent.futures
import datetime as dt
import getpass
import http.cookiejar
import json
import os
import re
import sys
import threading
import time
import urllib.error
import urllib.parse
import urllib.request
from pathlib import Path

# CONTROLNEXT_BASE solo para pruebas contra un simulador local.
BASE = os.environ.get("CONTROLNEXT_BASE", "https://empresa.controlnextapp.com")
AGENTE = ("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 "
          "(KHTML, like Gecko) Chrome/153.0.0.0 Safari/537.36")

# Nombre del CSV ("Reporte Clientes de <nombre>.csv", que es tambien Cartera.NombreArchivo)
# -> ruta de la cartera en ControlNext. La ruta es el nombre en minusculas.
CARTERAS = [
    "Delmar_Cast", "Mercadeos_Personal", "Bertal", "Pelicano_Propia", "Lumen_Propia",
    "Paradas_Credito", "Hogar", "Delmar_prej", "Omegakid", "Pelicano", "Fulgor", "Rio_Verde",
    "grupo_ceiba", "Cifra_Administrativo", "MilAmbientaria", "Interenlace", "Nova_Garant",
    "LATIN", "mercadeos_propia",
]

# Columnas que la limpieza necesita en el CSV crudo. Si falta alguna, el archivo no se usa.
COLUMNAS_MINIMAS = {"identificacion", "fechagestion", "ultusugestion", "fechapromesa",
                    "valorpromesa", "mejorperfil", "ultimoperfil", "motvnopago", "rotacion",
                    "gestion", "numeromarcado"}

ESPERA_CONSULTA = 10          # segundos entre consultas de la cola
ESPERA_MAXIMA = 45 * 60       # por informe


def log(texto):
    print(f"[{dt.datetime.now():%H:%M:%S}] {texto}", flush=True)


class Sesion:
    """Una sesion de ControlNext: cookie PHPSESSID compartida por todos los hilos."""

    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))

    def _pedir(self, url, datos=None, referer=None, ajax=True, timeout=120):
        cab = {"User-Agent": AGENTE, "Accept-Language": "es-ES,es;q=0.9", "Origin": BASE}
        if referer:
            cab["Referer"] = referer
        if ajax:
            cab["X-Requested-With"] = "XMLHttpRequest"
        cuerpo = None
        if datos is not None:
            cuerpo = urllib.parse.urlencode(datos).encode("utf-8")
            cab["Content-Type"] = "application/x-www-form-urlencoded; charset=UTF-8"
        return self.opener.open(urllib.request.Request(url, data=cuerpo, headers=cab), timeout=timeout)

    def texto(self, url, datos=None, referer=None, ajax=True):
        with self._pedir(url, datos, referer, ajax) as r:
            return r.geturl(), r.read().decode("utf-8", errors="replace")

    def json(self, url, datos, referer):
        _, t = self.texto(url, datos, referer)
        try:
            return json.loads(t)
        except json.JSONDecodeError:
            # Sin sesion, ControlNext suele devolver HTML (la pagina de login) en vez de JSON.
            raise RuntimeError(f"respuesta no es JSON (¿sesion vencida?): {t[:80]!r}")

    def bajar(self, url, destino, referer):
        with self._pedir(url, referer=referer, ajax=False, timeout=600) as r, open(destino, "wb") as f:
            while True:
                bloque = r.read(1 << 20)
                if not bloque:
                    break
                f.write(bloque)


# ── Credencial cifrada con DPAPI de Windows ──────────────────────────────────
# Se guarda una vez con --guardar-credencial y queda en controlnext.credencial, junto al
# script y fuera de git. Alcance de MAQUINA: cualquier cuenta de ESA maquina la descifra
# (tambien la del proxy del Agente), pero copiada a otra maquina no sirve. En el servidor
# hay que guardarla en el servidor.
CREDENCIAL = Path(__file__).with_name("controlnext.credencial")


def _dpapi(datos, proteger):
    import ctypes
    import ctypes.wintypes as wt

    class Blob(ctypes.Structure):
        _fields_ = [("cbData", wt.DWORD), ("pbData", ctypes.POINTER(ctypes.c_char))]

    buf = ctypes.create_string_buffer(datos, len(datos))
    entrada = Blob(len(datos), ctypes.cast(buf, ctypes.POINTER(ctypes.c_char)))
    salida = Blob()
    sin_ui, maquina = 0x1, 0x4
    crypt32 = ctypes.windll.crypt32
    if proteger:
        ok = crypt32.CryptProtectData(ctypes.byref(entrada), ctypes.c_wchar_p("ControlNext"), None,
                                      None, None, sin_ui | maquina, ctypes.byref(salida))
    else:
        ok = crypt32.CryptUnprotectData(ctypes.byref(entrada), None, None, None, None, sin_ui,
                                        ctypes.byref(salida))
    if not ok:
        raise ctypes.WinError()
    try:
        return ctypes.string_at(salida.pbData, salida.cbData)
    finally:
        ctypes.windll.kernel32.LocalFree(ctypes.cast(salida.pbData, ctypes.c_void_p))


def guardar_credencial():
    usuario = input("Usuario de ControlNext: ").strip()
    clave = getpass.getpass("Clave de ControlNext (no se ve al escribir): ")
    if not usuario or not clave:
        raise SystemExit("Usuario y clave son obligatorios.")
    cifrada = _dpapi(clave.encode("utf-8"), proteger=True).hex()
    CREDENCIAL.write_text(json.dumps({"usuario": usuario, "clave_dpapi": cifrada}), encoding="utf-8")
    print(f"Credencial guardada y cifrada en {CREDENCIAL}")


def credenciales():
    usuario = os.environ.get("CONTROLNEXT_USUARIO")
    clave = os.environ.get("CONTROLNEXT_CLAVE")
    if usuario and clave:
        return usuario, clave
    if CREDENCIAL.exists():
        d = json.loads(CREDENCIAL.read_text(encoding="utf-8"))
        try:
            return d["usuario"], _dpapi(bytes.fromhex(d["clave_dpapi"]), proteger=False).decode("utf-8")
        except OSError:
            raise SystemExit(f"{CREDENCIAL.name} no se puede descifrar en esta maquina "
                             "(se guardo en otra). Volver a correr con --guardar-credencial.")
    if not sys.stdin.isatty():
        raise SystemExit("No hay credencial: correr una vez con --guardar-credencial.")
    usuario = usuario or input("Usuario de ControlNext: ").strip()
    clave = clave or getpass.getpass("Clave de ControlNext: ")
    return usuario, clave


def iniciar_sesion(s, usuario, clave, cartera_prueba):
    _, resp = s.texto(f"{BASE}/assets/controller/users.php",
                      {"user": usuario, "pass": clave, "type": "login"}, referer=f"{BASE}/Login/")
    # No se sabe de antemano que devuelve el login; la prueba real es poder abrir una
    # pagina de cartera sin que redirija al login.
    url_final, _ = s.texto(f"{BASE}/{cartera_prueba.lower()}/Reports/clients.php", ajax=False)
    if "/login" in url_final.lower():
        raise SystemExit(f"No se pudo iniciar sesion en ControlNext (respuesta: {resp.strip()[:60]!r}).")
    log(f"sesion iniciada como {usuario}")


def url_descarga(estado):
    """Carpeta publica del CSV, sacada de la consulta que ControlNext registra en la cola.

    La carpeta NO se deduce del nombre de la cartera (Paradas_Credito -> "palacio",
    Nova_Garant -> "nuevagarantia"). Pero el campo "sql" del estado trae la ruta donde el
    servidor escribe el archivo, y la web la publica tal cual bajo /Downloads/:
        |/var/lib/postgresql/descargas/downloads/empresa/nuevagarantia/|
        -> https://empresa.controlnextapp.com/Downloads/downloads/empresa/nuevagarantia/
    (comprobado contra url_campaign2 de la pagina, que es lo que usa el boton Descargar).
    """
    m = re.search(r"/descargas/(downloads/[^|'\"]+?/)\s*[|'\"]", estado.get("sql") or "")
    if not m:
        raise RuntimeError("ControlNext no informo la carpeta del archivo (campo 'sql' sin ruta)")
    return f"{BASE}/Downloads/{m.group(1)}"


def validar_csv(ruta):
    with open(ruta, "rb") as f:
        encabezado = f.readline().decode("latin1").strip()
        hay_filas = bool(f.readline().strip())
    columnas = {c.strip().strip('"') for c in encabezado.split(";")}
    faltan = COLUMNAS_MINIMAS - columnas
    if faltan:
        raise RuntimeError(f"el CSV no trae las columnas esperadas (faltan: {', '.join(sorted(faltan))})")
    if not hay_filas:
        raise RuntimeError("el CSV vino sin filas")


def descargar_cartera(s, nombre, salida, mes, candado):
    ruta = nombre.lower()
    t0 = time.time()
    r = {"cartera": nombre, "ok": False, "error": None, "mb": 0.0, "segundos": 0.0}
    temporal = salida / "_descarga" / f"{nombre}.csv.part"
    try:
        referer = f"{BASE}/{ruta}/Reports/clients.php"
        # Se abre la pagina y se encola de a una cartera por vez: si ControlNext guarda la
        # cartera "actual" en la sesion, dos encolados simultaneos podrian pisarse.
        with candado:
            s.texto(referer, ajax=False)
            resp = s.json(f"{BASE}/{ruta}/Reports/assets/controller/ReportController.php",
                          {"type": "obtenerDataReporteClientes", "fechainicio": mes[0],
                           "fechafinal": mes[1], "estatus": "1", "fechas_filtro": "",
                           "esHistorico": "1"}, referer)
        if str(resp.get("result")) != "1":
            raise RuntimeError(f"ControlNext no encolo el informe: {resp}")
        id_informe = resp["response"]
        log(f"{nombre}: en cola (id {id_informe})")

        limite = time.time() + ESPERA_MAXIMA
        while True:
            estado = s.json(f"{BASE}/{ruta}/Upload/assets/controller/UploadController.php",
                            {"type": "getStatusReportGen", "id": id_informe}, referer)
            e = estado[0] if isinstance(estado, list) and estado else {}
            if e.get("error"):
                raise RuntimeError(f"ControlNext informo error al generar: {e['error']}")
            if e.get("estado") == 3 and str(e.get("respuesta", "")).lower() == "terminado":
                archivo = e["nombrearchivo"]
                base_csv = url_descarga(e)
                break
            if time.time() > limite:
                raise RuntimeError(f"no termino en {ESPERA_MAXIMA // 60} min "
                                   f"(estado {e.get('estado')}, {e.get('respuesta')})")
            time.sleep(ESPERA_CONSULTA)

        url_csv = base_csv + urllib.parse.quote(archivo)
        try:
            s.bajar(url_csv, temporal, referer)
        except urllib.error.HTTPError as ex:
            raise RuntimeError(f"HTTP {ex.code} al descargar {url_csv}") from None
        validar_csv(temporal)
        r["mb"] = round(temporal.stat().st_size / 1_048_576, 1)
        # Reemplazo atomico: el CSV viejo sigue ahi hasta el ultimo instante.
        os.replace(temporal, salida / f"Reporte Clientes de {nombre}.csv")
        r["ok"] = True
        log(f"{nombre}: listo ({r['mb']} MB)")
    except Exception as ex:
        r["error"] = f"{type(ex).__name__}: {ex}"
        log(f"{nombre}: ERROR {r['error']}")
        temporal.unlink(missing_ok=True)
    r["segundos"] = round(time.time() - t0, 1)
    return r


def main():
    if "--guardar-credencial" in sys.argv:
        guardar_credencial()
        return
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--guardar-credencial", action="store_true",
                    help="guardar usuario y clave cifrados en esta maquina (una sola vez) y salir")
    # Obligatoria a proposito: una prueba no debe pisar por descuido los CSV de produccion.
    ap.add_argument("--salida", required=True,
                    help=r"carpeta de los CSV. Produccion: E:\BASES\SISTEMAS\Apps\ReporteClientes")
    ap.add_argument("--solo", default="", help="solo estas carteras, separadas por coma (pruebas)")
    ap.add_argument("--simultaneos", type=int, default=4, help="informes en curso a la vez")
    a = ap.parse_args()

    solo = {x.strip().lower() for x in a.solo.split(",") if x.strip()}
    carteras = [c for c in CARTERAS if not solo or c.lower() in solo]
    if not carteras:
        raise SystemExit(f"Ninguna cartera coincide con --solo. Disponibles: {', '.join(CARTERAS)}")

    salida = Path(a.salida)
    (salida / "_descarga").mkdir(parents=True, exist_ok=True)
    for viejo in (salida / "_descarga").glob("*.part"):
        viejo.unlink()

    hoy = dt.date.today()
    mes = (hoy.replace(day=1).isoformat(),
           hoy.replace(day=calendar.monthrange(hoy.year, hoy.month)[1]).isoformat())
    log(f"{len(carteras)} cartera(s), del {mes[0]} al {mes[1]}, activos -> {salida}")

    s = Sesion()
    usuario, clave = credenciales()
    iniciar_sesion(s, usuario, clave, carteras[0])
    del clave

    t0 = time.time()
    candado = threading.Lock()
    with concurrent.futures.ThreadPoolExecutor(max_workers=max(1, a.simultaneos)) as ex:
        resultados = list(ex.map(lambda c: descargar_cartera(s, c, salida, mes, candado), carteras))

    fallidas = [r for r in resultados if not r["ok"]]
    informe = {"fecha": dt.datetime.now().isoformat(timespec="seconds"), "segundos": round(time.time() - t0, 1),
               "carteras": resultados, "fallidas": [r["cartera"] for r in fallidas]}
    (salida / "_descarga" / "resumen_descarga.json").write_text(
        json.dumps(informe, ensure_ascii=False, indent=2), encoding="utf-8")

    print()
    for r in resultados:
        print(f"  {r['cartera']:22s} {'OK ' + str(r['mb']) + ' MB' if r['ok'] else 'ERROR ' + r['error']}  ({r['segundos']}s)")
    print(f"TOTAL {len(resultados) - len(fallidas)}/{len(resultados)} en {informe['segundos']}s")
    if fallidas:
        print("ATENCION: estas carteras conservan el CSV ANTERIOR: " + ", ".join(informe["fallidas"]))
    sys.exit(1 if fallidas else 0)


if __name__ == "__main__":
    main()
