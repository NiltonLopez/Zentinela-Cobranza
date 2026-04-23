import type { Rol } from '../types/auth';

export type ModuloKey = 'home' | 'cartera' | 'informes' | 'administracion' | 'consulta';

export interface SubModulo {
  key: string;
  label: string;
  path: string;
  rolesPermitidos?: Rol[];
}

export interface Modulo {
  key: ModuloKey;
  label: string;
  rolesPermitidos: Rol[];
  defaultPath: string;
  subModulos: SubModulo[];
}

export const MODULOS: Modulo[] = [
  {
    key: 'home',
    label: 'Home',
    rolesPermitidos: ['Admin', 'Líder', 'Director', 'Asesor'],
    defaultPath: '/home',
    subModulos: [
      { key: 'dashboard',         label: 'Dashboard',          path: '/home' },
      { key: 'pagos-confirmados', label: 'Pagos confirmados',  path: '/home/pagos', rolesPermitidos: ['Asesor'] },
    ],
  },
  {
    key: 'cartera',
    label: 'Cartera',
    rolesPermitidos: ['Admin', 'Líder', 'Director'],
    defaultPath: '/cartera/creador',
    subModulos: [
      { key: 'creador', label: 'Creador Zentinela', path: '/cartera/creador' },
      { key: 'calificador', label: 'Calificador de Cartera', path: '/cartera/calificador' },
    ],
  },
  {
    key: 'informes',
    label: 'Informes',
    rolesPermitidos: ['Admin', 'Líder', 'Director'],
    defaultPath: '/informes/virtualidad',
    subModulos: [
      { key: 'virtualidad', label: 'Informes de Virtualidad', path: '/informes/virtualidad' },
      { key: 'recaudo',    label: 'Informes de Recaudo',     path: '/informes/recaudo'     },
    ],
  },
  {
    key: 'administracion',
    label: 'Administración',
    rolesPermitidos: ['Admin'],
    defaultPath: '/administracion/gestion',
    subModulos: [
      { key: 'gestion',  label: 'Gestión de Asignaciones',       path: '/administracion/gestion' },
      { key: 'carteras', label: 'Gestión de Líderes y Carteras', path: '/administracion/carteras' },
      { key: 'personal', label: 'Gestión de Personal',           path: '/administracion/personal' },
      { key: 'reportes', label: 'Cargue de Reportes',            path: '/administracion/reportes' },
      { key: 'pagos',    label: 'Cargue de Pagos',               path: '/administracion/pagos' },
      { key: 'metas',    label: 'Asignación de Metas',            path: '/administracion/metas' },
    ],
  },
  {
    key: 'consulta',
    label: 'Consulta Integrada',
    rolesPermitidos: ['Admin', 'Líder', 'Director', 'Asesor'],
    defaultPath: '/consulta',
    subModulos: [
      { key: 'consulta', label: 'Consulta Integrada', path: '/consulta' },
    ],
  },
];

// Dado un pathname, devuelve el módulo activo
export function pathToModulo(pathname: string): ModuloKey {
  if (pathname.startsWith('/cartera')) return 'cartera';
  if (pathname.startsWith('/informes')) return 'informes';
  if (pathname.startsWith('/administracion')) return 'administracion';
  if (pathname.startsWith('/consulta')) return 'consulta';
  return 'home';
}
