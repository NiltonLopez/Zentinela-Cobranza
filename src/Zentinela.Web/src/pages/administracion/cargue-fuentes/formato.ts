// Utilidades sin JSX del sub-módulo "Cargue de Fuentes de Datos". Van aparte de los
// componentes para no romper el fast-refresh (react-refresh/only-export-components).
import { formatearFecha, formatearFechaHora } from '../../../utils/formatters';

export const num = (v: number) => v.toLocaleString('es-CO');

type ErrorAxios = { response?: { status?: number; data?: { mensaje?: string } } };

/** Mensaje legible de un error de axios: prioriza el `mensaje` que manda el backend. */
export function mensajeDeError(err: unknown, porDefecto: string, si409?: string): string {
  const e = err as ErrorAxios;
  const mensaje = e?.response?.data?.mensaje;
  if (mensaje) return mensaje;
  if (e?.response?.status === 409 && si409) return si409;
  return porDefecto;
}

export const es404 = (err: unknown) => (err as ErrorAxios)?.response?.status === 404;

/**
 * Fecha del servidor legible. Una fecha sola ('2026-09-23') se trata como día local (si se
 * pasara a Date directo, JS la toma como UTC y en Colombia saldría el día anterior).
 */
export function fechaLegible(iso: string | null | undefined): string {
  if (!iso) return '—';
  if (/^\d{4}-\d{2}-\d{2}$/.test(iso)) return formatearFecha(iso);
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : formatearFechaHora(iso);
}

/** 83 → "1 min 23 s"; 3725 → "1 h 2 min". */
export function duracionLegible(segundos: number): string {
  const s = Math.max(0, Math.round(segundos));
  if (s < 60) return `${s} s`;
  const h = Math.floor(s / 3600);
  const m = Math.floor((s % 3600) / 60);
  return h > 0 ? `${h} h ${m} min` : `${m} min ${s % 60} s`;
}

/** Tamaño de archivo: KB hasta 1 MB, luego MB con un decimal. */
export const tamanoLegible = (kb: number) =>
  kb >= 1024 ? `${(kb / 1024).toLocaleString('es-CO', { maximumFractionDigits: 1 })} MB` : `${num(Math.round(kb))} KB`;
