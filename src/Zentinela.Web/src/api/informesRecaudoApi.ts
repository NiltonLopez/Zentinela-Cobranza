import api from './authApi';
import type {
  InformeAgentesResponse,
  InformeLideresResponse,
  InformeCarterasResponse,
  InformeAgenciasExternasResponse,
  CategoriaAgenteDto,
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

export const getInformeCarteraBaseGeneral = async (
  carteraID: number,
  año: number,
  mes: number,
): Promise<InformeLideresResponse> => {
  const { data } = await api.get<InformeLideresResponse>(
    '/api/informes/recaudo/cartera-base-general',
    { params: { carteraID, año, mes } },
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

export const getInformeAgenciasExternas = async (
  año: number,
  mes: number,
): Promise<InformeAgenciasExternasResponse> => {
  const { data } = await api.get<InformeAgenciasExternasResponse>(
    '/api/informes/recaudo/agencias-externas',
    { params: { año, mes } },
  );
  return data;
};

// Solo Admin/Director — categoría de ranking (Diamante/Oro/Plata/Bronce) de todos los asesores.
export const getCategoriasAgentes = async (
  año: number,
  mes: number,
): Promise<CategoriaAgenteDto[]> => {
  const { data } = await api.get<CategoriaAgenteDto[]>(
    '/api/informes/recaudo/categorias',
    { params: { año, mes } },
  );
  return data;
};
