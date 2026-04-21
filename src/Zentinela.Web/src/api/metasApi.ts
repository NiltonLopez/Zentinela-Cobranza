import api from './authApi';
import type {
  EstadoMetasMesDto,
  GuardarMetaLiderRequest,
  GuardarMetaAgenteRequest,
  ResultadoMetaDto,
} from '../types/metas';

export const obtenerEstadoMetasMesActual = async (): Promise<EstadoMetasMesDto> => {
  const res = await api.get<EstadoMetasMesDto>('/api/metas/estado-mes-actual');
  return res.data;
};

export const guardarMetaLider = async (
  request: GuardarMetaLiderRequest,
): Promise<ResultadoMetaDto> => {
  const res = await api.post<ResultadoMetaDto>('/api/metas/guardar-lider', request);
  return res.data;
};

export const guardarMetaAgente = async (
  request: GuardarMetaAgenteRequest,
): Promise<ResultadoMetaDto> => {
  const res = await api.post<ResultadoMetaDto>('/api/metas/guardar-agente', request);
  return res.data;
};
