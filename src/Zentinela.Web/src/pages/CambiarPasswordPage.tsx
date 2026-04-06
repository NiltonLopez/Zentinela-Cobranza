import { useState, type FormEvent } from 'react';
import { useNavigate } from 'react-router-dom';
import { cambiarPassword } from '../api/authApi';
import { useAuth } from '../context/AuthContext';
import { ZButton } from '../components/ZButton';

export function CambiarPasswordPage() {
  const { usuario, actualizarUsuario, cerrarSesion } = useAuth();
  const [passwordActual, setPasswordActual] = useState('');
  const [nuevoPassword, setNuevoPassword] = useState('');
  const [confirmar, setConfirmar] = useState('');
  const [error, setError] = useState('');
  const [cargando, setCargando] = useState(false);
  const navigate = useNavigate();

  const esPrimerLogin = usuario?.requiereCambioPassword ?? false;

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
    boxSizing: 'border-box',
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    setError('');

    if (nuevoPassword !== confirmar) {
      setError('Las contraseñas nuevas no coinciden.');
      return;
    }

    if (nuevoPassword.length < 6) {
      setError('La nueva contraseña debe tener al menos 6 caracteres.');
      return;
    }

    setCargando(true);
    try {
      await cambiarPassword({ passwordActual, nuevoPassword });
      // Desactiva el flag localmente sin forzar un nuevo login
      actualizarUsuario({ requiereCambioPassword: false });
      navigate('/home');
    } catch (err: unknown) {
      const errores = (err as { response?: { data?: { errores?: string[] } } })
        ?.response?.data?.errores;
      setError(errores?.join(', ') ?? 'No se pudo cambiar la contraseña.');
    } finally {
      setCargando(false);
    }
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
        maxWidth: '400px',
        boxShadow: '0 4px 24px rgba(0,0,0,0.06)',
      }}>
        <div style={{ marginBottom: '28px' }}>
          <p style={{ fontSize: '16px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
            {esPrimerLogin ? 'Elige tu contraseña' : 'Cambiar contraseña'}
          </p>
          <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
            {esPrimerLogin
              ? 'Es tu primer ingreso. Tu contraseña actual es tu número de cédula.'
              : `Sesión: ${usuario?.nombreCompleto}`}
          </p>
        </div>

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <label style={{ fontSize: '12px', fontWeight: 500, color: 'var(--z-text-muted)' }}>
              {esPrimerLogin ? 'Contraseña actual (tu cédula)' : 'Contraseña actual'}
            </label>
            <input
              type="password"
              value={passwordActual}
              onChange={(e) => setPasswordActual(e.target.value)}
              required
              style={inputStyle}
              onFocus={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-brand)'; }}
              onBlur={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-border)'; }}
            />
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <label style={{ fontSize: '12px', fontWeight: 500, color: 'var(--z-text-muted)' }}>
              Nueva contraseña
            </label>
            <input
              type="password"
              value={nuevoPassword}
              onChange={(e) => setNuevoPassword(e.target.value)}
              required
              style={inputStyle}
              onFocus={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-brand)'; }}
              onBlur={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-border)'; }}
            />
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <label style={{ fontSize: '12px', fontWeight: 500, color: 'var(--z-text-muted)' }}>
              Confirmar nueva contraseña
            </label>
            <input
              type="password"
              value={confirmar}
              onChange={(e) => setConfirmar(e.target.value)}
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
            {cargando ? 'Guardando...' : 'Guardar contraseña'}
          </ZButton>

          {!esPrimerLogin && (
            <ZButton
              type="button"
              variant="ghost"
              onClick={() => navigate(-1)}
              style={{ width: '100%', justifyContent: 'center' }}
            >
              Cancelar
            </ZButton>
          )}

          {esPrimerLogin && (
            <button
              type="button"
              onClick={cerrarSesion}
              style={{
                background: 'none', border: 'none', cursor: 'pointer',
                fontSize: '12px', color: 'var(--z-text-muted)',
                textAlign: 'center', marginTop: '4px',
              }}
            >
              Cerrar sesión
            </button>
          )}
        </form>
      </div>
    </div>
  );
}
