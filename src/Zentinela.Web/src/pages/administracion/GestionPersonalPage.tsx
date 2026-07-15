import { useState, useEffect, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { listarUsuarios, resetPassword, listarSupervisores, listarSupervisoresActivos, reasignarSupervisor, inactivarUsuario, obtenerSupervisorActual, cambiarUsuario, cambiarRol } from '../../api/adminApi';
import type { UsuarioAdmin, Supervisor } from '../../types/admin';

const ROLES_DISPONIBLES = [
  'Admin', 'Líder', 'Director', 'Asesor',
  'Líder Externo', 'Asesor Externo',
] as const;

// Ícono lápiz minimalista (16×16 stroke). Usa currentColor para heredar color del padre.
function IconoLapiz({ size = 13 }: { size?: number }) {
  return (
    <svg
      width={size} height={size} viewBox="0 0 16 16" fill="none"
      stroke="currentColor" strokeWidth={1.4} strokeLinecap="round" strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M11.5 2.5l2 2L5 13H3v-2l8.5-8.5z" />
    </svg>
  );
}

const BADGE_COLORES: Record<string, { bg: string; color: string }> = {
  Admin:              { bg: '#FEF0EC', color: '#F54927' },
  Líder:              { bg: '#EFF6FF', color: '#2563EB' },
  Director:           { bg: '#F0FDF4', color: '#16A34A' },
  Asesor:             { bg: '#F5F5F4', color: '#78716C' },
  // Roles externos: tonalidades más cálidas para distinguirlos visualmente
  'Líder Externo':    { bg: '#FFF7ED', color: '#C2410C' },
  'Asesor Externo':   { bg: '#FDF4FF', color: '#A21CAF' },
};

function BadgeRol({ rol }: { rol: string }) {
  const colores = BADGE_COLORES[rol] ?? { bg: '#F5F5F4', color: '#78716C' };
  return (
    <span style={{
      display: 'inline-block',
      padding: '2px 8px',
      borderRadius: '4px',
      fontSize: '11px',
      fontWeight: 600,
      background: colores.bg,
      color: colores.color,
    }}>
      {rol}
    </span>
  );
}

function ModalConfirmar({
  usuario,
  onConfirmar,
  onCancelar,
  cargando,
}: {
  usuario: UsuarioAdmin;
  onConfirmar: () => void;
  onCancelar: () => void;
  cargando: boolean;
}) {
  return (
    <div style={{
      position: 'fixed', inset: 0,
      background: 'rgba(0,0,0,0.4)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      zIndex: 100,
    }}>
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '12px',
        padding: '32px',
        maxWidth: '400px',
        width: '90%',
        boxShadow: '0 8px 32px rgba(0,0,0,0.12)',
      }}>
        <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '8px', color: 'var(--z-text-primary)' }}>
          Restablecer contraseña
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '24px', lineHeight: 1.5 }}>
          La contraseña de <strong style={{ color: 'var(--z-text-primary)' }}>{usuario.nombreCompleto}</strong> será
          restablecida a su número de cédula (<strong style={{ color: 'var(--z-text-primary)' }}>{usuario.numeroDocumento}</strong>).
          El usuario deberá cambiarla en su próximo ingreso.
        </p>
        <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
          <button
            onClick={onCancelar}
            disabled={cargando}
            style={{
              padding: '8px 16px',
              borderRadius: '6px',
              border: '1px solid var(--z-border)',
              background: 'transparent',
              color: 'var(--z-text-muted)',
              fontSize: '13px',
              cursor: 'pointer',
            }}
          >
            Cancelar
          </button>
          <button
            onClick={onConfirmar}
            disabled={cargando}
            style={{
              padding: '8px 16px',
              borderRadius: '6px',
              border: 'none',
              background: 'var(--z-brand)',
              color: 'white',
              fontSize: '13px',
              fontWeight: 600,
              cursor: cargando ? 'not-allowed' : 'pointer',
              opacity: cargando ? 0.7 : 1,
            }}
          >
            {cargando ? 'Restableciendo...' : 'Confirmar'}
          </button>
        </div>
      </div>
    </div>
  );
}

