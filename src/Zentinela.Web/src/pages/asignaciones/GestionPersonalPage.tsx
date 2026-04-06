import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { listarUsuarios, resetPassword } from '../../api/adminApi';
import type { UsuarioAdmin } from '../../types/admin';

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

export function GestionPersonalPage() {
  const [busqueda, setBusqueda] = useState('');
  const [usuarioSeleccionado, setUsuarioSeleccionado] = useState<UsuarioAdmin | null>(null);
  const [feedback, setFeedback] = useState<{ tipo: 'exito' | 'error'; texto: string } | null>(null);
  const queryClient = useQueryClient();

  const { data: usuarios = [], isLoading, isError } = useQuery({
    queryKey: ['admin-usuarios'],
    queryFn: listarUsuarios,
  });

  const mutation = useMutation({
    mutationFn: (id: number) => resetPassword(id),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-usuarios'] });
      setFeedback({ tipo: 'exito', texto: `Contraseña de ${usuarioSeleccionado?.nombreCompleto} restablecida correctamente.` });
      setUsuarioSeleccionado(null);
    },
    onError: () => {
      setFeedback({ tipo: 'error', texto: 'No se pudo restablecer la contraseña. Intenta de nuevo.' });
      setUsuarioSeleccionado(null);
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
    <div style={{ maxWidth: '860px' }}>
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
          gridTemplateColumns: '1fr 160px 100px 120px 130px',
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
              gridTemplateColumns: '1fr 160px 100px 120px 130px',
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

            <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
              <button
                onClick={() => setUsuarioSeleccionado(u)}
                style={{
                  padding: '5px 12px',
                  borderRadius: '5px',
                  border: '1px solid var(--z-border)',
                  background: 'transparent',
                  fontSize: '12px',
                  color: 'var(--z-text-muted)',
                  cursor: 'pointer',
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
            </div>
          </div>
        ))}
      </div>

      {/* Modal de confirmación */}
      {usuarioSeleccionado && (
        <ModalConfirmar
          usuario={usuarioSeleccionado}
          cargando={mutation.isPending}
          onConfirmar={() => mutation.mutate(usuarioSeleccionado.id)}
          onCancelar={() => setUsuarioSeleccionado(null)}
        />
      )}
    </div>
  );
}
