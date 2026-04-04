import { Navigate, useLocation } from 'react-router-dom';
import { useAuth } from '../context/AuthContext';
import type { Rol } from '../types/auth';
import type { ReactNode } from 'react';

interface Props {
  children: ReactNode;
  rolesPermitidos?: Rol[];
}

export function RutaProtegida({ children, rolesPermitidos }: Props) {
  const { estaAutenticado, usuario } = useAuth();
  const location = useLocation();

  if (!estaAutenticado) {
    return <Navigate to="/login" replace />;
  }

  // Si el usuario debe cambiar su contraseña, solo puede ir a /cambiar-password
  if (usuario?.requiereCambioPassword && location.pathname !== '/cambiar-password') {
    return <Navigate to="/cambiar-password" replace />;
  }

  if (rolesPermitidos && usuario && !rolesPermitidos.includes(usuario.rol as Rol)) {
    return <Navigate to="/sin-permiso" replace />;
  }

  return <>{children}</>;
}
