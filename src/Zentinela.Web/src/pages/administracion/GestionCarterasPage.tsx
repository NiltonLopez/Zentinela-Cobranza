import { useState, useEffect, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  listarRelaciones, listarLideres, buscarCarteras,
  crearRelacion, eliminarRelacion,
} from '../../api/gestionCarterasApi';
import type { RelacionLiderCartera, LiderDropdown, CarteraAutocomplete } from '../../types/gestionCarteras';

// ─── Modal: Confirmar eliminación ──────────────────────────────────────────────

function ModalEliminar({
  relacion,
  onConfirmar,
  onCancelar,
  cargando,
}: {
  relacion: RelacionLiderCartera;
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
        maxWidth: '440px',
        width: '90%',
        boxShadow: '0 8px 32px rgba(0,0,0,0.12)',
      }}>
        <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '8px', color: 'var(--z-text-primary)' }}>
          Eliminar relación
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', lineHeight: 1.6, marginBottom: '14px' }}>
          ¿Está seguro que desea eliminar{' '}
          <strong style={{ color: 'var(--z-text-primary)' }}>{relacion.nombreCartera}</strong>{' '}
          de{' '}
          <strong style={{ color: 'var(--z-text-primary)' }}>{relacion.nombreLider}</strong>?
        </p>
        {relacion.totalAsignaciones > 0 && (
          <div style={{
            padding: '10px 14px',
            borderRadius: '8px',
            fontSize: '12px',
            background: '#fffbeb',
            border: '0.5px solid #fde68a',
            color: '#92400e',
            marginBottom: '20px',
            lineHeight: 1.5,
          }}>
            Esta cartera tiene{' '}
            <strong>{relacion.totalAsignaciones.toLocaleString('es-CO')}</strong>{' '}
            asignaciones en Titular_Cartera, recuerde reasignarlas.
          </div>
        )}
        {relacion.totalAsignaciones === 0 && <div style={{ marginBottom: '20px' }} />}
        <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
          <button
            onClick={onCancelar}
            disabled={cargando}
            style={{
              padding: '8px 16px', borderRadius: '6px',
              border: '1px solid var(--z-border)', background: 'transparent',
              color: 'var(--z-text-muted)', fontSize: '13px', cursor: 'pointer',
            }}
          >
            Cancelar
          </button>
          <button
            onClick={onConfirmar}
            disabled={cargando}
            style={{
              padding: '8px 16px', borderRadius: '6px',
              border: 'none', background: '#dc2626',
              color: 'white', fontSize: '13px', fontWeight: 600,
              cursor: cargando ? 'not-allowed' : 'pointer',
              opacity: cargando ? 0.7 : 1,
            }}
          >
            {cargando ? 'Eliminando...' : 'Eliminar'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── Modal: Crear relación ──────────────────────────────────────────────────────

function ModalCrear({
  lideres,
  onCrear,
  onCancelar,
  cargando,
  errorServidor,
}: {
  lideres: LiderDropdown[];
  onCrear: (payload: { carteraID?: number; nombreCartera?: string; liderID: number; esExterna?: boolean }) => void;
  onCancelar: () => void;
  cargando: boolean;
  errorServidor: string | null;
}) {
  const [nombreCartera, setNombreCartera] = useState('');
  const [carteraSeleccionada, setCarteraSeleccionada] = useState<CarteraAutocomplete | null>(null);
  const [sugerencias, setSugerencias] = useState<CarteraAutocomplete[]>([]);
  const [mostrarSugerencias, setMostrarSugerencias] = useState(false);
  const [liderID, setLiderID] = useState<number | ''>('');
  // Solo aplica cuando se crea una cartera nueva — si el usuario selecciona una existente,
  // el flag se ignora server-side para no alterar la cartera ya guardada.
  const [esExterna, setEsExterna] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Debounce para el autocompletado de cartera
  useEffect(() => {
    if (carteraSeleccionada) return;
    if (debounceRef.current) clearTimeout(debounceRef.current);
    if (!nombreCartera.trim()) { setSugerencias([]); return; }

    debounceRef.current = setTimeout(async () => {
      const resultados = await buscarCarteras(nombreCartera);
      setSugerencias(resultados);
      setMostrarSugerencias(true);
    }, 250);
  }, [nombreCartera, carteraSeleccionada]);

  function seleccionarSugerencia(c: CarteraAutocomplete) {
    setCarteraSeleccionada(c);
    setNombreCartera(c.nombreCartera);
    setSugerencias([]);
    setMostrarSugerencias(false);
  }

  function limpiarCartera() {
    setCarteraSeleccionada(null);
    setNombreCartera('');
    setSugerencias([]);
  }

  function handleSubmit() {
    if (!liderID) return;
    // EsExterna se persiste en la relación (Lider_Cartera) — aplica también cuando
    // se asocia a una cartera existente, porque la misma cartera puede tener
    // relaciones internas y externas en paralelo según el líder.
    if (carteraSeleccionada) {
      onCrear({ carteraID: carteraSeleccionada.id, liderID: Number(liderID), esExterna });
    } else if (nombreCartera.trim()) {
      onCrear({ nombreCartera: nombreCartera.trim(), liderID: Number(liderID), esExterna });
    }
  }

  const puedeEnviar = !!liderID && (!!carteraSeleccionada || !!nombreCartera.trim());

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
        maxWidth: '440px',
        width: '90%',
        boxShadow: '0 8px 32px rgba(0,0,0,0.12)',
      }}>
        <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '4px', color: 'var(--z-text-primary)' }}>
          Crear Cartera
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '20px', lineHeight: 1.5 }}>
          Selecciona o crea una cartera y asígnala a un líder.
        </p>

        {/* Campo Cartera con autocompletado */}
        <div style={{ marginBottom: '14px' }}>
          <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-muted)', display: 'block', marginBottom: '6px' }}>
            Nombre de la cartera
          </label>
          <div style={{ position: 'relative' }}>
            <input
              type="text"
              placeholder="Escribe para buscar o crear..."
              value={nombreCartera}
              onChange={(e) => { setNombreCartera(e.target.value); setCarteraSeleccionada(null); }}
              onFocus={() => sugerencias.length > 0 && setMostrarSugerencias(true)}
              onBlur={() => setTimeout(() => setMostrarSugerencias(false), 150)}
              style={{
                width: '100%', padding: '8px 32px 8px 11px',
                fontSize: '13px', border: '1px solid var(--z-border)',
                borderRadius: '6px', background: 'var(--z-bg-card)',
                color: 'var(--z-text-primary)', outline: 'none',
                boxSizing: 'border-box',
                borderColor: carteraSeleccionada ? 'var(--z-brand)' : 'var(--z-border)',
              }}
            />
            {carteraSeleccionada && (
              <button
                onClick={limpiarCartera}
                style={{
                  position: 'absolute', right: '8px', top: '50%', transform: 'translateY(-50%)',
                  background: 'none', border: 'none', cursor: 'pointer',
                  fontSize: '14px', color: 'var(--z-text-muted)', lineHeight: 1,
                }}
              >×</button>
            )}
            {/* Sugerencias */}
            {mostrarSugerencias && sugerencias.length > 0 && (
              <div style={{
                position: 'absolute', top: '100%', left: 0, right: 0,
                background: 'var(--z-bg-card)',
                border: '0.5px solid var(--z-border)',
                borderRadius: '6px',
                boxShadow: '0 4px 16px rgba(0,0,0,0.1)',
                zIndex: 10,
                marginTop: '2px',
                overflow: 'hidden',
              }}>
                {sugerencias.map((s, i) => (
                  <div
                    key={s.id}
                    onMouseDown={() => seleccionarSugerencia(s)}
                    style={{
                      padding: '9px 12px',
                      fontSize: '13px',
                      cursor: 'pointer',
                      color: 'var(--z-text-primary)',
                      borderBottom: i < sugerencias.length - 1 ? '0.5px solid var(--z-border)' : 'none',
                    }}
                    onMouseOver={(e) => { (e.currentTarget as HTMLElement).style.background = 'var(--z-bg-surface)'; }}
                    onMouseOut={(e) => { (e.currentTarget as HTMLElement).style.background = 'transparent'; }}
                  >
                    {s.nombreCartera}
                  </div>
                ))}
              </div>
            )}
          </div>
          {carteraSeleccionada && (
            <p style={{ fontSize: '11px', color: 'var(--z-brand)', marginTop: '4px' }}>
              Cartera existente seleccionada — solo se creará la relación.
            </p>
          )}
          {!carteraSeleccionada && nombreCartera.trim() && (
            <p style={{ fontSize: '11px', color: '#d97706', marginTop: '4px' }}>
              Nueva cartera — se creará si no existe ninguna con este nombre.
            </p>
          )}
        </div>

        {/* Selector de Líder */}
        <div style={{ marginBottom: '14px' }}>
          <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-muted)', display: 'block', marginBottom: '6px' }}>
            Líder
          </label>
          <select
            value={liderID}
            onChange={(e) => setLiderID(e.target.value ? Number(e.target.value) : '')}
            style={{
              width: '100%', padding: '8px 11px',
              fontSize: '13px', border: '1px solid var(--z-border)',
              borderRadius: '6px', background: 'var(--z-bg-card)',
              color: liderID ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
              outline: 'none', cursor: 'pointer',
            }}
          >
            <option value="">Selecciona un líder...</option>
            {lideres.map((l) => (
              <option key={l.id} value={l.id}>{l.nombre}</option>
            ))}
          </select>
        </div>

        {/* Toggle Cartera externa — marca esta relación líder-cartera como externa.
            Aplica también cuando se reutiliza una cartera existente, porque la
            misma cartera puede tener relaciones internas y externas en paralelo. */}
        <label
          title="La relación queda fuera de informes y metas administrativas"
          style={{
            display: 'flex', alignItems: 'center', gap: '8px',
            marginBottom: '20px',
            cursor: 'pointer',
            fontSize: '12px',
            color: 'var(--z-text-muted)',
            whiteSpace: 'nowrap',
          }}
        >
          <input
            type="checkbox"
            checked={esExterna}
            onChange={(e) => setEsExterna(e.target.checked)}
            style={{ cursor: 'inherit' }}
          />
          <span>Cartera externa</span>
          <span>— excluida de informes y metas administrativas</span>
        </label>

        {/* Error del servidor */}
        {errorServidor && (
          <div style={{
            padding: '8px 12px', borderRadius: '6px', fontSize: '12px',
            background: '#fff5f5', border: '0.5px solid #fecaca',
            color: '#dc2626', marginBottom: '16px',
          }}>
            {errorServidor}
          </div>
        )}

        <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
          <button
            onClick={onCancelar}
            disabled={cargando}
            style={{
              padding: '8px 16px', borderRadius: '6px',
              border: '1px solid var(--z-border)', background: 'transparent',
              color: 'var(--z-text-muted)', fontSize: '13px', cursor: 'pointer',
            }}
          >
            Cancelar
          </button>
          <button
            onClick={handleSubmit}
            disabled={!puedeEnviar || cargando}
            style={{
              padding: '8px 16px', borderRadius: '6px',
              border: 'none', background: 'var(--z-brand)',
              color: 'white', fontSize: '13px', fontWeight: 600,
              cursor: (!puedeEnviar || cargando) ? 'not-allowed' : 'pointer',
              opacity: (!puedeEnviar || cargando) ? 0.5 : 1,
            }}
          >
            {cargando ? 'Creando...' : 'Crear'}
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── Tarjeta de relación ────────────────────────────────────────────────────────

