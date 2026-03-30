import { Navigate } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import type { Rol } from '../types/auth';
import type { ReactNode } from 'react';

interface Props {
  children: ReactNode;
  rolesPermitidos?: Rol[];
}

export function RutaProtegida({ children, rolesPermitidos }: Props) {
  const { estaAutenticado, usuario } = useAuth();

  if (!estaAutenticado) {
    return <Navigate to="/login" replace />;
  }

  if (rolesPermitidos && usuario && !rolesPermitidos.includes(usuario.rol as Rol)) {
    return <Navigate to="/sin-permiso" replace />;
  }

  return <>{children}</>;
}
