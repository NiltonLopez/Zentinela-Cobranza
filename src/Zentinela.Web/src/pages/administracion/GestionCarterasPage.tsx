import { useState, useEffect, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  listarRelaciones, listarLideres, buscarCarteras,
  crearRelacion, eliminarRelacion, inactivarRelacion, reactivarRelacion, descargarAsignacion,
  listarEmpleadosElegibles, crearLider, crearLiderExterno,
} from '../../api/gestionCarterasApi';
import type {
  RelacionLiderCartera, LiderDropdown, CarteraAutocomplete, EmpleadoElegible,
  CrearLiderExternoRequest, CrearLiderExternoResponse,
} from '../../types/gestionCarteras';

// ─── Modal: Retirar relación (X inteligente) ────────────────────────────────────
// Una sola X decide la acción según el histórico de la relación (ADR-022):
//   - Con histórico (pagos/metas/mensajería/IVR) ⇒ INACTIVAR (soft): conserva el
//     histórico, vacía titulares, reversible. No se ofrece borrar — rompería la
//     trazabilidad del recaudo.
//   - Sin histórico ⇒ ELIMINAR (hard): borrado físico real, salida de escape para
//     un alta creada por error.

function ModalRetirar({
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
  const esSoft = relacion.tieneHistorico;
  const titulo = esSoft ? 'Inactivar relación' : 'Eliminar relación';
  const verboInf = esSoft ? 'inactivar' : 'eliminar';
  const colorAccion = esSoft ? '#d97706' : '#dc2626';
  const textoBoton = cargando
    ? (esSoft ? 'Inactivando...' : 'Eliminando...')
    : (esSoft ? 'Inactivar' : 'Eliminar');

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
          {titulo}
        </p>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', lineHeight: 1.6, marginBottom: '14px' }}>
          ¿Está seguro que desea {verboInf}{' '}
          <strong style={{ color: 'var(--z-text-primary)' }}>{relacion.nombreCartera}</strong>{' '}
          de{' '}
          <strong style={{ color: 'var(--z-text-primary)' }}>{relacion.nombreLider}</strong>?
        </p>
        {esSoft ? (
          <div style={{
            padding: '10px 14px', borderRadius: '8px', fontSize: '12px',
            background: '#fffbeb', border: '0.5px solid #fde68a',
            color: '#92400e', marginBottom: '20px', lineHeight: 1.5,
          }}>
            Esta relación tiene histórico (pagos, metas o envíos) que debe conservarse.
            Se <strong>inactivará</strong>: desaparece de lo operativo
            {relacion.totalAsignaciones > 0 && (
              <> y se vacían sus <strong>{relacion.totalAsignaciones.toLocaleString('es-CO')}</strong> asignaciones</>
            )}
            , pero su histórico queda intacto. Podrás reactivarla cuando quieras.
          </div>
        ) : (
          <div style={{
            padding: '10px 14px', borderRadius: '8px', fontSize: '12px',
            background: '#fff5f5', border: '0.5px solid #fecaca',
            color: '#991b1b', marginBottom: '20px', lineHeight: 1.5,
          }}>
            Esta relación no tiene histórico, se <strong>eliminará permanentemente</strong>.
            Esta acción no se puede deshacer.
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
            onClick={onConfirmar}
            disabled={cargando}
            style={{
              padding: '8px 16px', borderRadius: '6px',
              border: 'none', background: colorAccion,
              color: 'white', fontSize: '13px', fontWeight: 600,
              cursor: cargando ? 'not-allowed' : 'pointer',
              opacity: cargando ? 0.7 : 1,
            }}
          >
            {textoBoton}
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
  onReactivar,
  onDescargar,
  estaDescargando,
  estaReactivando,
}: {
  relacion: RelacionLiderCartera;
  activa: boolean;
  onSeleccionar: () => void;
  onEliminar: (r: RelacionLiderCartera) => void;
  onReactivar: (r: RelacionLiderCartera) => void;
  onDescargar: (r: RelacionLiderCartera) => void;
  estaDescargando: boolean;
  estaReactivando: boolean;
}) {
  const inactiva = !relacion.activo;
  return (
    <div
      onClick={onSeleccionar}
      style={{
        position: 'relative',
        background: inactiva ? 'var(--z-bg-surface)' : 'var(--z-bg-card)',
        border: `0.5px solid ${activa ? 'var(--z-brand)' : 'var(--z-border)'}`,
        borderRadius: '10px',
        padding: '16px',
        cursor: 'pointer',
        transition: 'border-color 0.15s, box-shadow 0.15s',
        boxShadow: activa ? '0 0 0 2px rgba(37,99,235,0.12)' : 'none',
        userSelect: 'none',
        opacity: inactiva ? 0.72 : 1,
      }}
    >
      {/* Botones de acción — visibles solo cuando la tarjeta está activa (seleccionada) */}
      {activa && (
        <div
          style={{
            position: 'absolute', top: '8px', right: '8px',
            display: 'flex', gap: '4px',
          }}
          onClick={(e) => e.stopPropagation()}
        >
          {inactiva ? (
            // Relación retirada: única acción es Reactivar (ADR-022).
            <button
              title={estaReactivando ? 'Reactivando...' : 'Reactivar relación'}
              onClick={() => !estaReactivando && onReactivar(relacion)}
              style={{
                height: '22px', padding: '0 10px',
                borderRadius: '11px', border: 'none',
                background: estaReactivando ? '#86efac' : '#16a34a',
                color: 'white', fontSize: '11px', fontWeight: 700,
                cursor: estaReactivando ? 'not-allowed' : 'pointer',
                lineHeight: 1,
                display: 'flex', alignItems: 'center', justifyContent: 'center',
              }}
            >
              ↻ Reactivar
            </button>
          ) : (
            <>
              {relacion.totalAsignaciones > 0 && (
                <button
                  title={estaDescargando ? 'Descargando...' : 'Descargar asignación actual'}
                  onClick={() => !estaDescargando && onDescargar(relacion)}
                  style={{
                    width: '22px', height: '22px',
                    borderRadius: '50%', border: 'none',
                    background: estaDescargando ? '#93c5fd' : '#2563eb',
                    color: 'white',
                    fontSize: '12px', fontWeight: 700,
                    cursor: estaDescargando ? 'not-allowed' : 'pointer',
                    lineHeight: 1,
                    display: 'flex', alignItems: 'center', justifyContent: 'center',
                  }}
                >
                  ↓
                </button>
              )}
              <button
                // Título adaptativo: con histórico la X inactiva (soft), sin histórico elimina (hard).
                title={relacion.tieneHistorico ? 'Inactivar relación (conserva histórico)' : 'Eliminar relación'}
                onClick={() => onEliminar(relacion)}
                style={{
                  width: '22px', height: '22px',
                  borderRadius: '50%', border: 'none',
                  background: relacion.tieneHistorico ? '#d97706' : '#dc2626', color: 'white',
                  fontSize: '13px', fontWeight: 700,
                  cursor: 'pointer', lineHeight: 1,
                  display: 'flex', alignItems: 'center', justifyContent: 'center',
                }}
              >
                ×
              </button>
            </>
          )}
        </div>
      )}

      <div style={{
        display: 'flex', alignItems: 'center', gap: '6px',
        marginBottom: '6px',
        paddingRight: activa ? '54px' : '0',
      }}>
        <p style={{
          fontSize: '13px', fontWeight: 600,
          color: 'var(--z-text-primary)', margin: 0,
        }}>
          {relacion.nombreCartera}
        </p>
        {inactiva && (
          <span title="Relación inactivada — retirada de lo operativo, histórico conservado" style={{
            fontSize: '10px', fontWeight: 600,
            padding: '1px 6px', borderRadius: '4px',
            background: '#F5F5F4', color: '#78716C',
            textTransform: 'uppercase', letterSpacing: '0.04em',
            flexShrink: 0,
          }}>
            Inactiva
          </span>
        )}
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

// ─── Modal: Agregar Líder ────────────────────────────────────────────────────────
// Da de alta en la tabla Lider (DBCallCenter) a un empleado de RRHH cuyo cargo sea
// de líder de cobranza (Líder / Coordinador / Director Operativo). El backend
// resuelve nombre, email y cargo desde Empleado a partir de la cédula.
const inputModal: React.CSSProperties = {
  width: '100%', padding: '8px 11px', fontSize: '13px',
  border: '1px solid var(--z-border)', borderRadius: '6px',
  background: 'var(--z-bg-card)', color: 'var(--z-text-primary)',
  outline: 'none', boxSizing: 'border-box',
};

function ModalAgregarLider({
  empleados,
  cargandoEmpleados,
  onCrear,
  onCrearExterno,
  onCancelar,
  cargando,
  errorServidor,
  credenciales,
}: {
  empleados: EmpleadoElegible[];
  cargandoEmpleados: boolean;
  onCrear: (numeroDocumento: number) => void;
  onCrearExterno: (payload: CrearLiderExternoRequest) => void;
  onCancelar: () => void;
  cargando: boolean;
  errorServidor: string | null;
  credenciales: CrearLiderExternoResponse | null;
}) {
  const [documento, setDocumento] = useState<number | ''>('');
  const [esExterno, setEsExterno] = useState(false);
  const [nombreExt, setNombreExt] = useState('');
  const [documentoExt, setDocumentoExt] = useState('');
  const [emailExt, setEmailExt] = useState('');
  const [copiado, setCopiado] = useState(false);

  const nombreExtOk = nombreExt.trim().length >= 3;
  const puedeCrear = cargando
    ? false
    : (esExterno ? nombreExtOk : !!documento);

  const enviar = () => {
    if (esExterno) {
      if (!nombreExtOk) return;
      const doc = documentoExt.trim();
      onCrearExterno({
        nombre: nombreExt.trim(),
        numeroDocumento: doc ? Number(doc) : null,
        email: emailExt.trim() || null,
      });
    } else if (documento) {
      onCrear(Number(documento));
    }
  };

  const copiarCredenciales = async () => {
    if (!credenciales) return;
    const clave = credenciales.passwordTemporal ?? '(el documento del líder)';
    await navigator.clipboard.writeText(`Usuario: ${credenciales.usuario}\nContraseña: ${clave}`);
    setCopiado(true);
    setTimeout(() => setCopiado(false), 1500);
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
        maxWidth: '440px',
        width: '90%',
        boxShadow: '0 8px 32px rgba(0,0,0,0.12)',
      }}>
        {credenciales ? (
          /* ── Éxito: líder externo creado — mostrar credenciales una sola vez ── */
          <>
            <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '4px', color: '#15803d' }}>
              Líder externo creado
            </p>
            <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '16px', lineHeight: 1.5 }}>
              Se creó <strong>{credenciales.nombre}</strong> y su usuario Zentinela con rol
              <strong> Líder Externo</strong>. Entrégale estas credenciales — deberá cambiar la
              contraseña en el primer inicio de sesión.
            </p>

            <div style={{
              background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)',
              borderRadius: '8px', padding: '14px 16px', marginBottom: '16px',
              display: 'flex', flexDirection: 'column', gap: '8px',
            }}>
              <div style={{ display: 'flex', gap: '8px', fontSize: '13px' }}>
                <span style={{ color: 'var(--z-text-muted)', minWidth: 84 }}>Usuario</span>
                <span style={{ fontFamily: 'monospace', fontWeight: 600, color: 'var(--z-text-primary)' }}>{credenciales.usuario}</span>
              </div>
              <div style={{ display: 'flex', gap: '8px', fontSize: '13px' }}>
                <span style={{ color: 'var(--z-text-muted)', minWidth: 84 }}>Contraseña</span>
                {credenciales.passwordTemporal ? (
                  <span style={{ fontFamily: 'monospace', fontWeight: 600, color: '#b45309' }}>{credenciales.passwordTemporal}</span>
                ) : (
                  <span style={{ color: 'var(--z-text-primary)' }}>El documento del líder</span>
                )}
              </div>
              {credenciales.passwordTemporal && (
                <p style={{ margin: '2px 0 0', fontSize: '11px', color: '#b45309' }}>
                  ⚠ Guárdala ahora — no se volverá a mostrar.
                </p>
              )}
            </div>

            <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
              <button
                onClick={copiarCredenciales}
                style={{
                  padding: '8px 16px', borderRadius: '6px',
                  border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                  color: copiado ? '#15803d' : 'var(--z-text-primary)', fontSize: '13px', cursor: 'pointer',
                }}
              >
                {copiado ? '✓ Copiado' : 'Copiar credenciales'}
              </button>
              <button
                onClick={onCancelar}
                style={{
                  padding: '8px 16px', borderRadius: '6px', border: 'none',
                  background: 'var(--z-brand)', color: '#fff', fontSize: '13px', fontWeight: 600, cursor: 'pointer',
                }}
              >
                Listo
              </button>
            </div>
          </>
        ) : (
          /* ── Formulario de alta (interno o externo) ── */
          <>
            <p style={{ fontSize: '15px', fontWeight: 600, marginBottom: '4px', color: 'var(--z-text-primary)' }}>
              Agregar Líder
            </p>
            <p style={{ fontSize: '13px', color: 'var(--z-text-muted)', marginBottom: '16px', lineHeight: 1.5 }}>
              {esExterno
                ? 'Registra un líder externo (agencia / no empleado). Se creará también su usuario Zentinela con rol Líder Externo.'
                : 'Selecciona un empleado con cargo de líder (Líder de Cobranza, Coordinador de Cartera o Director Operativo).'}
            </p>

            {/* Toggle externo */}
            <label style={{
              display: 'flex', alignItems: 'center', gap: '8px', marginBottom: '16px',
              cursor: 'pointer', fontSize: '13px', color: 'var(--z-text-primary)',
            }}>
              <input
                type="checkbox"
                checked={esExterno}
                onChange={(e) => setEsExterno(e.target.checked)}
                style={{ cursor: 'pointer', width: 16, height: 16 }}
              />
              Es líder externo (no es empleado)
            </label>

            {esExterno ? (
              /* Formulario externo */
              <div style={{ display: 'flex', flexDirection: 'column', gap: '12px', marginBottom: '18px' }}>
                <div>
                  <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-muted)', display: 'block', marginBottom: '4px' }}>
                    Nombre completo *
                  </label>
                  <input
                    type="text" value={nombreExt} placeholder="Nombre y apellido del líder externo"
                    onChange={(e) => setNombreExt(e.target.value)} style={inputModal}
                  />
                </div>
                <div>
                  <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-muted)', display: 'block', marginBottom: '4px' }}>
                    Documento / NIT (opcional)
                  </label>
                  <input
                    type="text" inputMode="numeric" value={documentoExt} placeholder="Solo dígitos — se usará como contraseña inicial"
                    onChange={(e) => setDocumentoExt(e.target.value.replace(/[^0-9]/g, ''))} style={inputModal}
                  />
                </div>
                <div>
                  <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-muted)', display: 'block', marginBottom: '4px' }}>
                    Email (opcional)
                  </label>
                  <input
                    type="email" value={emailExt} placeholder="correo@ejemplo.com"
                    onChange={(e) => setEmailExt(e.target.value)} style={inputModal}
                  />
                </div>
                <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', margin: 0, lineHeight: 1.5 }}>
                  Si indicas documento, será la contraseña inicial. Si no, el sistema generará una
                  contraseña temporal y te la mostrará al crear.
                </p>
              </div>
            ) : (
              /* Selector de empleado elegible */
              <div style={{ marginBottom: '18px' }}>
                <label style={{ fontSize: '12px', fontWeight: 600, color: 'var(--z-text-muted)', display: 'block', marginBottom: '6px' }}>
                  Empleado
                </label>
                {cargandoEmpleados ? (
                  <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>Cargando empleados...</p>
                ) : empleados.length === 0 ? (
                  <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
                    No hay empleados con cargo de líder pendientes de dar de alta.
                  </p>
                ) : (
                  <select
                    value={documento}
                    onChange={(e) => setDocumento(e.target.value ? Number(e.target.value) : '')}
                    style={{ ...inputModal, color: documento ? 'var(--z-text-primary)' : 'var(--z-text-muted)', cursor: 'pointer' }}
                  >
                    <option value="">Selecciona un empleado...</option>
                    {empleados.map((e) => (
                      <option key={e.numeroDocumento} value={e.numeroDocumento}>
                        {e.nombreCompleto} — {e.cargo}
                      </option>
                    ))}
                  </select>
                )}
              </div>
            )}

            {/* Error del servidor */}
            {errorServidor && (
              <div style={{
                padding: '8px 12px', borderRadius: '6px', fontSize: '12px',
                background: '#fff5f5', border: '0.5px solid #fecaca', color: '#dc2626',
                marginBottom: '16px',
              }}>
                {errorServidor}
              </div>
            )}

            {/* Acciones */}
            <div style={{ display: 'flex', gap: '10px', justifyContent: 'flex-end' }}>
              <button
                onClick={onCancelar}
                style={{
                  padding: '8px 16px', borderRadius: '6px',
                  border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                  color: 'var(--z-text-muted)', fontSize: '13px', cursor: 'pointer',
                }}
              >
                Cancelar
              </button>
              <button
                onClick={enviar}
                disabled={!puedeCrear}
                style={{
                  padding: '8px 16px', borderRadius: '6px', border: 'none',
                  background: puedeCrear ? 'var(--z-brand)' : 'var(--z-border)',
                  color: puedeCrear ? '#fff' : 'var(--z-text-muted)',
                  fontSize: '13px', fontWeight: 600,
                  cursor: puedeCrear ? 'pointer' : 'not-allowed',
                }}
              >
                {cargando ? 'Agregando...' : (esExterno ? 'Crear Líder Externo' : 'Agregar Líder')}
              </button>
            </div>
          </>
        )}
      </div>
    </div>
  );
}

