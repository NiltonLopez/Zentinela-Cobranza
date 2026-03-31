import { render, screen } from '@testing-library/react';
import { MemoryRouter, Routes, Route } from 'react-router-dom';
import { RutaProtegida } from '../../components/RutaProtegida';
import { AuthContext } from '../../context/AuthContext';
import type { Usuario } from '../../types/auth';

interface MockAuthProps {
  autenticado: boolean;
  usuario?: Usuario | null;
  children: React.ReactNode;
}

// Proveedor de contexto de autenticación controlado para tests
function MockAuthProvider({ autenticado, usuario = null, children }: MockAuthProps) {
  return (
    <AuthContext.Provider value={{
      usuario,
      token: autenticado ? 'mock-token' : null,
      iniciarSesion: () => {},
      cerrarSesion: () => {},
      estaAutenticado: autenticado,
    }}>
      {children}
    </AuthContext.Provider>
  );
}

describe('RutaProtegida', () => {
  it('redirige a /login cuando no está autenticado', () => {
    render(
      <MockAuthProvider autenticado={false}>
        <MemoryRouter initialEntries={['/bases']}>
          <Routes>
            <Route path="/bases" element={<RutaProtegida><p>Contenido</p></RutaProtegida>} />
            <Route path="/login" element={<p>Página login</p>} />
          </Routes>
        </MemoryRouter>
      </MockAuthProvider>
    );
    expect(screen.getByText('Página login')).toBeInTheDocument();
    expect(screen.queryByText('Contenido')).not.toBeInTheDocument();
  });

  it('muestra el contenido cuando está autenticado', () => {
    const usuario: Usuario = {
      email: 'a@b.com',
      nombreCompleto: 'Test',
      rol: 'Admin',
      liderID: null,
    };
    render(
      <MockAuthProvider autenticado={true} usuario={usuario}>
        <MemoryRouter initialEntries={['/bases']}>
          <Routes>
            <Route path="/bases" element={<RutaProtegida><p>Contenido protegido</p></RutaProtegida>} />
          </Routes>
        </MemoryRouter>
      </MockAuthProvider>
    );
    expect(screen.getByText('Contenido protegido')).toBeInTheDocument();
  });

  it('redirige a /sin-permiso con rol no permitido', () => {
    const usuario: Usuario = {
      email: 'a@b.com',
      nombreCompleto: 'Test',
      rol: 'Asesor',
      liderID: null,
    };
    render(
      <MockAuthProvider autenticado={true} usuario={usuario}>
        <MemoryRouter initialEntries={['/bases']}>
          <Routes>
            <Route path="/bases" element={
              <RutaProtegida rolesPermitidos={['Admin', 'Líder']}>
                <p>Solo admins</p>
              </RutaProtegida>
            } />
            <Route path="/sin-permiso" element={<p>Sin permiso</p>} />
          </Routes>
        </MemoryRouter>
      </MockAuthProvider>
    );
    expect(screen.getByText('Sin permiso')).toBeInTheDocument();
  });
});
