import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { obtenerFotoPersona, type TamFoto } from '../api/personasApi';

// ── objectURL compartidos por Blob ───────────────────────────────────────────
// react-query guarda el Blob (no el objectURL: un string no se puede "liberar" cuando la query
// sale del caché). Cada Blob tiene UN objectURL, compartido por todos los avatares que lo usan
// (un ranking puede mostrar a la misma persona en el podio y en la tabla), con conteo de
// usuarios. Cuando el último se desmonta, el revoke se difiere un tick: StrictMode (y un
// remontaje rápido) desmonta y vuelve a montar en el mismo tick, y revocar de inmediato dejaría
// la imagen rota.

interface EntradaUrl { url: string; usuarios: number; revocar?: number }

const urls = new Map<Blob, EntradaUrl>();

function urlDe(blob: Blob): string {
  let entrada = urls.get(blob);
  if (!entrada) {
    entrada = { url: URL.createObjectURL(blob), usuarios: 0 };
    urls.set(blob, entrada);
  }
  return entrada.url;
}

function tomar(blob: Blob) {
  const entrada = urls.get(blob);
  if (!entrada) return;
  entrada.usuarios++;
  if (entrada.revocar !== undefined) {
    window.clearTimeout(entrada.revocar);
    entrada.revocar = undefined;
  }
}

function soltar(blob: Blob) {
  const entrada = urls.get(blob);
  if (!entrada) return;
  entrada.usuarios--;
  if (entrada.usuarios > 0) return;
  entrada.revocar = window.setTimeout(() => {
    if (entrada.usuarios > 0) return;
    URL.revokeObjectURL(entrada.url);
    urls.delete(blob);
  }, 0);
}

/**
 * Foto de una persona (líder o asesor) como objectURL listo para un <img>. null mientras carga,
 * si no tiene foto (404), si falla o si no hay documento.
 *
 * - Una query por (documento, tam); staleTime Infinity: la foto no cambia en la sesión.
 * - gcTime 1 h: al salir del informe el blob se libera al rato.
 * - 404 no es error (el API devuelve null) → no hay reintentos; un error real reintenta una vez.
 */
export function useFotoPersona(documento: number | null | undefined, tam: TamFoto = 160) {
  const habilitada = documento != null && documento > 0;

  const { data: blob, isLoading } = useQuery({
    queryKey: ['foto-persona', documento, tam],
    queryFn: ({ signal }) => obtenerFotoPersona(documento as number, tam, signal),
    enabled: habilitada,
    staleTime: Infinity,
    gcTime: 60 * 60 * 1000,
    retry: 1,
  });

  const url = blob ? urlDe(blob) : null;

  useEffect(() => {
    if (!blob) return;
    tomar(blob);
    return () => soltar(blob);
  }, [blob]);

  return { url, cargando: habilitada && isLoading };
}
