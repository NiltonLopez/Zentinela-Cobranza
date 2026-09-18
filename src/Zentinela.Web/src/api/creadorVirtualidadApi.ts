import api from './authApi';
import type {
  ValidarArchivoSmsResponse, CarteraListaNegra, AsesorEstrategia, ContactoAsesor,
  CatalogoMensajesResponse, ResultadoGeneracion, EntradaSms,
} from '../types/creadorVirtualidad';

const BASE = '/api/creador-virtualidad/sms';

type Progreso = (pct: number) => void;

function conProgreso(onProgreso?: Progreso) {
  return {
    onUploadProgress: (e: { loaded: number; total?: number }) => {
      if (onProgreso && e.total) onProgreso(Math.round((100 * e.loaded) / e.total));
    },
  };
}

/**
 * El archivo viaja completo en cada paso (ADR-031/037): re-subirlo es más barato que bajar
 * 200k filas al navegador. Las carteras van como clave repetida (List<string> en el
 * controller); los objetos van como JSON string porque multipart no anida.
 */
function formBase(entrada: EntradaSms): FormData {
  const fd = new FormData();
  fd.append('archivo', entrada.archivo);
  if (entrada.liderID != null) fd.append('liderID', String(entrada.liderID));
  entrada.carterasListaNegra.forEach(c => fd.append('carterasListaNegra', c));
  if (entrada.archivoListaNegra) fd.append('archivoListaNegra', entrada.archivoListaNegra);
  return fd;
}

function descargarBlob(data: Blob, nombre: string): void {
  const url  = URL.createObjectURL(data);
  const link = document.createElement('a');
  link.href = url; link.download = nombre; link.click();
  setTimeout(() => URL.revokeObjectURL(url), 5000);
}

// ── Apoyo ───────────────────────────────────────────────────────────────────

export const descargarPlantillaSms = async (): Promise<void> => {
  const { data } = await api.get(`${BASE}/plantilla`, { responseType: 'blob' });
  descargarBlob(data as Blob, 'plantilla_sms.xlsx');
};

export const obtenerCarterasListaNegra = async (): Promise<CarteraListaNegra[]> => {
  const { data } = await api.get<CarteraListaNegra[]>(`${BASE}/lista-negra/carteras`);
  return data;
};

export const obtenerAsesoresDelLider = async (liderID: number | null): Promise<AsesorEstrategia[]> => {
  const { data } = await api.get<AsesorEstrategia[]>(`${BASE}/asesores`, {
    params: liderID != null ? { liderID } : {},
  });
  return data;
};

export const obtenerAgentesActivos = async (): Promise<AsesorEstrategia[]> => {
  const { data } = await api.get<AsesorEstrategia[]>(`${BASE}/agentes-activos`);
  return data;
};

export const guardarContactosAsesores = async (contactos: ContactoAsesor[]): Promise<number> => {
  const { data } = await api.put<{ actualizados: number }>(`${BASE}/asesores/contactos`, contactos);
  return data.actualizados;
};

// ── Pasos ───────────────────────────────────────────────────────────────────

export const validarArchivoSms = async (entrada: EntradaSms, onProgreso?: Progreso): Promise<ValidarArchivoSmsResponse> => {
  const { data } = await api.post<ValidarArchivoSmsResponse>(`${BASE}/validar`, formBase(entrada), conProgreso(onProgreso));
  return data;
};

export const obtenerMensajesDisponibles = async (
  entrada: EntradaSms, huella: string, links: string[], onProgreso?: Progreso,
): Promise<CatalogoMensajesResponse> => {
  const fd = formBase(entrada);
  fd.append('huella', huella);
  fd.append('links', JSON.stringify(links));
  const { data } = await api.post<CatalogoMensajesResponse>(`${BASE}/mensajes-disponibles`, fd, conProgreso(onProgreso));
  return data;
};

export const generarEstrategiaSms = async (
  entrada: EntradaSms, huella: string, mensajeAutorizadoID: number, asesores: ContactoAsesor[], onProgreso?: Progreso,
): Promise<ResultadoGeneracion> => {
  const fd = formBase(entrada);
  fd.append('huella', huella);
  fd.append('mensajeAutorizadoID', String(mensajeAutorizadoID));
  fd.append('asesores', JSON.stringify(asesores));

  const respuesta = await api.post(`${BASE}/generar`, fd, { ...conProgreso(onProgreso), responseType: 'blob' });

  const disposition: string = respuesta.headers['content-disposition'] ?? '';
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(disposition);
  const nombreArchivo = match ? decodeURIComponent(match[1]) : 'estrategia_sms.xlsx';

  descargarBlob(respuesta.data as Blob, nombreArchivo);

  return {
    estrategiaID:      Number(respuesta.headers['x-estrategia-id'] ?? 0),
    totalGenerados:    Number(respuesta.headers['x-total-generados'] ?? 0),
    excluidosLongitud: Number(respuesta.headers['x-excluidos-longitud'] ?? 0),
    nombreArchivo,
  };
};

/** Con responseType 'blob' el JSON de error llega como Blob: hay que leerlo para mostrarlo. */
export async function mensajeDeErrorBlob(err: unknown, porDefecto: string): Promise<string> {
  const e = err as { response?: { status?: number; data?: unknown } };
  const data = e.response?.data;
  try {
    if (data instanceof Blob) {
      const texto = await data.text();
      const json = JSON.parse(texto) as { mensaje?: string };
      if (json.mensaje) return json.mensaje;
    } else if (data && typeof data === 'object' && 'mensaje' in data) {
      return String((data as { mensaje: unknown }).mensaje);
    }
  } catch { /* cae al mensaje por defecto */ }
  return porDefecto;
}