function TarjetaRelacion({
  relacion,
  activa,
  onSeleccionar,
  onEliminar,
}: {
  relacion: RelacionLiderCartera;
  activa: boolean;
  onSeleccionar: () => void;
  onEliminar: (r: RelacionLiderCartera) => void;
}) {
  return (
    <div
      onClick={onSeleccionar}
      style={{
        position: 'relative',
        background: 'var(--z-bg-card)',
        border: `0.5px solid ${activa ? 'var(--z-brand)' : 'var(--z-border)'}`,
        borderRadius: '10px',
        padding: '16px',
        cursor: 'pointer',
        transition: 'border-color 0.15s, box-shadow 0.15s',
        boxShadow: activa ? '0 0 0 2px rgba(37,99,235,0.12)' : 'none',
        userSelect: 'none',
      }}
    >
      {/* Botón X — visible solo cuando la tarjeta está activa */}
      {activa && (
        <button
          onClick={(e) => { e.stopPropagation(); onEliminar(relacion); }}
          style={{
            position: 'absolute', top: '8px', right: '8px',
            width: '22px', height: '22px',
            borderRadius: '50%', border: 'none',
            background: '#dc2626', color: 'white',
            fontSize: '13px', fontWeight: 700,
            cursor: 'pointer', lineHeight: 1,
            display: 'flex', alignItems: 'center', justifyContent: 'center',
          }}
        >
          ×
        </button>
      )}

      <div style={{
        display: 'flex', alignItems: 'center', gap: '6px',
        marginBottom: '6px',
        paddingRight: activa ? '24px' : '0',
      }}>
        <p style={{
          fontSize: '13px', fontWeight: 600,
          color: 'var(--z-text-primary)', margin: 0,
        }}>
          {relacion.nombreCartera}
        </p>
        {relacion.esExterna && (
          <span title="Cartera externa — agencias/personal externo" style={{
            fontSize: '10px', fontWeight: 600,
            padding: '1px 6px', borderRadius: '4px',
            background: '#FFF7ED', color: '#C2410C',
            textTransform: 'uppercase', letterSpacing: '0.04em',
            flexShrink: 0,
          }}>
            Externa
          </span>
        )}
      </div>

      <p style={{ fontSize: '12px', color: 'var(--z-text-muted)', marginBottom: '10px' }}>
        {relacion.nombreLider}
      </p>

      <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap' }}>
        <span style={{
          display: 'inline-block',
          fontSize: '11px', fontWeight: 500,
          padding: '2px 8px', borderRadius: '4px',
          background: relacion.totalAsignaciones > 0 ? '#EFF6FF' : '#F5F5F4',
          color: relacion.totalAsignaciones > 0 ? '#2563EB' : '#78716C',
        }}>
          {relacion.totalAsignaciones.toLocaleString('es-CO')} asignaciones
        </span>
        <span style={{
          display: 'inline-block',
          fontSize: '11px', fontWeight: 500,
          padding: '2px 8px', borderRadius: '4px',
          background: relacion.totalAsesores > 0 ? '#F0FDF4' : '#F5F5F4',
          color: relacion.totalAsesores > 0 ? '#16A34A' : '#78716C',
        }}>
          {relacion.totalAsesores} asesor{relacion.totalAsesores !== 1 ? 'es' : ''}
        </span>
      </div>
    </div>
  );
}

