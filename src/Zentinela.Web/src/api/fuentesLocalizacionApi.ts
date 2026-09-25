import api from './authApi';
import type { FuenteLocalizacion } from '../types/fuentesLocalizacion';

/** Catálogo de fuentes de localización con su estado y último cargue OK (ADR-040). */
export const obtenerFuentesLocalizacion = async (): Promise<FuenteLocalizacion[]> => {
  const response = await api.get<FuenteLocalizacion[]>('/api/fuentes-localizacion');
  return response.data;
};
