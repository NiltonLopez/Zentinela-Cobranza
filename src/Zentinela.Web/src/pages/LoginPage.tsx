import { useState, type FormEvent } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { login } from '../api/authApi';
import { useAuth } from '../context/AuthContext';
import { ZButton } from '../components/ZButton';
import ZentinelaLogo from '../components/ZentinelaLogo';
import { rutaInicialPorRol } from '../config/navigation';

export function LoginPage() {
  const [usuario, setUsuario] = useState('');
  const [password, setPassword] = useState('');
  const [error, setError] = useState('');
  const [cargando, setCargando] = useState(false);
  const { iniciarSesion } = useAuth();
  const navigate = useNavigate();

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');
    setCargando(true);
    try {
      const respuesta = await login({ usuario, password });
      iniciarSesion(respuesta);
      navigate(rutaInicialPorRol(respuesta.rol));
    } catch {
      setError('Usuario o contraseña incorrectos.');
    } finally {
      setCargando(false);
    }
  };

  const inputStyle: React.CSSProperties = {
    width: '100%',
    padding: '9px 12px',
    fontSize: '13px',
    border: '1px solid var(--z-border)',
    borderRadius: '6px',
    background: 'var(--z-bg-surface)',
    color: 'var(--z-text-primary)',
    outline: 'none',
    transition: 'border-color 0.15s',
  };

  return (
    <div style={{
      minHeight: '100vh',
      background: 'var(--z-bg-page)',
      display: 'flex',
      alignItems: 'center',
      justifyContent: 'center',
    }}>
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '12px',
        padding: '40px',
        width: '100%',
        maxWidth: '360px',
        boxShadow: '0 4px 24px rgba(0,0,0,0.06)',
      }}>
        {/* Logo */}
        <div style={{ display: 'flex', alignItems: 'center', gap: '10px', marginBottom: '28px' }}>
          <ZentinelaLogo size={40} />
          <div>
            <p style={{ fontSize: '16px', fontWeight: 600, color: 'var(--z-text-primary)', lineHeight: 1.2 }}>Zentinela</p>
            <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Gestión de cobranza</p>
          </div>
        </div>

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <label style={{ fontSize: '12px', fontWeight: 500, color: 'var(--z-text-muted)' }}>
              Usuario
            </label>
            <input
              type="text"
              value={usuario}
              onChange={(e) => setUsuario(e.target.value)}
              required
              placeholder="nombre.apellido123"
              style={inputStyle}
              onFocus={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-brand)'; }}
              onBlur={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-border)'; }}
            />
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <label style={{ fontSize: '12px', fontWeight: 500, color: 'var(--z-text-muted)' }}>
              Contraseña
            </label>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              required
              style={inputStyle}
              onFocus={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-brand)'; }}
              onBlur={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-border)'; }}
            />
          </div>

          {error && (
            <p style={{
              fontSize: '12px',
              color: '#dc2626',
              background: '#fff5f5',
              border: '0.5px solid #fecaca',
              borderRadius: '6px',
              padding: '10px 12px',
            }}>
              {error}
            </p>
          )}

          <ZButton
            type="submit"
            variant="primary"
            disabled={cargando}
            style={{ width: '100%', justifyContent: 'center', padding: '10px', marginTop: '4px' }}
          >
            {cargando ? 'Ingresando...' : 'Ingresar'}
          </ZButton>
        </form>

        <p style={{ fontSize: '12px', color: 'var(--z-text-muted)', textAlign: 'center', marginTop: '20px' }}>
          ¿Primera vez?{' '}
          <Link
            to="/registro"
            style={{ color: 'var(--z-brand)', textDecoration: 'none', fontWeight: 500 }}
          >
            Crear cuenta
          </Link>
        </p>
      </div>
    </div>
  );
}
