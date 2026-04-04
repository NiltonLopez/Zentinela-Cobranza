import { render, screen, act } from '@testing-library/react';
import { AuthProvider, useAuth } from '../../context/AuthContext';
import type { LoginResponse } from '../../types/auth';

const MOCK_RESPONSE: LoginResponse = {
  token: 'eyJ.mock.token',
  email: 'nilton@zentinela.com',
  nombreCompleto: 'Nilton Ramírez',
  rol: 'Admin',
  liderID: null,
  expiracion: new Date(Date.now() + 3600000).toISOString(),
  requiereCambioPassword: false,
};

function ComponenteDeTest() {
  const { usuario, estaAutenticado, iniciarSesion, cerrarSesion } = useAuth();
  return (
    <div>
      <span data-testid="autenticado">{estaAutenticado ? 'sí' : 'no'}</span>
      <span data-testid="nombre">{usuario?.nombreCompleto ?? 'sin usuario'}</span>
      <span data-testid="rol">{usuario?.rol ?? 'sin rol'}</span>
      <button onClick={() => iniciarSesion(MOCK_RESPONSE)}>login</button>
      <button onClick={cerrarSesion}>logout</button>
    </div>
  );
}

describe('AuthContext', () => {
  beforeEach(() => {
    localStorage.clear();
  });

  it('inicia sin usuario autenticado', () => {
    render(<AuthProvider><ComponenteDeTest /></AuthProvider>);
    expect(screen.getByTestId('autenticado').textContent).toBe('no');
    expect(screen.getByTestId('nombre').textContent).toBe('sin usuario');
  });

  it('iniciarSesion guarda usuario y token en localStorage', () => {
    render(<AuthProvider><ComponenteDeTest /></AuthProvider>);
    act(() => { screen.getByText('login').click(); });
    expect(screen.getByTestId('autenticado').textContent).toBe('sí');
    expect(screen.getByTestId('nombre').textContent).toBe('Nilton Ramírez');
    expect(screen.getByTestId('rol').textContent).toBe('Admin');
    expect(localStorage.getItem('zentinela_token')).toBe('eyJ.mock.token');
  });

  it('cerrarSesion limpia estado y localStorage', () => {
    render(<AuthProvider><ComponenteDeTest /></AuthProvider>);
    act(() => { screen.getByText('login').click(); });
    act(() => { screen.getByText('logout').click(); });
    expect(screen.getByTestId('autenticado').textContent).toBe('no');
    expect(localStorage.getItem('zentinela_token')).toBeNull();
  });

  it('restaura sesión desde localStorage al montar', () => {
    // Pre-cargar sesión en localStorage antes de montar el componente
    localStorage.setItem('zentinela_token', 'token-guardado');
    localStorage.setItem('zentinela_usuario', JSON.stringify({
      email: '',
      nombreCompleto: 'Usuario Guardado',
      rol: 'Líder',
      liderID: 3,
      requiereCambioPassword: false,
    }));
    render(<AuthProvider><ComponenteDeTest /></AuthProvider>);
    expect(screen.getByTestId('autenticado').textContent).toBe('sí');
    expect(screen.getByTestId('nombre').textContent).toBe('Usuario Guardado');
  });
});