function ModalInactivar({
  usuario,
  onConfirmar,
  onCancelar,
  cargando,
}: {
  usuario: UsuarioAdmin;
  onConfirmar: () => void;
  onCancelar: () => void;
  cargando: boolean;
}) {
  return (
    <div style={{
      position: 'fixed', inset: 0,
      background: 'rgba(0,0,0,0.4)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      zIndex: 100,
    }}>
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '12px',
        padding: '32px',
        maxWidth: '400px',
        width: '90%',
        boxShadow: '0 8px 32px rgba(0,0,0,0.12)',
      }}>
        <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '8px', color: 'var(--z-text-primary)' }}>
          Inactivar usuario
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '12px', lineHeight: 1.5 }}>
          Se inactivará a <strong style={{ color: 'var(--z-text-primary)' }}>{usuario.nombreCompleto}</strong> y su cuenta será eliminada de Zentinela.
        </p>
        <div style={{
          padding: '10px 14px',
          borderRadius: '8px',
          fontSize: '12px',
          background: '#fff5f5',
          border: '0.5px solid #fecaca',
          color: '#dc2626',
          marginBottom: '24px',
          lineHeight: 1.5,
        }}>
          Esta acción no se puede deshacer. El empleado quedará inactivo en RRHH y no podrá iniciar sesión.
        </div>
        <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
          <button
            onClick={onCancelar}
            disabled={cargando}
            style={{
              padding: '8px 16px',
              borderRadius: '6px',
              border: '1px solid var(--z-border)',
              background: 'transparent',
              color: 'var(--z-text-muted)',
              fontSize: '13px',
              cursor: 'pointer',
            }}
          >
            Cancelar
          </button>
          <button
            onClick={onConfirmar}
            disabled={cargando}
            style={{
              padding: '8px 16px',
              borderRadius: '6px',
              border: 'none',
              background: '#dc2626',
              color: 'white',
              fontSize: '13px',
              fontWeight: 600,
              cursor: cargando ? 'not-allowed' : 'pointer',
              opacity: cargando ? 0.7 : 1,
            }}
          >
            {cargando ? 'Inactivando...' : 'Inactivar'}
          </button>
        </div>
      </div>
    </div>
  );
}

