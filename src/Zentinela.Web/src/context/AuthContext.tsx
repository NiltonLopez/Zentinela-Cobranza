import { createContext, useContext, useState, useCallback, type ReactNode } from 'react';
import type { Usuario, LoginResponse } from '../types/auth';

interface AuthContextType {
  usuario: Usuario | null;
  token: string | null;
  iniciarSesion: (respuesta: LoginResponse) => void;
  cerrarSesion: () => void;
  estaAutenticado: boolean;
}

export const AuthContext = createContext<AuthContextType | null>(null);

const TOKEN_KEY = 'zentinela_token';
const USUARIO_KEY = 'zentinela_usuario';

function cargarUsuarioGuardado(): { usuario: Usuario | null; token: string | null } {
  try {
    const token = localStorage.getItem(TOKEN_KEY);
    const raw = localStorage.getItem(USUARIO_KEY);
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
      email: '',
      nombreCompleto: respuesta.nombreCompleto,
      rol: respuesta.rol,
      liderID: respuesta.liderID,
    };
    localStorage.setItem(TOKEN_KEY, respuesta.token);
    localStorage.setItem(USUARIO_KEY, JSON.stringify(u));
    setToken(respuesta.token);
    setUsuario(u);
  }, []);

  const cerrarSesion = useCallback(() => {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(USUARIO_KEY);
    setToken(null);
    setUsuario(null);
  }, []);

  return (
    <AuthContext.Provider value={{ usuario, token, iniciarSesion, cerrarSesion, estaAutenticado: !!token }}>
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth(): AuthContextType {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error('useAuth debe usarse dentro de AuthProvider');
  return ctx;
}
