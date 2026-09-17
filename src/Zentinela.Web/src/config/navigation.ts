import type { Rol } from '../types/auth';

export type ModuloKey = 'home' | 'cartera' | 'informes' | 'administracion' | 'consultas';

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

// Líder Externo: solo ve el módulo Cartera (Calificador / Creador / Comparador).
// Asesor Externo: solo ve Consulta Integrada (dentro del módulo Consultas).
// Ninguno ve Home/Dashboard, Informes, Administración ni Rastreo de Líneas.
export const MODULOS: Modulo[] = [
  {
    key: 'home',
    label: 'Home',
    rolesPermitidos: ['Admin', 'Líder', 'Director', 'Asesor'],
    defaultPath: '/home',
    subModulos: [
      { key: 'dashboard',                 label: 'Dashboard',          path: '/home' },
      { key: 'pagos-confirmados',         label: 'Pagos confirmados',  path: '/home/pagos',          rolesPermitidos: ['Asesor'] },
      { key: 'pagos-confirmados-lider',   label: 'Pagos confirmados',  path: '/home/pagos-lider',    rolesPermitidos: ['Líder'] },
      { key: 'pagos-confirmados-director', label: 'Pagos confirmados', path: '/home/pagos-director', rolesPermitidos: ['Director', 'Admin'] },
    ],
  },
  {
    key: 'cartera',
    label: 'Cartera',
    rolesPermitidos: ['Admin', 'Líder', 'Director', 'Líder Externo'],
    defaultPath: '/cartera/creador',
    subModulos: [
      { key: 'creador', label: 'Creador Zentinela', path: '/cartera/creador' },
      { key: 'calificador', label: 'Calificador de Cartera',  path: '/cartera/calificador' },
      { key: 'comparador',  label: 'Comparador de Carteras', path: '/cartera/comparador'  },
    ],
  },
  {
    key: 'informes',
    label: 'Informes',
    rolesPermitidos: ['Admin', 'Líder', 'Director'],
    defaultPath: '/informes/recaudo',
    subModulos: [
      { key: 'virtualidad',         label: 'Informes de Virtualidad', path: '/informes/virtualidad', rolesPermitidos: ['Admin', 'Líder', 'Director'] },
      { key: 'recaudo',             label: 'Informes de Recaudo',     path: '/informes/recaudo',     rolesPermitidos: ['Admin', 'Líder', 'Director'] },
      { key: 'agencias-externas',   label: 'Agencias Externas',       path: '/informes/agencias-externas', rolesPermitidos: ['Admin', 'Líder', 'Director'] },
      { key: 'auditoria-gestiones', label: 'Auditoría de Gestiones',  path: '/informes/auditoria-gestiones', rolesPermitidos: ['Admin', 'Director'] },
      { key: 'generales',           label: 'Informes Generales',      path: '/informes/generales',           rolesPermitidos: ['Admin', 'Director'] },
    ],
  },
  {
    key: 'administracion',
    label: 'Administración',
    rolesPermitidos: ['Admin', 'Auxiliar de Sistemas'],
    defaultPath: '/administracion/gestion',
    subModulos: [
      { key: 'gestion',  label: 'Gestión de Asignaciones',       path: '/administracion/gestion' },
      { key: 'agentes',  label: 'Gestión de Agentes',            path: '/administracion/agentes' },
      { key: 'carteras', label: 'Gestión de Líderes y Carteras', path: '/administracion/carteras' },
      { key: 'personal', label: 'Gestión de Personal',           path: '/administracion/personal' },
      { key: 'reportes', label: 'Cargue de Reportes',            path: '/administracion/reportes' },
      { key: 'pagos',    label: 'Cargue de Pagos',               path: '/administracion/pagos' },
      { key: 'fuentes-datos', label: 'Cargue de Fuentes de Datos', path: '/administracion/fuentes-datos' },
      { key: 'historico-virtualidad', label: 'Gestión de Histórico de Virtualidad', path: '/administracion/historico-virtualidad' },
      { key: 'metas',    label: 'Asignación de Metas',            path: '/administracion/metas',  rolesPermitidos: ['Admin'] },
    ],
  },
  {
    key: 'consultas',
    label: 'Consultas',
    rolesPermitidos: ['Admin', 'Líder', 'Director', 'Asesor', 'Líder Externo', 'Asesor Externo'],
    defaultPath: '/consultas/integrada',
    subModulos: [
      { key: 'integrada', label: 'Consulta Integrada', path: '/consultas/integrada' },
      { key: 'rastreo',   label: 'Rastreo de Líneas',  path: '/consultas/rastreo',
        rolesPermitidos: ['Admin', 'Líder', 'Director', 'Asesor', 'Líder Externo'] },
    ],
  },
];

/// Devuelve la ruta inicial post-login según el rol.
/// Los roles externos tienen caso especial (no tienen módulo Home). Para los demás
/// roles, retorna el `defaultPath` del primer módulo permitido — así, p. ej.,
/// Auxiliar de Sistemas aterriza en `/administracion/gestion` y no en `/home`
/// (al que no tiene acceso).
export function rutaInicialPorRol(rol: string | undefined): string {
  if (rol === 'Líder Externo')  return '/cartera/creador';
  if (rol === 'Asesor Externo') return '/consultas/integrada';
  const modulo = MODULOS.find(m => m.rolesPermitidos.includes(rol as Rol));
  if (!modulo) return '/home';
  // Aterrizar en el primer sub-módulo PERMITIDO (no en defaultPath fijo, que
  // podría estar restringido para el rol y rebotar a "sin acceso").
  const sub = modulo.subModulos.find(s => !s.rolesPermitidos || s.rolesPermitidos.includes(rol as Rol));
  return sub?.path ?? modulo.defaultPath;
}

// Dado un pathname, devuelve el módulo activo
export function pathToModulo(pathname: string): ModuloKey {
  if (pathname.startsWith('/cartera')) return 'cartera';
  if (pathname.startsWith('/informes')) return 'informes';
  if (pathname.startsWith('/administracion')) return 'administracion';
  // /consultas/* (nuevo) y /consulta (legacy redirect) caen al mismo módulo
  if (pathname.startsWith('/consulta')) return 'consultas';
  return 'home';
}
