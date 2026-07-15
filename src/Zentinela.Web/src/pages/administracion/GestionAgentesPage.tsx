import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  listarAgentes,
  obtenerCarterasDelAgente,
  asignarCarteraAgente,
  desasignarCarteraAgente,
} from '../../api/adminApi';
import type { AgenteAdmin } from '../../types/admin';

// ── Modal: gestionar carteras del agente ─────────────────────────────────────

function ModalCarteras({
  agente,
  onCerrar,
}: {
  agente: AgenteAdmin;
  onCerrar: () => void;
}) {
  const queryClient = useQueryClient();

  const { data, isLoading, isError } = useQuery({
    queryKey: ['agente-carteras', agente.agenteID],
    queryFn: () => obtenerCarterasDelAgente(agente.agenteID),
  });

  // Estado por checkbox: pending | error | idle (ausente del Map = idle).
  const [estadoChecks, setEstadoChecks] = useState<Map<number, 'pending' | 'error'>>(new Map());
  const [errorMensaje, setErrorMensaje] = useState<string | null>(null);

  const setEstado = (carteraID: number, estado: 'pending' | 'error' | null) => {
    setEstadoChecks(prev => {
      const next = new Map(prev);
      if (estado === null) next.delete(carteraID); else next.set(carteraID, estado);
      return next;
    });
  };

  const mutationToggle = useMutation({
    mutationFn: async ({ carteraID, asignar }: { carteraID: number; asignar: boolean }) => {
      if (asignar) await asignarCarteraAgente(agente.agenteID, carteraID);
      else         await desasignarCarteraAgente(agente.agenteID, carteraID);
      return carteraID;
    },
    onSuccess: (carteraID) => {
      setEstado(carteraID, null);
      setErrorMensaje(null);
      queryClient.invalidateQueries({ queryKey: ['agente-carteras', agente.agenteID] });
      queryClient.invalidateQueries({ queryKey: ['agentes'] });
    },
    onError: (err: unknown, vars) => {
      setEstado(vars.carteraID, 'error');
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'No se pudo actualizar la asignación.';
      setErrorMensaje(msg);
    },
  });

  const handleToggle = (carteraID: number, asignadaActual: boolean) => {
    if (estadoChecks.get(carteraID) === 'pending') return;
    setEstado(carteraID, 'pending');
    mutationToggle.mutate({ carteraID, asignar: !asignadaActual });
  };

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
        <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '4px', color: 'var(--z-text-primary)' }}>
          Carteras de {agente.nombreCompleto}
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '14px' }}>
          {data?.liderNombre
            ? <>Líder: <strong style={{ color: 'var(--z-text-primary)' }}>{data.liderNombre}</strong></>
            : 'Cargando...'}
        </p>

        {isLoading && (
          <div style={{ padding: '24px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
            Cargando carteras...
          </div>
        )}

        {isError && (
          <div style={{ padding: '24px', textAlign: 'center', fontSize: '13px', color: '#dc2626' }}>
            Error al cargar carteras.
          </div>
        )}

        {data && data.motivoNoAsignable && (
          <div style={{
            padding: '12px 14px',
            borderRadius: '8px',
            fontSize: '13px',
            background: '#fffbeb',
            border: '0.5px solid #fde68a',
            color: '#92400e',
            marginBottom: '20px',
            lineHeight: 1.5,
          }}>
            {data.motivoNoAsignable === 'Sin supervisor asignado'
              ? 'Este agente no tiene supervisor asignado. Asígnale uno en Gestión de Personal antes de poder gestionar sus carteras.'
              : data.motivoNoAsignable === 'Supervisor no es líder activo'
                ? 'El supervisor de este agente no está registrado como líder activo en el sistema. Verifica en Gestión de Líderes y Carteras.'
                : data.motivoNoAsignable}
          </div>
        )}

        {data && !data.motivoNoAsignable && data.carteras.length === 0 && (
          <div style={{
            padding: '12px 14px',
            borderRadius: '8px',
            fontSize: '13px',
            background: 'var(--z-bg-page)',
            border: '0.5px solid var(--z-border)',
            color: 'var(--z-text-muted)',
            marginBottom: '20px',
            lineHeight: 1.5,
          }}>
            Este líder no tiene carteras asignadas en el sistema. Asígnaselas en{' '}
            <strong>Gestión de Líderes y Carteras</strong> para poder vincularlas a sus agentes.
          </div>
        )}

        {data && !data.motivoNoAsignable && data.carteras.length > 0 && (
          <div style={{
            border: '0.5px solid var(--z-border)',
            borderRadius: '8px',
            marginBottom: '14px',
            maxHeight: '320px',
            overflowY: 'auto',
          }}>
            {data.carteras.map((c, i) => {
              const estado = estadoChecks.get(c.carteraID);
              const pending = estado === 'pending';
              const errored = estado === 'error';
              return (
                <label
                  key={c.carteraID}
                  htmlFor={`cartera-${c.carteraID}`}
                  style={{
                    display: 'flex',
                    alignItems: 'center',
                    gap: '10px',
                    padding: '10px 14px',
                    borderBottom: i < data.carteras.length - 1 ? '0.5px solid var(--z-border)' : 'none',
                    cursor: pending ? 'wait' : 'pointer',
                    background: errored ? '#fff5f5' : 'transparent',
                    transition: 'background 0.1s',
                  }}
                  onMouseOver={(e) => { if (!errored && !pending) (e.currentTarget as HTMLElement).style.background = 'var(--z-bg-page)'; }}
                  onMouseOut={(e) => { if (!errored) (e.currentTarget as HTMLElement).style.background = 'transparent'; }}
                >
                  <input
                    id={`cartera-${c.carteraID}`}
                    type="checkbox"
                    checked={c.asignada}
                    disabled={pending}
                    onChange={() => handleToggle(c.carteraID, c.asignada)}
                    style={{
                      width: 16, height: 16, accentColor: 'var(--z-brand)',
                      cursor: pending ? 'wait' : 'pointer',
                      flexShrink: 0,
                    }}
                  />
                  <span style={{
                    fontSize: '13px',
                    color: errored ? '#dc2626' : 'var(--z-text-primary)',
                    flex: 1,
                  }}>
                    {c.nombre}
                  </span>
                  {pending && (
                    <span style={{
                      width: 12, height: 12,
                      border: '1.5px solid var(--z-border)',
                      borderTopColor: 'var(--z-brand)',
                      borderRadius: '50%',
                      animation: 'spin 0.6s linear infinite',
                      flexShrink: 0,
                    }} />
                  )}
                </label>
              );
            })}
          </div>
        )}

        {errorMensaje && (
          <div style={{
            padding: '8px 12px',
            borderRadius: '6px',
            fontSize: '12px',
            background: '#fff5f5',
            border: '0.5px solid #fecaca',
            color: '#dc2626',
            marginBottom: '14px',
          }}>
            {errorMensaje}
          </div>
        )}

        <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
          <button
            onClick={onCerrar}
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
            Cerrar
          </button>
        </div>
      </div>
      <style>{`@keyframes spin { to { transform: rotate(360deg); } }`}</style>
    </div>
  );
}