function ModalReasignar({
  usuario,
  supervisores,
  supervisorActual,
  cargandoSupervisores,
  cargando,
  onConfirmar,
  onCancelar,
}: {
  usuario: UsuarioAdmin;
  supervisores: Supervisor[];
  supervisorActual: Supervisor | null | undefined;
  cargandoSupervisores: boolean;
  cargando: boolean;
  onConfirmar: (supervisorId: number) => void;
  onCancelar: () => void;
}) {
  const [paso, setPaso] = useState<'seleccion' | 'confirmacion'>('seleccion');
  const [supervisorSeleccionado, setSupervisorSeleccionado] = useState<Supervisor | null>(null);
  const [busqueda, setBusqueda] = useState('');

  const supervisoresFiltrados = supervisores.filter((s) => {
    const q = busqueda.toLowerCase();
    return (
      s.nombreCompleto.toLowerCase().includes(q) ||
      s.cargo.toLowerCase().includes(q) ||
      s.numeroDocumento.toString().includes(q)
    );
  });

  return (
    <div style={{
      position: 'fixed', inset: 0,
      background: 'rgba(0,0,0,0.4)',
      display: 'flex', alignItems: 'center', justifyContent: 'center',
      zIndex: 100,
    }}>
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '12px',
        padding: '32px',
        maxWidth: '480px',
        width: '90%',
        boxShadow: '0 8px 32px rgba(0,0,0,0.12)',
      }}>
        {paso === 'seleccion' ? (
          <>
            <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '4px', color: 'var(--z-text-primary)' }}>
              Reasignar Supervisor
            </p>
            <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '12px', lineHeight: 1.5 }}>
              Selecciona el nuevo supervisor para{' '}
              <strong style={{ color: 'var(--z-text-primary)' }}>{usuario.nombreCompleto}</strong>.
            </p>

            {/* Supervisor actual */}
            <div style={{
              padding: '10px 14px',
              borderRadius: '8px',
              fontSize: '12px',
              background: 'var(--z-bg-surface)',
              border: '0.5px solid var(--z-border)',
              marginBottom: '14px',
              display: 'flex',
              alignItems: 'center',
              gap: '8px',
            }}>
              <span style={{ color: 'var(--z-text-muted)', flexShrink: 0 }}>Supervisor actual:</span>
              {supervisorActual
                ? <span style={{ fontWeight: 600, color: 'var(--z-text-primary)' }}>
                    {supervisorActual.nombreCompleto}
                    <span style={{ fontWeight: 400, color: 'var(--z-text-muted)' }}> · {supervisorActual.cargo}</span>
                  </span>
                : <span style={{ color: 'var(--z-text-muted)', fontStyle: 'italic' }}>Sin asignar</span>
              }
            </div>

            {/* Buscador */}
            <input
              type="text"
              placeholder="Buscar por nombre, cargo o cédula..."
              value={busqueda}
              onChange={(e) => setBusqueda(e.target.value)}
              style={{
                width: '100%',
                padding: '7px 11px',
                fontSize: '12px',
                border: '1px solid var(--z-border)',
                borderRadius: '6px',
                background: 'var(--z-bg-card)',
                color: 'var(--z-text-primary)',
                outline: 'none',
                marginBottom: '10px',
                boxSizing: 'border-box',
              }}
              onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--z-brand)'; }}
              onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--z-border)'; }}
            />

            {/* Lista de supervisores */}
            <div style={{
              maxHeight: '240px',
              overflowY: 'auto',
              border: '0.5px solid var(--z-border)',
              borderRadius: '8px',
              marginBottom: '20px',
            }}>
              {cargandoSupervisores && (
                <div style={{ padding: '24px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                  Cargando supervisores...
                </div>
              )}
              {!cargandoSupervisores && supervisores.length === 0 && (
                <div style={{ padding: '24px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                  No hay supervisores disponibles.
                </div>
              )}
              {!cargandoSupervisores && supervisores.length > 0 && supervisoresFiltrados.length === 0 && (
                <div style={{ padding: '24px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                  Sin resultados para la búsqueda.
                </div>
              )}
              {supervisoresFiltrados.map((s, i) => {
                const seleccionado = supervisorSeleccionado?.id === s.id;
                return (
                  <div
                    key={s.id}
                    onClick={() => setSupervisorSeleccionado(s)}
                    style={{
                      padding: '12px 16px',
                      cursor: 'pointer',
                      borderBottom: i < supervisoresFiltrados.length - 1 ? '0.5px solid var(--z-border)' : 'none',
                      background: seleccionado ? 'var(--z-brand-subtle, #EFF6FF)' : 'transparent',
                      display: 'flex',
                      justifyContent: 'space-between',
                      alignItems: 'center',
                      transition: 'background 0.1s',
                    }}
                  >
                    <div>
                      <p style={{
                        fontSize: '13px',
                        fontWeight: seleccionado ? 600 : 400,
                        color: seleccionado ? 'var(--z-brand)' : 'var(--z-text-primary)',
                        marginBottom: '2px',
                      }}>
                        {s.nombreCompleto}
                      </p>
                      <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>
                        {s.cargo} · CC {s.numeroDocumento}
                      </p>
                    </div>
                    {seleccionado && (
                      <span style={{ fontSize: '16px', color: 'var(--z-brand)' }}>✓</span>
                    )}
                  </div>
                );
              })}
            </div>

            <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
              <button
                onClick={onCancelar}
                style={{
                  padding: '8px 16px',
                  borderRadius: '6px',
                  border: '1px solid var(--z-border)',
                  background: 'transparent',
                  color: 'var(--z-text-muted)',
                  fontSize: '13px',
                  cursor: 'pointer',
                }}
              >
                Cancelar
              </button>
              <button
                onClick={() => setPaso('confirmacion')}
                disabled={!supervisorSeleccionado}
                style={{
                  padding: '8px 16px',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--z-brand)',
                  color: 'white',
                  fontSize: '13px',
                  fontWeight: 600,
                  cursor: supervisorSeleccionado ? 'pointer' : 'not-allowed',
                  opacity: supervisorSeleccionado ? 1 : 0.4,
                }}
              >
                Siguiente
              </button>
            </div>
          </>
        ) : (
          <>
            <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '8px', color: 'var(--z-text-primary)' }}>
              Confirmar reasignación
            </p>
            <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '12px', lineHeight: 1.5 }}>
              El supervisor de{' '}
              <strong style={{ color: 'var(--z-text-primary)' }}>{usuario.nombreCompleto}</strong>{' '}
              será cambiado a{' '}
              <strong style={{ color: 'var(--z-text-primary)' }}>{supervisorSeleccionado!.nombreCompleto}</strong>.
            </p>
            <div style={{
              padding: '10px 14px',
              borderRadius: '8px',
              fontSize: '12px',
              background: '#fffbeb',
              border: '0.5px solid #fde68a',
              color: '#92400e',
              marginBottom: '24px',
              lineHeight: 1.5,
            }}>
              Esta acción también eliminará todas las asignaciones de cartera actuales del empleado.
            </div>
            <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
              <button
                onClick={() => setPaso('seleccion')}
                disabled={cargando}
                style={{
                  padding: '8px 16px',
                  borderRadius: '6px',
                  border: '1px solid var(--z-border)',
                  background: 'transparent',
                  color: 'var(--z-text-muted)',
                  fontSize: '13px',
                  cursor: 'pointer',
                }}
              >
                Atrás
              </button>
              <button
                onClick={() => onConfirmar(supervisorSeleccionado!.id)}
                disabled={cargando}
                style={{
                  padding: '8px 16px',
                  borderRadius: '6px',
                  border: 'none',
                  background: 'var(--z-brand)',
                  color: 'white',
                  fontSize: '13px',
                  fontWeight: 600,
                  cursor: cargando ? 'not-allowed' : 'pointer',
                  opacity: cargando ? 0.7 : 1,
                }}
              >
                {cargando ? 'Guardando...' : 'Confirmar'}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

function ModalEditarUsuario({
  usuario,
  cargando,
  errorMensaje,
  onConfirmar,
  onCancelar,
}: {
  usuario: UsuarioAdmin;
  cargando: boolean;
  errorMensaje: string | null;
  onConfirmar: (nuevoUsuario: string) => void;
  onCancelar: () => void;
}) {
  const [valor, setValor] = useState(usuario.usuario);
  const limpio = valor.trim();
  const sinCambios = limpio === usuario.usuario.trim();
  const valido = limpio.length >= 3 && limpio.length <= 50;
  const puedeGuardar = valido && !sinCambios && !cargando;

  return (
    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100 }}>
      <div style={{ background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: '12px', padding: '32px', maxWidth: '420px', width: '90%', boxShadow: '0 8px 32px rgba(0,0,0,0.12)' }}>
        <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '8px', color: 'var(--z-text-primary)' }}>
          Modificar usuario
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '16px', lineHeight: 1.5 }}>
          Cambia el nombre de usuario de{' '}
          <strong style={{ color: 'var(--z-text-primary)' }}>{usuario.nombreCompleto}</strong>.
          Se usará para iniciar sesión en Zentinela.
        </p>
        <label style={{ display: 'block', fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: 6 }}>
          Usuario
        </label>
        <input
          type="text" value={valor} onChange={(e) => setValor(e.target.value)}
          autoFocus disabled={cargando} spellCheck={false}
          style={{ width: '100%', padding: '8px 12px', fontSize: '13px', fontFamily: 'monospace', border: '1px solid var(--z-border)', borderRadius: '6px', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none', marginBottom: 6, boxSizing: 'border-box' }}
          onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--z-brand)'; }}
          onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--z-border)'; }}
        />
        <p style={{ fontSize: 11, color: 'var(--z-text-muted)', marginBottom: 16 }}>Entre 3 y 50 caracteres. El cambio aplica de inmediato.</p>
        {errorMensaje && (
          <div style={{ padding: '8px 12px', borderRadius: '6px', fontSize: '12px', background: '#fff5f5', border: '0.5px solid #fecaca', color: '#dc2626', marginBottom: '16px' }}>
            {errorMensaje}
          </div>
        )}
        <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
          <button onClick={onCancelar} disabled={cargando} style={{ padding: '8px 16px', borderRadius: '6px', border: '1px solid var(--z-border)', background: 'transparent', color: 'var(--z-text-muted)', fontSize: '13px', cursor: cargando ? 'not-allowed' : 'pointer' }}>
            Cancelar
          </button>
          <button onClick={() => onConfirmar(limpio)} disabled={!puedeGuardar} style={{ padding: '8px 16px', borderRadius: '6px', border: 'none', background: 'var(--z-brand)', color: 'white', fontSize: '13px', fontWeight: 600, cursor: puedeGuardar ? 'pointer' : 'not-allowed', opacity: puedeGuardar ? 1 : 0.5 }}>
            {cargando ? 'Guardando...' : 'Guardar'}
          </button>
        </div>
      </div>
    </div>
  );
}

