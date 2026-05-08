import { useState, type FormEvent } from 'react';
import { Link } from 'react-router-dom';
import { registrarUsuario, type RegistroResponse } from '../api/registroApi';
import { ZButton } from '../components/ZButton';

export function RegisterPage() {
  const [cedula, setCedula] = useState('');
  const [error, setError] = useState('');
  const [resultado, setResultado] = useState<RegistroResponse | null>(null);
  const [cargando, setCargando] = useState(false);

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
    setResultado(null);
    setCargando(true);

    try {
      const r = await registrarUsuario({ cedula: Number(cedula) });
      setResultado(r);
      setCedula('');
    } catch (err: unknown) {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje;
      setError(msg ?? 'Ocurrió un error al registrar el usuario.');
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
        {/* Encabezado */}
        <div style={{ marginBottom: '28px' }}>
          <p style={{ fontSize: '16px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
            Crear cuenta
          </p>
          <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
            Ingresa tu cédula y tu usuario de ControlNext
          </p>
        </div>

        <form onSubmit={handleSubmit} style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <label style={{ fontSize: '12px', fontWeight: 500, color: 'var(--z-text-muted)' }}>
              Número de cédula
            </label>
            <input
              type="number"
              value={cedula}
              onChange={(e) => setCedula(e.target.value)}
              required
              placeholder="1234567890"
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

          {resultado && (
            <div style={{
              fontSize: '12px',
              color: '#15803d',
              background: '#f0fdf4',
              border: '0.5px solid #bbf7d0',
              borderRadius: '6px',
              padding: '12px 14px',
              display: 'flex',
              flexDirection: 'column',
              gap: '8px',
            }}>
              <p style={{ margin: 0 }}>
                <strong>{resultado.nombreCompleto}</strong> registrado correctamente como <strong>{resultado.rol}</strong>.
              </p>
              <p style={{ margin: 0 }}>
                Tu usuario es:{' '}
                <code style={{
                  fontFamily: 'ui-monospace, SFMono-Regular, Menlo, monospace',
                  fontSize: '12px',
                  background: '#dcfce7',
                  padding: '2px 6px',
                  borderRadius: '4px',
                  color: '#14532d',
                }}>{resultado.usuario}</code>
              </p>
              <p style={{ margin: 0, color: '#166534' }}>
                Se compone de tu <strong>primer nombre</strong>, un punto, tu <strong>primer apellido</strong> y los <strong>últimos 3 dígitos de tu cédula</strong>.
              </p>
              <p style={{ margin: 0, color: '#166534' }}>
                Inicia sesión con tu usuario y, como contraseña, tu número de cédula. Te pediremos cambiarla en el primer ingreso.
              </p>
            </div>
          )}

          <ZButton
            type="submit"
            variant="primary"
            disabled={cargando}
            style={{ width: '100%', justifyContent: 'center', padding: '10px', marginTop: '4px' }}
          >
            {cargando ? 'Registrando...' : 'Crear cuenta'}
          </ZButton>
        </form>

        <p style={{ fontSize: '12px', color: 'var(--z-text-muted)', textAlign: 'center', marginTop: '20px' }}>
          ¿Ya tienes cuenta?{' '}
          <Link
            to="/login"
            style={{ color: 'var(--z-brand)', textDecoration: 'none', fontWeight: 500 }}
          >
            Iniciar sesión
          </Link>
        </p>
      </div>
    </div>
  );
}
