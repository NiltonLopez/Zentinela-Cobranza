import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { listarUsuarios, resetPassword, listarSupervisores, reasignarSupervisor, inactivarUsuario, obtenerSupervisorActual } from '../../api/adminApi';
import type { UsuarioAdmin, Supervisor } from '../../types/admin';

const BADGE_COLORES: Record<string, { bg: string; color: string }> = {
  Admin:    { bg: '#FEF0EC', color: '#F54927' },
  Líder:    { bg: '#EFF6FF', color: '#2563EB' },
  Director: { bg: '#F0FDF4', color: '#16A34A' },
  Asesor:   { bg: '#F5F5F4', color: '#78716C' },
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

export function GestionPersonalPage() {
  const [busqueda, setBusqueda] = useState('');
  const [usuarioParaReset, setUsuarioParaReset] = useState<UsuarioAdmin | null>(null);
  const [usuarioParaReasignar, setUsuarioParaReasignar] = useState<UsuarioAdmin | null>(null);
  const [usuarioParaInactivar, setUsuarioParaInactivar] = useState<UsuarioAdmin | null>(null);
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

  const usuariosFiltrados = usuarios.filter((u) => {
    const q = busqueda.toLowerCase();
    return (
      u.nombreCompleto.toLowerCase().includes(q) ||
      u.usuario.toLowerCase().includes(q) ||
      u.rol.toLowerCase().includes(q)
    );
  });

  return (
    <div style={{ maxWidth: '960px' }}>
      {/* Encabezado */}
      <div style={{ marginBottom: '24px' }}>
        <h2 style={{ fontSize: '18px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Gestión de Personal
        </h2>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Administración de usuarios registrados en Zentinela.
        </p>
      </div>

      {/* Feedback */}
      {feedback && (
        <div style={{
          padding: '10px 14px',
          borderRadius: '8px',
          fontSize: '13px',
          marginBottom: '16px',
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
            style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: '14px', color: 'inherit', lineHeight: 1 }}
          >
            ×
          </button>
        </div>
      )}

      {/* Buscador */}
      <div style={{ marginBottom: '16px' }}>
        <input
          type="text"
          placeholder="Buscar por nombre, usuario o rol..."
          value={busqueda}
          onChange={(e) => setBusqueda(e.target.value)}
          style={{
            width: '100%',
            maxWidth: '360px',
            padding: '8px 12px',
            fontSize: '13px',
            border: '1px solid var(--z-border)',
            borderRadius: '6px',
            background: 'var(--z-bg-card)',
            color: 'var(--z-text-primary)',
            outline: 'none',
          }}
          onFocus={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-brand)'; }}
          onBlur={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-border)'; }}
        />
      </div>

      {/* Tabla */}
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '10px',
        overflow: 'hidden',
      }}>
        {/* Encabezado tabla */}
        <div style={{
          display: 'grid',
          gridTemplateColumns: '1fr 160px 120px 140px 320px',
          columnGap: '16px',
          padding: '10px 16px',
          borderBottom: '0.5px solid var(--z-border)',
          background: 'var(--z-bg-surface)',
        }}>
          {['Nombre', 'Usuario', 'Rol', 'Estado', ''].map((h) => (
            <span key={h} style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em' }}>
              {h}
            </span>
          ))}
        </div>

        {isLoading && (
          <div style={{ padding: '32px', textAlign: 'center', color: 'var(--z-text-muted)', fontSize: '13px' }}>
            Cargando usuarios...
          </div>
        )}

        {isError && (
          <div style={{ padding: '32px', textAlign: 'center', color: '#dc2626', fontSize: '13px' }}>
            Error al cargar usuarios.
          </div>
        )}

        {!isLoading && !isError && usuariosFiltrados.length === 0 && (
          <div style={{ padding: '32px', textAlign: 'center', color: 'var(--z-text-muted)', fontSize: '13px' }}>
            {busqueda ? 'Sin resultados para la búsqueda.' : 'No hay usuarios registrados.'}
          </div>
        )}

        {usuariosFiltrados.map((u, i) => (
          <div
            key={u.id}
            style={{
              display: 'grid',
              gridTemplateColumns: '1fr 160px 120px 140px 320px',
          columnGap: '16px',
              padding: '12px 16px',
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

            <span style={{ fontSize: '12px', color: 'var(--z-text-muted)', fontFamily: 'monospace' }}>
              {u.usuario}
            </span>

            <BadgeRol rol={u.rol} />

            <span style={{
              fontSize: '11px',
              fontWeight: 500,
              color: u.requiereCambioPassword ? '#d97706' : '#16a34a',
            }}>
              {u.requiereCambioPassword ? 'Cambio pendiente' : 'Al día'}
            </span>

            {/* Acciones */}
            <div style={{ display: 'flex', gap: '6px', justifyContent: 'flex-end' }}>
              <button
                onClick={() => setUsuarioParaReset(u)}
                style={{
                  padding: '5px 10px',
                  borderRadius: '5px',
                  border: '1px solid var(--z-border)',
                  background: 'transparent',
                  fontSize: '11px',
                  color: 'var(--z-text-muted)',
                  cursor: 'pointer',
                  whiteSpace: 'nowrap',
                  transition: 'all 0.15s',
                }}
                onMouseOver={(e) => {
                  (e.currentTarget as HTMLElement).style.borderColor = 'var(--z-brand)';
                  (e.currentTarget as HTMLElement).style.color = 'var(--z-brand)';
                }}
                onMouseOut={(e) => {
                  (e.currentTarget as HTMLElement).style.borderColor = 'var(--z-border)';
                  (e.currentTarget as HTMLElement).style.color = 'var(--z-text-muted)';
                }}
              >
                Restablecer clave
              </button>
              <button
                onClick={() => setUsuarioParaReasignar(u)}
                style={{
                  padding: '5px 10px',
                  borderRadius: '5px',
                  border: '1px solid var(--z-border)',
                  background: 'transparent',
                  fontSize: '11px',
                  color: 'var(--z-text-muted)',
                  cursor: 'pointer',
                  whiteSpace: 'nowrap',
                  transition: 'all 0.15s',
                }}
                onMouseOver={(e) => {
                  (e.currentTarget as HTMLElement).style.borderColor = '#d97706';
                  (e.currentTarget as HTMLElement).style.color = '#d97706';
                }}
                onMouseOut={(e) => {
                  (e.currentTarget as HTMLElement).style.borderColor = 'var(--z-border)';
                  (e.currentTarget as HTMLElement).style.color = 'var(--z-text-muted)';
                }}
              >
                Reasignar supervisor
              </button>
              <button
                onClick={() => setUsuarioParaInactivar(u)}
                style={{
                  padding: '5px 10px',
                  borderRadius: '5px',
                  border: '1px solid var(--z-border)',
                  background: 'transparent',
                  fontSize: '11px',
                  color: 'var(--z-text-muted)',
                  cursor: 'pointer',
                  whiteSpace: 'nowrap',
                  transition: 'all 0.15s',
                }}
                onMouseOver={(e) => {
                  (e.currentTarget as HTMLElement).style.borderColor = '#dc2626';
                  (e.currentTarget as HTMLElement).style.color = '#dc2626';
                }}
                onMouseOut={(e) => {
                  (e.currentTarget as HTMLElement).style.borderColor = 'var(--z-border)';
                  (e.currentTarget as HTMLElement).style.color = 'var(--z-text-muted)';
                }}
              >
                Inactivar
              </button>
            </div>
          </div>
        ))}
      </div>

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
