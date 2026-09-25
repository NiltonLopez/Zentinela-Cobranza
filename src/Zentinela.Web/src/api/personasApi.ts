import axios from 'axios';
import api from './authApi';

/** Lados de miniatura que genera el servidor (cualquier otro se sirve como 160). */
export type TamFoto = 48 | 96 | 160 | 320;

export const TAMANOS_FOTO: readonly TamFoto[] = [48, 96, 160, 320];

/**
 * Foto (miniatura cuadrada JPEG) de un líder o asesor activo, por cédula (ADR-040).
 * Se baja como blob por axios y no con un <img src> directo porque el JWT viaja en el header
 * Authorization. 404 = no tiene foto (o no es líder/asesor activo) → null, no es un error.
 */
export const obtenerFotoPersona = async (
  documento: number,
  tam: TamFoto,
  signal?: AbortSignal,
): Promise<Blob | null> => {
  try {
    const res = await api.get<Blob>(`/api/personas/${documento}/foto`, {
      params: { tam },
      responseType: 'blob',
      signal,
    });
    return res.data;
  } catch (error) {
    if (axios.isAxiosError(error) && error.response?.status === 404) return null;
    throw error;
  }
};
