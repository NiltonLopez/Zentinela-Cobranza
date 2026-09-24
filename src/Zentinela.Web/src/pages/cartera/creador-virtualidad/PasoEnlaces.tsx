import { useEffect, useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { CargandoSpiral } from '../../../components/CargandoSpiral';
import { TH, TD, btnPrimario, btnSecundario, cajaError } from '../../administracion/historico-virtualidad/compartidos';
import { obtenerAsesoresDelLider, obtenerAgentesActivos, guardarContactosAsesores, obtenerMensajesDisponibles, mensajeDeErrorBlob } from '../../../api/creadorVirtualidadApi';
import type { EstadoAsistente, Accion, FilaAsesor } from './estadoAsistente';
import type { AsesorEstrategia } from '../../../types/creadorVirtualidad';
import { asesorValido, linkWaMe, linkMasLargo, CELULAR_REGEX, MOVIL_REGEX, siguienteIDManual } from './estadoAsistente';
import { C, inputStyle, etiquetaPaso, Aviso, BarraAcciones } from './compartidosSms';

interface Props {
  estado: EstadoAsistente;
  dispatch: React.Dispatch<Accion>;
}

export function PasoEnlaces({ estado, dispatch }: Props) {
  const [progreso, setProgreso] = useState(0);
  const [error, setError]       = useState<string | null>(null);
  const [agregarID, setAgregarID] = useState<number>(0);
  // Al añadir, se remonta el buscador para que vuelva vacío (sin setState en efectos).
  const [buscadorKey, setBuscadorKey] = useState(0);

  const { data: precargados, isLoading } = useQuery({
    queryKey: ['sms-asesores', estado.liderID],
    queryFn:  () => obtenerAsesoresDelLider(estado.liderID),
  });

  const { data: activos = [] } = useQuery({
    queryKey: ['sms-agentes-activos'],
    queryFn:  obtenerAgentesActivos,
    staleTime: 10 * 60 * 1000,
  });

  // Precarga una sola vez por líder: si el usuario ya editó filas, no se pisan.
  useEffect(() => {
    if (!precargados || estado.asesores.length > 0) return;
    dispatch({
      tipo: 'SET_ASESORES',
      asesores: precargados.map(a => {
        const numero = a.numeroCorporativo ?? '';
        const link   = a.linkWhatsapp ?? (CELULAR_REGEX.test(numero) ? linkWaMe(numero) : '');
        return {
          agenteID: a.agenteID, nombre: a.nombre, numeroCorporativo: numero, linkWhatsapp: link,
          precargado: a.precargado,
          linkEditadoManual: !!a.linkWhatsapp && a.linkWhatsapp !== linkWaMe(numero),
        };
      }),
    });
  }, [precargados, estado.asesores.length, dispatch]);

  const actualizar = (agenteID: number, cambio: Partial<FilaAsesor>) =>
    dispatch({ tipo: 'SET_ASESORES', asesores: estado.asesores.map(a => a.agenteID === agenteID ? { ...a, ...cambio } : a) });

  const cambiarNumero = (a: FilaAsesor, valor: string) => {
    const numero = valor.replace(/\D/g, '').slice(0, 10);
    const cambio: Partial<FilaAsesor> = { numeroCorporativo: numero };
    // Mientras el link no se haya editado a mano, se propone wa.me/57<numero> (18 caracteres).
    // Solo para móviles: a un fijo (6…) no se le inventa un wa.me.
    if (!a.linkEditadoManual) cambio.linkWhatsapp = MOVIL_REGEX.test(numero) ? linkWaMe(numero) : '';
    actualizar(a.agenteID, cambio);
  };

  const cambiarLink = (a: FilaAsesor, valor: string) =>
    actualizar(a.agenteID, { linkWhatsapp: valor.trim(), linkEditadoManual: valor.trim().length > 0 });

  const restaurarLink = (a: FilaAsesor) =>
    actualizar(a.agenteID, { linkWhatsapp: MOVIL_REGEX.test(a.numeroCorporativo) ? linkWaMe(a.numeroCorporativo) : '', linkEditadoManual: false });

  const eliminar = (agenteID: number) =>
    dispatch({ tipo: 'SET_ASESORES', asesores: estado.asesores.filter(a => a.agenteID !== agenteID) });

  const agregar = () => {
    const ag = activos.find(a => a.agenteID === agregarID);
    if (!ag || estado.asesores.some(a => a.agenteID === ag.agenteID)) return;
    const numero = ag.numeroCorporativo ?? '';
    dispatch({
      tipo: 'SET_ASESORES',
      asesores: [...estado.asesores, {
        agenteID: ag.agenteID, nombre: ag.nombre, numeroCorporativo: numero,
        linkWhatsapp: ag.linkWhatsapp ?? (CELULAR_REGEX.test(numero) ? linkWaMe(numero) : ''),
        precargado: false, linkEditadoManual: !!ag.linkWhatsapp && ag.linkWhatsapp !== linkWaMe(numero),
      }],
    });
    setAgregarID(0);
    setBuscadorKey(k => k + 1);
  };

  /**
   * Fila suelta: un número más que entra al reparto sin estar atado a ningún agente
   * (una línea de campaña, un celular prestado que no está en la nómina). Nace vacía y
   * con nombre editable; no se persiste en Agente porque no hay a quién asociarla.
   */
  const agregarManual = () =>
    dispatch({
      tipo: 'SET_ASESORES',
      asesores: [...estado.asesores, {
        agenteID: siguienteIDManual(estado.asesores),
        nombre: '', numeroCorporativo: '', linkWhatsapp: '',
        precargado: false, linkEditadoManual: false, manual: true,
      }],
    });

  // Continuar = persistir contactos y pedir el catálogo anotado con el link más largo.
  const continuar = useMutation({
    mutationFn: async () => {
      const validos = estado.asesores.filter(asesorValido);
      // Solo se persisten los que tienen agente detrás: una fila suelta no tiene dónde
      // guardarse. Igual entra al reparto, que es lo único que importa para el archivo.
      const persistibles = validos.filter(a => a.agenteID > 0);
      if (persistibles.length > 0)
        await guardarContactosAsesores(persistibles.map(a => ({ agenteID: a.agenteID, numeroCorporativo: a.numeroCorporativo.trim(), linkWhatsapp: a.linkWhatsapp.trim() })));
      return obtenerMensajesDisponibles(
        { archivo: estado.archivo!, liderID: estado.liderID, carterasListaNegra: estado.carterasListaNegra, archivoListaNegra: estado.archivoListaNegra },
        estado.validacion!.huellaArchivo,
        validos.map(a => a.linkWhatsapp.trim()),
        setProgreso,
      );
    },
    onMutate:  () => { setError(null); setProgreso(0); },
    onSuccess: catalogo => { dispatch({ tipo: 'SET_CATALOGO', catalogo }); dispatch({ tipo: 'IR_A', paso: 3 }); },
    onError:   async e => {
      const status = (e as { response?: { status?: number } }).response?.status;
      const msg = await mensajeDeErrorBlob(e, 'No se pudo continuar.');
      if (status === 409) { dispatch({ tipo: 'SET_ARCHIVO', archivo: null }); }
      setError(msg);
    },
  });

  const validos   = estado.asesores.filter(asesorValido);
  const invalidos = estado.asesores.length - validos.length;
  const puede     = validos.length > 0 && invalidos === 0 && !continuar.isPending;
  const candidatos = activos.filter(a => !estado.asesores.some(x => x.agenteID === a.agenteID));

  if (isLoading) return <CargandoSpiral mensaje="Cargando asesores del líder…" />;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 18 }}>
      <div>
        <p style={etiquetaPaso}>Enlaces a distribuir</p>
        <p style={{ margin: '4px 0 0', fontSize: 13, color: 'var(--z-text-muted)' }}>
          Una fila por asesor, o filas sueltas con cualquier número. Escribe el <strong>número corporativo</strong> (móvil 3… o fijo 6…) y el sistema propone el enlace
          <code style={{ margin: '0 4px' }}>wa.me/57…</code>(18 caracteres). Si tienes un enlace más corto (wa.link), pégalo encima:
          cada carácter que ahorres aquí es un carácter más para el mensaje. Todo se guarda por asesor para la próxima vez.
        </p>
      </div>

      {estado.asesores.length === 0 && (
        <Aviso tipo="warning">Este líder no tiene asesores en Cartera_Agente. Agrega al menos uno desde la lista de agentes activos, o una fila suelta con el número que quieras usar.</Aviso>
      )}

      <div style={{ overflowX: 'auto', border: '0.5px solid var(--z-border)', borderRadius: 8 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={TH}>Asesor</th>
              <th style={{ ...TH, width: 170 }}>Número corporativo</th>
              <th style={{ ...TH, width: 300 }}>Link Wpp</th>
              <th style={{ ...TH, width: 60 }}></th>
            </tr>
          </thead>
          <tbody>
            {estado.asesores.map(a => {
              const numOk  = CELULAR_REGEX.test(a.numeroCorporativo);
              const linkOk = a.linkWhatsapp.trim().length > 0;
              return (
                <tr key={a.agenteID}>
                  <td style={TD}>
                    {a.manual ? (
                      <div style={{ display: 'flex', alignItems: 'center', gap: 6 }}>
                        <input value={a.nombre} onChange={e => actualizar(a.agenteID, { nombre: e.target.value })}
                          placeholder="Nombre o referencia (opcional)" style={{ ...inputStyle, minWidth: 170 }} />
                        <span style={{ fontSize: 10, color: C.warning, fontWeight: 600, textTransform: 'uppercase', whiteSpace: 'nowrap' }}>sin asesor</span>
                      </div>
                    ) : (
                      <>
                        {a.nombre}
                        {!a.precargado && <span style={{ marginLeft: 8, fontSize: 10, color: C.sms, fontWeight: 600, textTransform: 'uppercase' }}>prestado</span>}
                      </>
                    )}
                  </td>
                  <td style={TD}>
                    <input value={a.numeroCorporativo} onChange={e => cambiarNumero(a, e.target.value)} placeholder="3001234567" inputMode="numeric"
                      style={{ ...inputStyle, borderColor: a.numeroCorporativo && !numOk ? C.danger : 'var(--z-border)' }} />
                  </td>
                  <td style={TD}>
                    <div style={{ display: 'flex', gap: 6, alignItems: 'center' }}>
                      <input value={a.linkWhatsapp} onChange={e => cambiarLink(a, e.target.value)} placeholder="wa.me/57… o wa.link/…"
                        style={{ ...inputStyle, borderColor: a.numeroCorporativo && !linkOk ? C.danger : 'var(--z-border)' }} />
                      {a.linkEditadoManual && numOk && (
                        <button onClick={() => restaurarLink(a)} title="Volver al wa.me generado"
                          style={{ background: 'none', border: 'none', cursor: 'pointer', color: 'var(--z-text-muted)', fontSize: 12 }}>↺</button>
                      )}
                    </div>
                    <span style={{ fontSize: 10, color: 'var(--z-text-muted)' }}>{a.linkWhatsapp.trim().length} caracteres</span>
                  </td>
                  <td style={{ ...TD, textAlign: 'center' }}>
                    <button onClick={() => eliminar(a.agenteID)} title="Quitar de esta estrategia"
                      style={{ background: 'none', border: 'none', cursor: 'pointer', color: C.danger, fontSize: 16 }}>×</button>
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
        <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>Agregar asesor:</span>
        <BuscadorAsesor key={buscadorKey} candidatos={candidatos} seleccionadoID={agregarID} onSeleccionar={setAgregarID} />
        <BotonAnadir habilitado={agregarID !== 0} onClick={agregar} />
        <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>o</span>
        <button onClick={agregarManual} title="Sumar un número al reparto sin asociarlo a ningún asesor"
          style={{ ...btnSecundario, padding: '9px 16px' }}>
          + Fila sin asesor
        </button>
      </div>

      {invalidos > 0 && (
        <Aviso tipo="warning">{invalidos} fila{invalidos === 1 ? '' : 's'} sin número válido (10 dígitos, empieza en 3 o en 6) o sin link. Complétalas o quítalas.</Aviso>
      )}
      {validos.length > 0 && (
        <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>
          Enlace más largo: <strong>{linkMasLargo(validos)} caracteres</strong> — con ese se calcula si cada mensaje cabe en 160.
        </span>
      )}

      {continuar.isPending && <CargandoSpiral mensaje={`Evaluando mensajes${progreso > 0 && progreso < 100 ? ` — subiendo ${progreso}%` : '…'}`} />}
      {error && <div style={cajaError}>{error}</div>}

      <BarraAcciones>
        <button onClick={() => dispatch({ tipo: 'IR_A', paso: 1 })} style={btnSecundario}>← Atrás</button>
        <button onClick={() => dispatch({ tipo: 'REINICIAR' })} style={btnSecundario}>Cancelar</button>
        <button onClick={() => continuar.mutate()} disabled={!puede} style={btnPrimario(puede)}>Continuar →</button>
      </BarraAcciones>
    </div>
  );
}

// ── Buscador de asesor (combobox con filtro por nombre) ─────────────────────

function normalizarBusqueda(s: string) {
  return s.normalize('NFD').replace(/[\u0300-\u036f]/g, '').toLowerCase().trim();
}

/**
 * Input de texto que filtra la lista de agentes activos por nombre mientras se escribe
 * (sin tildes ni mayúsculas). ZDropdown no busca y con 237 agentes hace falta.
 */
function BuscadorAsesor({ candidatos, seleccionadoID, onSeleccionar }: {
  candidatos: AsesorEstrategia[];
  seleccionadoID: number;
  onSeleccionar: (id: number) => void;
}) {
  const [texto, setTexto]     = useState('');
  const [abierto, setAbierto] = useState(false);
  const [resaltado, setResaltado] = useState(0);
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const cerrar = (e: MouseEvent) => { if (ref.current && !ref.current.contains(e.target as Node)) setAbierto(false); };
    document.addEventListener('mousedown', cerrar);
    return () => document.removeEventListener('mousedown', cerrar);
  }, []);

  const seleccionado = candidatos.find(c => c.agenteID === seleccionadoID) ?? null;
  const q = normalizarBusqueda(texto);
  const coincidencias = (q.length === 0 ? candidatos : candidatos.filter(c => normalizarBusqueda(c.nombre).includes(q))).slice(0, 12);

  const elegir = (a: AsesorEstrategia) => { onSeleccionar(a.agenteID); setTexto(a.nombre); setAbierto(false); };

  const onKeyDown = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (!abierto && (e.key === 'ArrowDown' || e.key === 'Enter')) { setAbierto(true); return; }
    if (e.key === 'ArrowDown') { e.preventDefault(); setResaltado(r => Math.min(r + 1, coincidencias.length - 1)); }
    if (e.key === 'ArrowUp')   { e.preventDefault(); setResaltado(r => Math.max(r - 1, 0)); }
    if (e.key === 'Enter' && coincidencias[resaltado]) { e.preventDefault(); elegir(coincidencias[resaltado]); }
    if (e.key === 'Escape') setAbierto(false);
  };

  return (
    <div ref={ref} style={{ position: 'relative', minWidth: 300 }}>
      <input
        value={texto}
        placeholder="Escribe el nombre del asesor…"
        onChange={e => { setTexto(e.target.value); setAbierto(true); setResaltado(0); if (seleccionado) onSeleccionar(0); }}
        onFocus={() => setAbierto(true)}
        onKeyDown={onKeyDown}
        style={{ ...inputStyle, background: 'var(--z-bg-page)', borderColor: seleccionado ? C.sms : 'var(--z-border)', paddingRight: 28 }}
      />
      <span style={{ position: 'absolute', right: 10, top: '50%', transform: 'translateY(-50%)', fontSize: 11, color: seleccionado ? C.sms : 'var(--z-text-muted)', pointerEvents: 'none' }}>
        {seleccionado ? '✓' : '⌕'}
      </span>
      {abierto && (
        <div style={{
          position: 'absolute', top: 'calc(100% + 4px)', left: 0, right: 0, zIndex: 20, maxHeight: 260, overflowY: 'auto',
          background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 8, boxShadow: '0 6px 20px rgba(0,0,0,0.10)',
        }}>
          {coincidencias.length === 0 ? (
            <div style={{ padding: '10px 12px', fontSize: 12, color: 'var(--z-text-muted)' }}>Sin coincidencias para "{texto}".</div>
          ) : coincidencias.map((a, i) => (
            <div key={a.agenteID}
              onMouseDown={e => { e.preventDefault(); elegir(a); }}
              onMouseEnter={() => setResaltado(i)}
              style={{
                padding: '8px 12px', fontSize: 13, cursor: 'pointer',
                background: i === resaltado ? '#f5f3ff' : 'transparent',
                color: i === resaltado ? C.sms : 'var(--z-text-primary)',
                display: 'flex', justifyContent: 'space-between', gap: 10,
              }}>
              <span>{a.nombre}</span>
              {a.numeroCorporativo && <span style={{ fontSize: 11, color: 'var(--z-text-muted)' }}>{a.numeroCorporativo}</span>}
            </div>
          ))}
          {q.length > 0 && candidatos.filter(c => normalizarBusqueda(c.nombre).includes(q)).length > 12 && (
            <div style={{ padding: '6px 12px', fontSize: 11, color: 'var(--z-text-muted)', borderTop: '0.5px solid var(--z-border)' }}>Sigue escribiendo para acotar…</div>
          )}
        </div>
      )}
    </div>
  );
}

/** Botón "Añadir fila": bloqueado hasta que hay asesor elegido; con hover cuando se puede. */
function BotonAnadir({ habilitado, onClick }: { habilitado: boolean; onClick: () => void }) {
  const [hover, setHover] = useState(false);
  const activo = habilitado && hover;
  return (
    <button
      onClick={onClick}
      disabled={!habilitado}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      title={habilitado ? 'Agregar este asesor a la estrategia' : 'Primero elige un asesor'}
      style={{
        padding: '9px 18px', borderRadius: 6, fontSize: 13, fontWeight: 600,
        border: `1px solid ${habilitado ? C.sms : 'var(--z-border)'}`,
        background: activo ? C.sms : habilitado ? '#f5f3ff' : 'var(--z-bg-page)',
        color: activo ? '#fff' : habilitado ? C.sms : 'var(--z-text-muted)',
        cursor: habilitado ? 'pointer' : 'not-allowed',
        opacity: habilitado ? 1 : 0.6,
        transform: activo ? 'translateY(-1px)' : 'none',
        boxShadow: activo ? '0 4px 12px rgba(124, 58, 237, 0.25)' : 'none',
        transition: 'background 0.15s ease, color 0.15s ease, transform 0.15s ease, box-shadow 0.15s ease',
      }}>
      + Añadir fila
    </button>
  );
}