function ModalCambiarRol({
  usuario,
  onConfirmar,
  onCancelar,
  cargando,
}: {
  usuario: UsuarioAdmin;
  onConfirmar: (nuevoRol: string) => void;
  onCancelar: () => void;
  cargando: boolean;
}) {
  const [rolSeleccionado, setRolSeleccionado] = useState<string>(usuario.rol);

  return (
    <div style={{ position: 'fixed', inset: 0, background: 'rgba(0,0,0,0.4)', display: 'flex', alignItems: 'center', justifyContent: 'center', zIndex: 100 }}>
      <div style={{ background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: '12px', padding: '32px', maxWidth: '400px', width: '90%', boxShadow: '0 8px 32px rgba(0,0,0,0.12)' }}>
        <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '4px', color: 'var(--z-text-primary)' }}>
          Cambiar rol
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '20px', lineHeight: 1.5 }}>
          Selecciona el nuevo rol para{' '}
          <strong style={{ color: 'var(--z-text-primary)' }}>{usuario.nombreCompleto}</strong>.
        </p>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '8px', marginBottom: '24px' }}>
          {ROLES_DISPONIBLES.map((rol) => {
            const colores = BADGE_COLORES[rol] ?? { bg: '#F5F5F4', color: '#78716C' };
            const seleccionado = rolSeleccionado === rol;
            return (
              <div key={rol} onClick={() => setRolSeleccionado(rol)} style={{ display: 'flex', alignItems: 'center', justifyContent: 'space-between', padding: '10px 14px', borderRadius: '8px', border: seleccionado ? `1.5px solid ${colores.color}` : '1px solid var(--z-border)', background: seleccionado ? colores.bg : 'transparent', cursor: 'pointer', transition: 'all 0.15s' }}>
                <span style={{ fontSize: '13px', fontWeight: seleccionado ? 600 : 400, color: seleccionado ? colores.color : 'var(--z-text-primary)' }}>{rol}</span>
                {seleccionado && <span style={{ fontSize: '14px', color: colores.color }}>✓</span>}
              </div>
            );
          })}
        </div>
        <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
          <button onClick={onCancelar} disabled={cargando} style={{ padding: '8px 16px', borderRadius: '6px', border: '1px solid var(--z-border)', background: 'transparent', color: 'var(--z-text-muted)', fontSize: '13px', cursor: 'pointer' }}>
            Cancelar
          </button>
          <button onClick={() => onConfirmar(rolSeleccionado)} disabled={cargando || rolSeleccionado === usuario.rol} style={{ padding: '8px 16px', borderRadius: '6px', border: 'none', background: 'var(--z-brand)', color: 'white', fontSize: '13px', fontWeight: 600, cursor: (cargando || rolSeleccionado === usuario.rol) ? 'not-allowed' : 'pointer', opacity: (cargando || rolSeleccionado === usuario.rol) ? 0.4 : 1 }}>
            {cargando ? 'Guardando...' : 'Confirmar'}
          </button>
        </div>
      </div>
    </div>
  );
}

