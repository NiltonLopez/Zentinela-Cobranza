import type { Rol } from '../types/auth';

export type ModuloKey = 'home' | 'cartera' | 'localizacion' | 'asignaciones' | 'consulta';

export interface SubModulo {
  key: string;
  label: string;
  path: string;
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
      { key: 'dashboard', label: 'Dashboard', path: '/home' },
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
    key: 'localizacion',
    label: 'Localización',
    rolesPermitidos: ['Admin', 'Líder', 'Director'],
    defaultPath: '/localizacion/arlsura',
    subModulos: [
      { key: 'arlsura', label: 'ARL Sura', path: '/localizacion/arlsura' },
      { key: 'cajacopi', label: 'Cajacopi', path: '/localizacion/cajacopi' },
      { key: 'camara', label: 'Cámara de Comercio', path: '/localizacion/camara' },
      { key: 'fosyga', label: 'Fosyga', path: '/localizacion/fosyga' },
      { key: 'runt', label: 'RUNT', path: '/localizacion/runt' },
      { key: 'simit', label: 'SIMIT', path: '/localizacion/simit' },
    ],
  },
  {
    key: 'asignaciones',
    label: 'Asignaciones',
    rolesPermitidos: ['Admin'],
    defaultPath: '/asignaciones/gestion',
    subModulos: [
      { key: 'gestion', label: 'Gestión de Asignaciones', path: '/asignaciones/gestion' },
      { key: 'carteras', label: 'Gestión de Carteras', path: '/asignaciones/carteras' },
      { key: 'personal', label: 'Gestión de Personal', path: '/asignaciones/personal' },
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
  if (pathname.startsWith('/localizacion')) return 'localizacion';
  if (pathname.startsWith('/asignaciones')) return 'asignaciones';
  if (pathname.startsWith('/consulta')) return 'consulta';
  return 'home';
}