// ─── Página principal ───────────────────────────────────────────────────────────

export function GestionCarterasPage() {
  const [busqueda, setBusqueda] = useState('');
  const [tarjetaActiva, setTarjetaActiva] = useState<string | null>(null);
  const [relacionAEliminar, setRelacionAEliminar] = useState<RelacionLiderCartera | null>(null);
  const [mostrarCrear, setMostrarCrear] = useState(false);
  const [errorCrear, setErrorCrear] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<{ tipo: 'exito' | 'error'; texto: string } | null>(null);
  const queryClient = useQueryClient();

  const { data: relaciones = [], isLoading, isError } = useQuery({
    queryKey: ['gestion-carteras-relaciones'],
    queryFn: listarRelaciones,
  });

  const { data: lideres = [] } = useQuery({
    queryKey: ['gestion-carteras-lideres'],
    queryFn: listarLideres,
    enabled: mostrarCrear,
  });

  const mutacionEliminar = useMutation({
    mutationFn: ({ liderID, carteraID }: { liderID: number; carteraID: number }) =>
      eliminarRelacion(liderID, carteraID),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['gestion-carteras-relaciones'] });
      setFeedback({ tipo: 'exito', texto: 'Relación eliminada correctamente.' });
      setRelacionAEliminar(null);
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'No se pudo eliminar la relación. Intenta de nuevo.';
      setFeedback({ tipo: 'error', texto: msg });
      setRelacionAEliminar(null);
    },
  });

  const mutacionCrear = useMutation({
    mutationFn: crearRelacion,
    onSuccess: (nueva) => {
      queryClient.invalidateQueries({ queryKey: ['gestion-carteras-relaciones'] });
      setFeedback({ tipo: 'exito', texto: `Relación ${nueva.nombreCartera} — ${nueva.nombreLider} creada.` });
      setMostrarCrear(false);
      setErrorCrear(null);
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'No se pudo crear la relación.';
      setErrorCrear(msg);
    },
  });

  const relacionesFiltradas = relaciones.filter((r) => {
    const q = busqueda.toLowerCase();
    return r.nombreCartera.toLowerCase().includes(q) || r.nombreLider.toLowerCase().includes(q);
  });

  return (
    <div style={{ maxWidth: '1000px' }}>
      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12, marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Gestión de Líderes y Carteras
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Administra las relaciones entre líderes y carteras. Haz clic en una tarjeta para activar su edición.
          </p>
        </div>
        <button
          onClick={() => { setMostrarCrear(true); setErrorCrear(null); }}
          style={{
            padding: '8px 16px', borderRadius: '6px',
            border: 'none', background: 'var(--z-brand)',
            color: 'white', fontSize: '13px', fontWeight: 600,
            cursor: 'pointer', whiteSpace: 'nowrap', flexShrink: 0,
          }}
        >
          + Crear Cartera
        </button>
      </div>

      {/* Feedback */}
      {feedback && (
        <div style={{
          padding: '10px 14px', borderRadius: '8px', fontSize: '13px',
          marginBottom: '16px',
          background: feedback.tipo === 'exito' ? '#f0fdf4' : '#fff5f5',
          border: `0.5px solid ${feedback.tipo === 'exito' ? '#bbf7d0' : '#fecaca'}`,
          color: feedback.tipo === 'exito' ? '#16a34a' : '#dc2626',
          display: 'flex', justifyContent: 'space-between', alignItems: 'center',
        }}>
          {feedback.texto}
          <button
            onClick={() => setFeedback(null)}
            style={{ background: 'none', border: 'none', cursor: 'pointer', fontSize: '14px', color: 'inherit', lineHeight: 1 }}
          >×</button>
        </div>
      )}

      {/* Buscador */}
      <div style={{ marginBottom: '20px' }}>
        <input
          type="text"
          placeholder="Buscar por cartera o líder..."
          value={busqueda}
          onChange={(e) => setBusqueda(e.target.value)}
          style={{
            width: '100%', maxWidth: '360px', padding: '8px 12px',
            fontSize: '13px', border: '1px solid var(--z-border)',
            borderRadius: '6px', background: 'var(--z-bg-card)',
            color: 'var(--z-text-primary)', outline: 'none',
          }}
          onFocus={(e) => { e.currentTarget.style.borderColor = 'var(--z-brand)'; }}
          onBlur={(e) => { e.currentTarget.style.borderColor = 'var(--z-border)'; }}
        />
        {!isLoading && (
          <span style={{ fontSize: '12px', color: 'var(--z-text-muted)', marginLeft: '12px' }}>
            {relacionesFiltradas.length} relación{relacionesFiltradas.length !== 1 ? 'es' : ''}
          </span>
        )}
      </div>

      {/* Estados de carga / error / vacío */}
      {isLoading && (
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', padding: '32px 0', textAlign: 'center' }}>
          Cargando relaciones...
        </p>
      )}
      {isError && (
        <p style={{ fontSize: '13px', color: '#dc2626', padding: '32px 0', textAlign: 'center' }}>
          Error al cargar las relaciones.
        </p>
      )}
      {!isLoading && !isError && relacionesFiltradas.length === 0 && (
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', padding: '32px 0', textAlign: 'center' }}>
          {busqueda ? 'Sin resultados para la búsqueda.' : 'No hay relaciones registradas.'}
        </p>
      )}

      {/* Grid de tarjetas */}
      {!isLoading && !isError && relacionesFiltradas.length > 0 && (
        <div style={{
          display: 'grid',
          gridTemplateColumns: 'repeat(auto-fill, minmax(220px, 1fr))',
          gap: '12px',
        }}>
          {relacionesFiltradas.map((r) => {
            const key = `${r.liderID}-${r.carteraID}`;
            return (
              <TarjetaRelacion
                key={key}
                relacion={r}
                activa={tarjetaActiva === key}
                onSeleccionar={() => setTarjetaActiva(tarjetaActiva === key ? null : key)}
                onEliminar={(rel) => { setRelacionAEliminar(rel); setTarjetaActiva(null); }}
              />
            );
          })}
        </div>
      )}

      {/* Modal eliminar */}
      {relacionAEliminar && (
        <ModalEliminar
          relacion={relacionAEliminar}
          cargando={mutacionEliminar.isPending}
          onConfirmar={() => mutacionEliminar.mutate({
            liderID: relacionAEliminar.liderID,
            carteraID: relacionAEliminar.carteraID,
          })}
          onCancelar={() => setRelacionAEliminar(null)}
        />
      )}

      {/* Modal crear */}
      {mostrarCrear && (
        <ModalCrear
          lideres={lideres}
          cargando={mutacionCrear.isPending}
          errorServidor={errorCrear}
          onCrear={(payload) => mutacionCrear.mutate(payload)}
          onCancelar={() => { setMostrarCrear(false); setErrorCrear(null); }}
        />
      )}
    </div>
  );
}