export function GestionPersonalPage() {
  const [busqueda,          setBusqueda]          = useState('');
  const [filtroSupervisor,  setFiltroSupervisor]  = useState('');
  const [menuAbiertoId,     setMenuAbiertoId]     = useState<number | null>(null);
  const menuRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const cerrar = (e: MouseEvent) => {
      if (menuRef.current && !menuRef.current.contains(e.target as Node))
        setMenuAbiertoId(null);
    };
    document.addEventListener('mousedown', cerrar);
    return () => document.removeEventListener('mousedown', cerrar);
  }, []);
  const [usuarioParaReset, setUsuarioParaReset] = useState<UsuarioAdmin | null>(null);
  const [usuarioParaReasignar, setUsuarioParaReasignar] = useState<UsuarioAdmin | null>(null);
  const [usuarioParaInactivar, setUsuarioParaInactivar] = useState<UsuarioAdmin | null>(null);
  const [usuarioParaEditarUsuario, setUsuarioParaEditarUsuario] = useState<UsuarioAdmin | null>(null);
  const [errorEditarUsuario, setErrorEditarUsuario] = useState<string | null>(null);
  const [filaHover, setFilaHover] = useState<number | null>(null);
  const [usuarioParaCambiarRol, setUsuarioParaCambiarRol] = useState<UsuarioAdmin | null>(null);
  const [feedback, setFeedback] = useState<{ tipo: 'exito' | 'error'; texto: string } | null>(null);
  const queryClient = useQueryClient();

  const { data: usuarios = [], isLoading, isError } = useQuery({
    queryKey: ['admin-usuarios'],
    queryFn: listarUsuarios,
  });

  const { data: supervisores = [], isLoading: cargandoSupervisores } = useQuery({
    queryKey: ['admin-supervisores'],
    queryFn: listarSupervisores,
    enabled: !!usuarioParaReasignar,
  });

  const { data: supervisoresActivos = [] } = useQuery({
    queryKey: ['admin-supervisores-activos'],
    queryFn: listarSupervisoresActivos,
  });

  const { data: supervisorActual, isLoading: cargandoSupervisorActual } = useQuery({
    queryKey: ['admin-supervisor-actual', usuarioParaReasignar?.id],
    queryFn: () => obtenerSupervisorActual(usuarioParaReasignar!.id),
    enabled: !!usuarioParaReasignar,
  });

  const mutationReset = useMutation({
    mutationFn: (id: number) => resetPassword(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-usuarios'] });
      setFeedback({ tipo: 'exito', texto: `Contraseña de ${usuarioParaReset?.nombreCompleto} restablecida correctamente.` });
      setUsuarioParaReset(null);
    },
    onError: () => {
      setFeedback({ tipo: 'error', texto: 'No se pudo restablecer la contraseña. Intenta de nuevo.' });
      setUsuarioParaReset(null);
    },
  });

  const mutationInactivar = useMutation({
    mutationFn: (id: number) => inactivarUsuario(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-usuarios'] });
      setFeedback({ tipo: 'exito', texto: `${usuarioParaInactivar?.nombreCompleto} fue inactivado y eliminado de Zentinela.` });
      setUsuarioParaInactivar(null);
    },
    onError: () => {
      setFeedback({ tipo: 'error', texto: 'No se pudo inactivar el usuario. Intenta de nuevo.' });
      setUsuarioParaInactivar(null);
    },
  });

  const mutationCambiarUsuario = useMutation({
    mutationFn: ({ id, nuevoUsuario }: { id: number; nuevoUsuario: string }) =>
      cambiarUsuario(id, nuevoUsuario),
    onSuccess: (_, { nuevoUsuario }) => {
      queryClient.invalidateQueries({ queryKey: ['admin-usuarios'] });
      setFeedback({ tipo: 'exito', texto: `Usuario actualizado a "${nuevoUsuario}".` });
      setUsuarioParaEditarUsuario(null);
      setErrorEditarUsuario(null);
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'No se pudo cambiar el usuario. Intenta de nuevo.';
      setErrorEditarUsuario(msg);
    },
  });

  const mutationCambiarRol = useMutation({
    mutationFn: ({ usuarioId, rol }: { usuarioId: number; rol: string }) =>
      cambiarRol(usuarioId, rol),
    onSuccess: (_, { rol }) => {
      queryClient.invalidateQueries({ queryKey: ['admin-usuarios'] });
      setFeedback({ tipo: 'exito', texto: `Rol de ${usuarioParaCambiarRol?.nombreCompleto} actualizado a '${rol}' correctamente.` });
      setUsuarioParaCambiarRol(null);
    },
    onError: () => {
      setFeedback({ tipo: 'error', texto: 'No se pudo cambiar el rol. Intenta de nuevo.' });
      setUsuarioParaCambiarRol(null);
    },
  });

  const mutationReasignar = useMutation({
    mutationFn: ({ usuarioId, nuevoSupervisorId }: { usuarioId: number; nuevoSupervisorId: number }) =>
      reasignarSupervisor(usuarioId, nuevoSupervisorId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-usuarios'] });
      setFeedback({ tipo: 'exito', texto: `Supervisor de ${usuarioParaReasignar?.nombreCompleto} reasignado correctamente.` });
      setUsuarioParaReasignar(null);
    },
    onError: () => {
      setFeedback({ tipo: 'error', texto: 'No se pudo reasignar el supervisor. Intenta de nuevo.' });
      setUsuarioParaReasignar(null);
    },
  });

  // Opciones del dropdown — todos los empleados que son supervisor de alguien en RRHH
  const opcionesSupervisor = supervisoresActivos
    .map(s => s.nombreCompleto)
    .sort();

  const usuariosFiltrados = usuarios.filter((u) => {
    const q = busqueda.toLowerCase();
    const coincideTexto =
      u.nombreCompleto.toLowerCase().includes(q) ||
      u.usuario.toLowerCase().includes(q) ||
      u.rol.toLowerCase().includes(q);
    const coincideSupervisor =
      !filtroSupervisor || u.nombreSupervisor === filtroSupervisor;
    return coincideTexto && coincideSupervisor;
  });

  return (
    <div style={{ width: '100%' }}>
      {/* Encabezado */}
      <div style={{ marginBottom: 28 }}>
        <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
          Gestión de Personal
        </h1>
        <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
          Administración de usuarios registrados en Zentinela.
        </p>
      </div>

      {/* Feedback */}
      {feedback && (
        <div style={{
          padding: '11px 16px',
          borderRadius: '8px',
          fontSize: '13px',
          marginBottom: '20px',
          background: feedback.tipo === 'exito' ? '#f0fdf4' : '#fff5f5',
          border: `0.5px solid ${feedback.tipo === 'exito' ? '#bbf7d0' : '#fecaca'}`,
          color: feedback.tipo === 'exito' ? '#16a34a' : '#dc2626',
          display: 'flex',
          justifyContent: 'space-between',
          alignItems: 'center',
        }}>
          {feedback.texto}
          <button
            onClick={() => setFeedback(null)}
            style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: '16px', color: 'inherit', lineHeight: 1, marginLeft: 12, flexShrink: 0 }}
          >
            ×
          </button>
        </div>
      )}

      {/* Buscador + filtro supervisor */}
      <div style={{ display: 'flex', gap: '10px', marginBottom: '20px', flexWrap: 'wrap' }}>
        <input
          type="text"
          placeholder="Buscar por nombre, usuario o rol..."
          value={busqueda}
          onChange={(e) => setBusqueda(e.target.value)}
          style={{
            flex: '1 1 260px',
            maxWidth: '380px',
            padding: '8px 12px',
            fontSize: '13px',
            border: '1px solid var(--z-border)',
            borderRadius: '6px',
            background: 'var(--z-bg-card)',
            color: 'var(--z-text-primary)',
            outline: 'none',
            boxShadow: '0 4px 12px rgba(0,0,0,0.08), 0 1px 3px rgba(0,0,0,0.05)',
          }}
          onFocus={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-brand)'; }}
          onBlur={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-border)'; }}
        />
        <select
          value={filtroSupervisor}
          onChange={(e) => setFiltroSupervisor(e.target.value)}
          style={{
            padding: '8px 12px',
            fontSize: '13px',
            border: '1px solid var(--z-border)',
            borderRadius: '6px',
            background: 'var(--z-bg-card)',
            color: filtroSupervisor ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
            outline: 'none',
            cursor: 'pointer',
            minWidth: '200px',
            boxShadow: '0 4px 12px rgba(0,0,0,0.08), 0 1px 3px rgba(0,0,0,0.05)',
          }}
        >
          <option value="">Todos los supervisores</option>
          {opcionesSupervisor.map(s => (
            <option key={s} value={s}>{s}</option>
          ))}
        </select>
      </div>

      {/* Tabla */}
      <div ref={menuRef} style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '12px',
        overflow: 'visible',
      }}>
        {/* Encabezado tabla */}
        <div style={{
          display: 'grid',
          gridTemplateColumns: '28fr 14fr max-content 15fr 40px',
          columnGap: '16px',
          padding: '11px 20px',
          borderBottom: '2px solid var(--z-brand)',
          background: 'var(--z-brand-light)',
          borderRadius: '12px 12px 0 0',
        }}>
          {['Nombre', 'Usuario', 'Rol', 'Estado', ''].map((h) => (
            <span key={h} style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-primary)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
              {h}
            </span>
          ))}
        </div>

        {isLoading && (
          <div style={{ padding: '40px', textAlign: 'center', color: 'var(--z-text-muted)', fontSize: '13px' }}>
            Cargando usuarios...
          </div>
        )}

        {isError && (
          <div style={{ padding: '40px', textAlign: 'center', color: '#dc2626', fontSize: '13px' }}>
            Error al cargar usuarios.
          </div>
        )}

        {!isLoading && !isError && usuariosFiltrados.length === 0 && (
          <div style={{ padding: '40px', textAlign: 'center', color: 'var(--z-text-muted)', fontSize: '13px' }}>
            {busqueda ? 'Sin resultados para la búsqueda.' : 'No hay usuarios registrados.'}
          </div>
        )}

        {usuariosFiltrados.map((u, i) => (
          <div
            key={u.id}
            onMouseEnter={() => setFilaHover(u.id)}
            onMouseLeave={() => setFilaHover(prev => prev === u.id ? null : prev)}
            style={{
              display: 'grid',
              gridTemplateColumns: '28fr 14fr max-content 15fr 40px',
              columnGap: '16px',
              padding: '13px 20px',
              alignItems: 'center',
              borderBottom: i < usuariosFiltrados.length - 1 ? '0.5px solid var(--z-border)' : 'none',
            }}
          >
            <div>
              <p style={{ fontSize: '13px', fontWeight: 500, color: 'var(--z-text-primary)', marginBottom: '2px' }}>
                {u.nombreCompleto}
              </p>
              <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>
                CC {u.numeroDocumento}
              </p>
            </div>

            {/* Usuario + lápiz que aparece en hover de la fila */}
            <span style={{
              fontSize: '12px', color: 'var(--z-text-muted)', fontFamily: 'monospace',
              display: 'inline-flex', alignItems: 'center', gap: 6, minWidth: 0,
            }}>
              <span style={{ overflow: 'hidden', textOverflow: 'ellipsis', whiteSpace: 'nowrap' }}>
                {u.usuario}
              </span>
              <button
                type="button"
                aria-label={`Modificar usuario de ${u.nombreCompleto}`}
                onClick={() => { setErrorEditarUsuario(null); setUsuarioParaEditarUsuario(u); }}
                style={{
                  display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
                  width: 18, height: 18, padding: 0,
                  border: 'none', background: 'transparent',
                  color: 'var(--z-text-muted)',
                  cursor: 'pointer',
                  opacity: filaHover === u.id ? 1 : 0,
                  transition: 'opacity 0.15s, color 0.15s',
                  flexShrink: 0,
                }}
                onMouseOver={(e) => { (e.currentTarget as HTMLElement).style.color = 'var(--z-brand)'; }}
                onMouseOut={(e) => { (e.currentTarget as HTMLElement).style.color = 'var(--z-text-muted)'; }}
              >
                <IconoLapiz />
              </button>
            </span>

            <BadgeRol rol={u.rol} />

            <span style={{ fontSize: '11px', fontWeight: 500, color: u.requiereCambioPassword ? '#d97706' : '#16a34a' }}>
              {u.requiereCambioPassword ? 'Cambio pendiente' : 'Al día'}
            </span>

            {/* Botón opciones */}
            <div style={{ position: 'relative' }}>
              <button
                onClick={() => setMenuAbiertoId(menuAbiertoId === u.id ? null : u.id)}
                style={{
                  width: '32px',
                  height: '32px',
                  borderRadius: '6px',
                  border: '1px solid var(--z-border)',
                  background: menuAbiertoId === u.id ? 'var(--z-bg-surface)' : 'transparent',
                  color: 'var(--z-text-muted)',
                  cursor: 'pointer',
                  fontSize: '16px',
                  lineHeight: 1,
                  display: 'flex',
                  alignItems: 'center',
                  justifyContent: 'center',
                  transition: 'all 0.15s',
                }}
                onMouseOver={(e) => { (e.currentTarget as HTMLElement).style.background = 'var(--z-bg-surface)'; }}
                onMouseOut={(e) => { if (menuAbiertoId !== u.id) (e.currentTarget as HTMLElement).style.background = 'transparent'; }}
              >
                ···
              </button>

              {menuAbiertoId === u.id && (
                <div style={{
                  position: 'absolute',
                  right: 0,
                  top: '36px',
                  background: 'var(--z-bg-card)',
                  border: '0.5px solid var(--z-border)',
                  borderRadius: '8px',
                  boxShadow: '0 4px 16px rgba(0,0,0,0.10)',
                  zIndex: 50,
                  minWidth: '180px',
                  overflow: 'hidden',
                }}>
                  {[
                    { label: 'Cambiar rol',          color: '#7c3aed', bg: '#f5f3ff', accion: () => { setUsuarioParaCambiarRol(u);  setMenuAbiertoId(null); } },
                    { label: 'Restablecer clave',     color: '#2563eb', bg: '#eff6ff', accion: () => { setUsuarioParaReset(u);        setMenuAbiertoId(null); } },
                    { label: 'Reasignar supervisor',  color: '#d97706', bg: '#fffbeb', accion: () => { setUsuarioParaReasignar(u);    setMenuAbiertoId(null); } },
                    { label: 'Inactivar',             color: '#dc2626', bg: '#fff5f5', accion: () => { setUsuarioParaInactivar(u);   setMenuAbiertoId(null); } },
                  ].map(({ label, color, bg, accion }, idx, arr) => (
                    <button
                      key={label}
                      onClick={accion}
                      style={{
                        display: 'block',
                        width: '100%',
                        padding: '10px 16px',
                        textAlign: 'left',
                        background: 'transparent',
                        border: 'none',
                        borderBottom: idx < arr.length - 1 ? '0.5px solid var(--z-border)' : 'none',
                        fontSize: '13px',
                        color: 'var(--z-text-primary)',
                        cursor: 'pointer',
                        transition: 'background 0.1s, color 0.1s',
                      }}
                      onMouseOver={(e) => {
                        (e.currentTarget as HTMLElement).style.background = bg;
                        (e.currentTarget as HTMLElement).style.color = color;
                      }}
                      onMouseOut={(e) => {
                        (e.currentTarget as HTMLElement).style.background = 'transparent';
                        (e.currentTarget as HTMLElement).style.color = 'var(--z-text-primary)';
                      }}
                    >
                      {label}
                    </button>
                  ))}
                </div>
              )}
            </div>
          </div>
        ))}
      </div>

      {/* Modal cambiar rol */}
      {usuarioParaCambiarRol && (
        <ModalCambiarRol
          usuario={usuarioParaCambiarRol}
          cargando={mutationCambiarRol.isPending}
          onConfirmar={(nuevoRol) =>
            mutationCambiarRol.mutate({ usuarioId: usuarioParaCambiarRol.id, rol: nuevoRol })
          }
          onCancelar={() => setUsuarioParaCambiarRol(null)}
        />
      )}

      {/* Modal restablecer contraseña */}
      {usuarioParaReset && (
        <ModalConfirmar
          usuario={usuarioParaReset}
          cargando={mutationReset.isPending}
          onConfirmar={() => mutationReset.mutate(usuarioParaReset.id)}
          onCancelar={() => setUsuarioParaReset(null)}
        />
      )}

      {/* Modal inactivar usuario */}
      {usuarioParaInactivar && (
        <ModalInactivar
          usuario={usuarioParaInactivar}
          cargando={mutationInactivar.isPending}
          onConfirmar={() => mutationInactivar.mutate(usuarioParaInactivar.id)}
          onCancelar={() => setUsuarioParaInactivar(null)}
        />
      )}

      {/* Modal editar usuario (UserName) */}
      {usuarioParaEditarUsuario && (
        <ModalEditarUsuario
          usuario={usuarioParaEditarUsuario}
          cargando={mutationCambiarUsuario.isPending}
          errorMensaje={errorEditarUsuario}
          onConfirmar={(nuevoUsuario) =>
            mutationCambiarUsuario.mutate({ id: usuarioParaEditarUsuario.id, nuevoUsuario })
          }
          onCancelar={() => { setUsuarioParaEditarUsuario(null); setErrorEditarUsuario(null); }}
        />
      )}

      {/* Modal reasignar supervisor */}
      {usuarioParaReasignar && (
        <ModalReasignar
          usuario={usuarioParaReasignar}
          supervisores={supervisores}
          supervisorActual={cargandoSupervisorActual ? undefined : (supervisorActual ?? null)}
          cargandoSupervisores={cargandoSupervisores}
          cargando={mutationReasignar.isPending}
          onConfirmar={(nuevoSupervisorId) =>
            mutationReasignar.mutate({ usuarioId: usuarioParaReasignar.id, nuevoSupervisorId })
          }
          onCancelar={() => setUsuarioParaReasignar(null)}
        />
      )}
    </div>
  );
}