// ─── Página principal ───────────────────────────────────────────────────────────

export function GestionCarterasPage() {
  const [busqueda, setBusqueda] = useState('');
  const [tarjetaActiva, setTarjetaActiva] = useState<string | null>(null);
  const [relacionARetirar, setRelacionARetirar] = useState<RelacionLiderCartera | null>(null);
  const [mostrarCrear, setMostrarCrear] = useState(false);
  const [errorCrear, setErrorCrear] = useState<string | null>(null);
  const [mostrarAgregarLider, setMostrarAgregarLider] = useState(false);
  const [errorLider, setErrorLider] = useState<string | null>(null);
  const [credencialesLiderExterno, setCredencialesLiderExterno] = useState<CrearLiderExternoResponse | null>(null);
  const [feedback, setFeedback] = useState<{ tipo: 'exito' | 'error'; texto: string } | null>(null);
  const [descargando, setDescargando] = useState<string | null>(null);  // key de la tarjeta en descarga
  const [reactivando, setReactivando] = useState<string | null>(null);  // key de la tarjeta reactivándose
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

  const { data: empleadosElegibles = [], isLoading: cargandoElegibles } = useQuery({
    queryKey: ['gestion-carteras-empleados-elegibles'],
    queryFn: listarEmpleadosElegibles,
    enabled: mostrarAgregarLider,
  });

  // Retirar (X inteligente): inactiva si la relación tiene histórico, elimina si no (ADR-022).
  const mutacionRetirar = useMutation({
    mutationFn: (rel: RelacionLiderCartera) =>
      rel.tieneHistorico
        ? inactivarRelacion(rel.liderID, rel.carteraID)
        : eliminarRelacion(rel.liderID, rel.carteraID),
    onSuccess: (_data, rel) => {
      queryClient.invalidateQueries({ queryKey: ['gestion-carteras-relaciones'] });
      setFeedback({
        tipo: 'exito',
        texto: rel.tieneHistorico
          ? 'Relación inactivada. Su histórico se conserva.'
          : 'Relación eliminada correctamente.',
      });
      setRelacionARetirar(null);
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'No se pudo completar la acción. Intenta de nuevo.';
      setFeedback({ tipo: 'error', texto: msg });
      setRelacionARetirar(null);
    },
  });

  const mutacionReactivar = useMutation({
    mutationFn: (rel: RelacionLiderCartera) => reactivarRelacion(rel.liderID, rel.carteraID),
    onMutate: (rel) => setReactivando(`${rel.liderID}-${rel.carteraID}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['gestion-carteras-relaciones'] });
      setFeedback({ tipo: 'exito', texto: 'Relación reactivada correctamente.' });
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'No se pudo reactivar la relación.';
      setFeedback({ tipo: 'error', texto: msg });
    },
    onSettled: () => setReactivando(null),
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

  const mutacionCrearLider = useMutation({
    mutationFn: crearLider,
    onSuccess: (lider) => {
      // El nuevo líder aparece en el selector de "Crear Cartera" y deja de ser elegible.
      queryClient.invalidateQueries({ queryKey: ['gestion-carteras-lideres'] });
      queryClient.invalidateQueries({ queryKey: ['gestion-carteras-empleados-elegibles'] });
      setFeedback({ tipo: 'exito', texto: `Líder '${lider.nombre}' agregado correctamente.` });
      setMostrarAgregarLider(false);
      setErrorLider(null);
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'No se pudo agregar el líder.';
      setErrorLider(msg);
    },
  });

  const mutacionCrearLiderExterno = useMutation({
    mutationFn: crearLiderExterno,
    onSuccess: (resp) => {
      queryClient.invalidateQueries({ queryKey: ['gestion-carteras-lideres'] });
      setErrorLider(null);
      // No se cierra el modal: se muestran las credenciales para copiarlas una sola vez.
      setCredencialesLiderExterno(resp);
      setFeedback({ tipo: 'exito', texto: `Líder externo '${resp.nombre}' creado correctamente.` });
    },
    onError: (err: unknown) => {
      const msg = (err as { response?: { data?: { mensaje?: string } } })
        ?.response?.data?.mensaje ?? 'No se pudo crear el líder externo.';
      setErrorLider(msg);
    },
  });

  const cerrarModalLider = () => {
    setMostrarAgregarLider(false);
    setErrorLider(null);
    setCredencialesLiderExterno(null);
  };

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
        <div style={{ display: 'flex', gap: '10px', flexShrink: 0 }}>
          <button
            onClick={() => { setMostrarAgregarLider(true); setErrorLider(null); setCredencialesLiderExterno(null); }}
            style={{
              padding: '8px 16px', borderRadius: '6px',
              border: '0.5px solid var(--z-brand)', background: 'var(--z-bg-page)',
              color: 'var(--z-brand)', fontSize: '13px', fontWeight: 600,
              cursor: 'pointer', whiteSpace: 'nowrap',
            }}
          >
            + Agregar Líder
          </button>
          <button
            onClick={() => { setMostrarCrear(true); setErrorCrear(null); }}
            style={{
              padding: '8px 16px', borderRadius: '6px',
              border: 'none', background: 'var(--z-brand)',
              color: 'white', fontSize: '13px', fontWeight: 600,
              cursor: 'pointer', whiteSpace: 'nowrap',
            }}
          >
            + Crear Cartera
          </button>
        </div>
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
                estaDescargando={descargando === key}
                estaReactivando={reactivando === key}
                onSeleccionar={() => setTarjetaActiva(tarjetaActiva === key ? null : key)}
                onEliminar={(rel) => { setRelacionARetirar(rel); setTarjetaActiva(null); }}
                onReactivar={(rel) => mutacionReactivar.mutate(rel)}
                onDescargar={async (rel) => {
                  if (descargando) return;
                  setDescargando(key);
                  try {
                    await descargarAsignacion(rel.liderID, rel.carteraID, rel.nombreCartera, rel.nombreLider);
                  } catch {
                    setFeedback({ tipo: 'error', texto: 'No se pudo descargar la asignación.' });
                  } finally {
                    setDescargando(null);
                  }
                }}
              />
            );
          })}
        </div>
      )}

      {/* Modal retirar (X inteligente: inactivar si hay histórico, eliminar si no) */}
      {relacionARetirar && (
        <ModalRetirar
          relacion={relacionARetirar}
          cargando={mutacionRetirar.isPending}
          onConfirmar={() => mutacionRetirar.mutate(relacionARetirar)}
          onCancelar={() => setRelacionARetirar(null)}
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

      {/* Modal agregar líder (interno o externo) */}
      {mostrarAgregarLider && (
        <ModalAgregarLider
          empleados={empleadosElegibles}
          cargandoEmpleados={cargandoElegibles}
          cargando={mutacionCrearLider.isPending || mutacionCrearLiderExterno.isPending}
          errorServidor={errorLider}
          credenciales={credencialesLiderExterno}
          onCrear={(numeroDocumento) => mutacionCrearLider.mutate(numeroDocumento)}
          onCrearExterno={(payload) => mutacionCrearLiderExterno.mutate(payload)}
          onCancelar={cerrarModalLider}
        />
      )}
    </div>
  );
}
