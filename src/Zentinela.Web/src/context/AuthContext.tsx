import { createContext, useContext, useState, useCallback, type ReactNode } from 'react';
import type { Usuario, LoginResponse } from '../types/auth';
import { STORAGE_KEYS } from '../constants/storage';
import { CLAVE_ACTIVA as CALIFICADOR_ACTIVA } from '../pages/cartera/CalificadorCarteraPage';

interface AuthContextType {
  usuario: Usuario | null;
  token: string | null;
  iniciarSesion: (respuesta: LoginResponse) => void;
  cerrarSesion: () => void;
  actualizarUsuario: (cambios: Partial<Usuario>) => void;
  estaAutenticado: boolean;
}

export const AuthContext = createContext<AuthContextType | null>(null);

function cargarUsuarioGuardado(): { usuario: Usuario | null; token: string | null } {
  try {
    const token = localStorage.getItem(STORAGE_KEYS.TOKEN);
    const raw = localStorage.getItem(STORAGE_KEYS.USUARIO);
    if (!token || !raw) return { usuario: null, token: null };
    return { usuario: JSON.parse(raw) as Usuario, token };
  } catch {
    return { usuario: null, token: null };
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const guardado = cargarUsuarioGuardado();
  const [usuario, setUsuario] = useState<Usuario | null>(guardado.usuario);
  const [token, setToken] = useState<string | null>(guardado.token);

  const iniciarSesion = useCallback((respuesta: LoginResponse) => {
    const u: Usuario = {
      email: respuesta.email,
      nombreCompleto: respuesta.nombreCompleto,
      rol: respuesta.rol,
      liderID: respuesta.liderID,
      requiereCambioPassword: respuesta.requiereCambioPassword,
    };
    localStorage.setItem(STORAGE_KEYS.TOKEN, respuesta.token);
    localStorage.setItem(STORAGE_KEYS.USUARIO, JSON.stringify(u));
    setToken(respuesta.token);
    setUsuario(u);
  }, []);

  const cerrarSesion = useCallback(() => {
    localStorage.removeItem(STORAGE_KEYS.TOKEN);
    localStorage.removeItem(STORAGE_KEYS.USUARIO);
    // Selecciones persistidas en sessionStorage no deben sobrevivir a un cambio
    // de usuario en el mismo navegador (ej. Calificador de Cartera).
    sessionStorage.removeItem(CALIFICADOR_ACTIVA);
    setToken(null);
    setUsuario(null);
  }, []);

  // Permite actualizar campos del usuario sin cerrar sesión.
  // Usado por CambiarPasswordPage para desactivar requiereCambioPassword.
  const actualizarUsuario = useCallback((cambios: Partial<Usuario>) => {
    setUsuario(prev => {
      if (!prev) return prev;
      const actualizado = { ...prev, ...cambios };
      localStorage.setItem(STORAGE_KEYS.USUARIO, JSON.stringify(actualizado));
      return actualizado;
    });
  }, []);

  return (
    <AuthContext.Provider value={{
      usuario, token,
      iniciarSesion, cerrarSesion, actualizarUsuario,
      estaAutenticado: !!token,
    }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextType {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth debe usarse dentro de AuthProvider');
  return ctx;
}