// ── Página principal ─────────────────────────────────────────────────────────

export function GestionAgentesPage() {
  const [busqueda, setBusqueda] = useState('');
  const [soloSinCartera, setSoloSinCartera] = useState(false);
  const [agenteSeleccionado, setAgenteSeleccionado] = useState<AgenteAdmin | null>(null);

  const { data: agentes = [], isLoading, isError } = useQuery({
    queryKey: ['agentes'],
    queryFn: listarAgentes,
  });

  const agentesFiltrados = agentes.filter((a) => {
    if (soloSinCartera && a.totalCarterasAsignadas > 0) return false;
    const q = busqueda.trim().toLowerCase();
    if (!q) return true;
    return (
      a.nombreCompleto.toLowerCase().includes(q) ||
      a.numeroDocumento.toString().includes(q) ||
      (a.supervisorNombre ?? '').toLowerCase().includes(q)
    );
  });

  return (
    <div style={{ maxWidth: '960px' }}>
      {/* Encabezado */}
      <div style={{ marginBottom: 24 }}>
        <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
          Gestión de Agentes
        </h1>
        <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
          Asigna las carteras de cada agente. Solo se permiten carteras lideradas por su supervisor.
        </p>
      </div>

      {/* Buscador + filtro */}
      <div style={{ marginBottom: 16, display: 'flex', alignItems: 'center', gap: 16, flexWrap: 'wrap' }}>
        <input
          type="text"
          placeholder="Buscar por nombre, cédula o supervisor..."
          value={busqueda}
          onChange={(e) => setBusqueda(e.target.value)}
          style={{
            flex: 1,
            minWidth: '240px',
            maxWidth: '380px',
            padding: '8px 12px',
            fontSize: '13px',
            border: '1px solid var(--z-border)',
            borderRadius: '6px',
            background: 'var(--z-bg-card)',
            color: 'var(--z-text-primary)',
            outline: 'none',
            boxSizing: 'border-box',
            boxShadow: '0 4px 12px rgba(0,0,0,0.08), 0 1px 3px rgba(0,0,0,0.05)',
          }}
          onFocus={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-brand)'; }}
          onBlur={(e) => { (e.target as HTMLInputElement).style.borderColor = 'var(--z-border)'; }}
        />
        <button
          onClick={() => setSoloSinCartera(v => !v)}
          style={{
            padding: '8px 14px', borderRadius: 6, border: '1px solid var(--z-border)',
            background: soloSinCartera ? '#fff7ed' : 'var(--z-bg-card)',
            color: soloSinCartera ? '#ea580c' : 'var(--z-text-muted)',
            fontSize: 12, fontWeight: soloSinCartera ? 600 : 400, cursor: 'pointer',
            whiteSpace: 'nowrap',
            boxShadow: '0 4px 12px rgba(0,0,0,0.08), 0 1px 3px rgba(0,0,0,0.05)',
          }}
        >
          {soloSinCartera ? '▪ Solo sin cartera' : 'Solo sin cartera'}
        </button>
      </div>

      {/* Tabla */}
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '10px',
        overflow: 'hidden',
      }}>
        {/* Encabezado */}
        <div style={{
          display: 'grid',
          gridTemplateColumns: '1.4fr 1fr 110px 140px',
          columnGap: '16px',
          padding: '10px 16px',
          borderBottom: '2px solid var(--z-brand)',
          background: 'var(--z-brand-light)',
        }}>
          {['Agente', 'Supervisor', 'Carteras', ''].map((h) => (
            <span key={h} style={{
              fontSize: '11px', fontWeight: 600, color: 'var(--z-text-primary)',
              textTransform: 'uppercase', letterSpacing: '0.05em',
            }}>
              {h}
            </span>
          ))}
        </div>

        {isLoading && (
          <div style={{ padding: '32px', textAlign: 'center', color: 'var(--z-text-muted)', fontSize: '13px' }}>
            Cargando agentes...
          </div>
        )}

        {isError && (
          <div style={{ padding: '32px', textAlign: 'center', color: '#dc2626', fontSize: '13px' }}>
            Error al cargar agentes.
          </div>
        )}

        {!isLoading && !isError && agentesFiltrados.length === 0 && (
          <div style={{ padding: '32px', textAlign: 'center', color: 'var(--z-text-muted)', fontSize: '13px' }}>
            {busqueda || soloSinCartera
              ? (soloSinCartera && !busqueda ? 'Todos los agentes tienen al menos una cartera asignada.' : 'Sin resultados para la búsqueda.')
              : 'No hay agentes activos.'}
          </div>
        )}

        {agentesFiltrados.map((a, i) => {
          const noAsignable = !!a.motivoNoAsignable;
          return (
            <div
              key={a.agenteID}
              style={{
                display: 'grid',
                gridTemplateColumns: '1.4fr 1fr 110px 140px',
                columnGap: '16px',
                padding: '12px 16px',
                alignItems: 'center',
                borderBottom: i < agentesFiltrados.length - 1 ? '0.5px solid var(--z-border)' : 'none',
              }}
            >
              <div>
                <p style={{ fontSize: '13px', fontWeight: 500, color: 'var(--z-text-primary)', marginBottom: '2px' }}>
                  {a.nombreCompleto}
                </p>
                <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>
                  CC {a.numeroDocumento}
                </p>
              </div>

              <div>
                {a.supervisorNombre ? (
                  <p style={{ fontSize: '13px', color: 'var(--z-text-primary)' }}>
                    {a.supervisorNombre}
                  </p>
                ) : (
                  <span style={{ fontSize: '12px', color: 'var(--z-text-muted)', fontStyle: 'italic' }}>
                    Sin supervisor
                  </span>
                )}
                {noAsignable && (
                  <span style={{
                    display: 'inline-block',
                    marginTop: 3,
                    padding: '2px 8px',
                    borderRadius: '4px',
                    fontSize: '10px',
                    fontWeight: 600,
                    background: '#fffbeb',
                    color: '#92400e',
                    border: '0.5px solid #fde68a',
                  }}>
                    {a.motivoNoAsignable}
                  </span>
                )}
              </div>

              <span style={{
                fontSize: '13px',
                fontWeight: 500,
                color: a.totalCarterasAsignadas > 0 ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
              }}>
                {a.totalCarterasAsignadas}
              </span>

              <div style={{ display: 'flex', justifyContent: 'flex-end' }}>
                <button
                  onClick={() => !noAsignable && setAgenteSeleccionado(a)}
                  disabled={noAsignable}
                  style={{
                    padding: '5px 10px',
                    borderRadius: '5px',
                    border: '1px solid var(--z-border)',
                    background: 'transparent',
                    fontSize: '11px',
                    color: 'var(--z-text-muted)',
                    cursor: noAsignable ? 'not-allowed' : 'pointer',
                    whiteSpace: 'nowrap',
                    opacity: noAsignable ? 0.4 : 1,
                    transition: 'all 0.15s',
                  }}
                  onMouseOver={(e) => {
                    if (noAsignable) return;
                    (e.currentTarget as HTMLElement).style.borderColor = 'var(--z-brand)';
                    (e.currentTarget as HTMLElement).style.color = 'var(--z-brand)';
                  }}
                  onMouseOut={(e) => {
                    if (noAsignable) return;
                    (e.currentTarget as HTMLElement).style.borderColor = 'var(--z-border)';
                    (e.currentTarget as HTMLElement).style.color = 'var(--z-text-muted)';
                  }}
                >
                  Gestionar carteras
                </button>
              </div>
            </div>
          );
        })}
      </div>

      {/* Modal carteras */}
      {agenteSeleccionado && (
        <ModalCarteras
          agente={agenteSeleccionado}
          onCerrar={() => setAgenteSeleccionado(null)}
        />
      )}
    </div>
  );
}
