import api from './authApi';
import type {
  InformeAgentesResponse,
  InformeLideresResponse,
  InformeCarterasResponse,
} from '../types/informesRecaudo';

export const getInformeAgentes = async (
  año: number,
  mes: number,
): Promise<InformeAgentesResponse> => {
  const { data } = await api.get<InformeAgentesResponse>(
    '/api/informes/recaudo/agentes',
    { params: { año, mes } },
  );
  return data;
};

export const getInformeLideres = async (
  año: number,
  mes: number,
): Promise<InformeLideresResponse> => {
  const { data } = await api.get<InformeLideresResponse>(
    '/api/informes/recaudo/lideres',
    { params: { año, mes } },
  );
  return data;
};

export const getInformeCarteras = async (
  añoA: number,
  mesA: number,
  añoB: number,
  mesB: number,
): Promise<InformeCarterasResponse> => {
  const { data } = await api.get<InformeCarterasResponse>(
    '/api/informes/recaudo/carteras',
    { params: { añoA, mesA, añoB, mesB } },
  );
  return data;
};
