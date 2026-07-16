import { useMemo, useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { obtenerOpcionesCalificador } from '../../api/calificadorApi';
import { generarPlantilla, validarArchivo, confirmarCargue } from '../../api/asignacionesApi';
import { listarRelaciones } from '../../api/gestionCarterasApi';
import type { PreviewAsignacionDto, EstadoValidacion, FilaValidacionDto } from '../../types/asignaciones';
import { CargandoSpiral } from '../../components/CargandoSpiral';

// ── Helpers de estilo ────────────────────────────────────────────────────────

type Etapa = 'inicial' | 'preview' | 'resultado';

// El value del <select> es "carteraID_liderID"; las opciones "Cartera Completo"
// traen liderID = null, que se serializa como el literal "null" (modo completo).
interface SeleccionParseada {
  carteraID:  number;
  liderID:    number | null;
  esCompleto: boolean;
}

function parseSeleccion(seleccion: string): SeleccionParseada | null {
  if (!seleccion) return null;
  const [carteraStr, liderStr] = seleccion.split('_');
  const esCompleto = liderStr === 'null';
  return {
    carteraID:  Number(carteraStr),
    liderID:    esCompleto ? null : Number(liderStr),
    esCompleto,
  };
}

function colorEstado(estado: EstadoValidacion): string {
  if (estado === 'Error')      return '#dc2626';
  if (estado === 'YaAsignado') return '#d97706';
  if (estado === 'Nuevo')      return '#7c3aed';
  return 'var(--z-text-primary)';
}

function etiquetaEstado(estado: EstadoValidacion, razon: string | null): string {
  if (estado === 'Ok')    return 'Listo';
  if (estado === 'Nuevo') return 'Titular nuevo';
  if (estado === 'Error') return razon ?? 'Error de validación';
  return razon ?? estado;
}

// ── Chip de conteo (clickeable opcional) ─────────────────────────────────────

function ChipConteo({
  numero, label, bg, border, colorNum, colorLabel,
  onClick, abierto,
}: {
  numero: number; label: string;
  bg: string; border: string; colorNum: string; colorLabel: string;
  onClick?: () => void;
  abierto?: boolean;
}) {
  const esClickeable = !!onClick;
  return (
    <div
      onClick={onClick}
      role={esClickeable ? 'button' : undefined}
      tabIndex={esClickeable ? 0 : undefined}
      onKeyDown={esClickeable
        ? (e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); onClick!(); } }
        : undefined}
      style={{
        display: 'flex', alignItems: 'center', gap: '8px',
        background: bg, border: `0.5px solid ${border}`,
        borderRadius: '8px', padding: '12px 18px',
        cursor: esClickeable ? 'pointer' : 'default',
        userSelect: 'none',
        boxShadow: esClickeable && abierto ? `inset 0 0 0 1px ${border}` : 'none',
        transition: 'box-shadow 0.15s, transform 0.05s',
      }}
      title={esClickeable ? (abierto ? 'Click para ocultar el detalle' : 'Click para ver el detalle') : undefined}
    >
      <span style={{ fontSize: '22px', fontWeight: 700, color: colorNum }}>{numero}</span>
      <span style={{ fontSize: '13px', color: colorLabel }}>{label}</span>
      {esClickeable && (
        <span style={{
          fontSize: '11px', color: colorLabel, marginLeft: 4,
          transform: abierto ? 'rotate(180deg)' : 'rotate(0deg)',
          transition: 'transform 0.15s',
        }}>
          ▾
        </span>
      )}
    </div>
  );
}

// ── Panel de errores — lista agrupada por tipo de error ────────────────────

function PanelErrores({ filas }: { filas: FilaValidacionDto[] }) {
  if (filas.length === 0) return null;

  const grupos = new Map<string, number>();
  for (const fila of filas) {
    const cat = fila.razon ?? 'Error de validación';
    grupos.set(cat, (grupos.get(cat) ?? 0) + 1);
  }
  const lista = [...grupos.entries()].sort((a, b) => b[1] - a[1]);

  return (
    <div style={{
      background: '#fef2f2', border: '1px solid #fca5a5',
      borderRadius: '8px', overflow: 'hidden',
    }}>
      {/* Banner con total de REGISTROS (no de tipos) — debe ser inequívoco. */}
      <div style={{
        padding: '14px 18px', borderBottom: '1px solid #fca5a5',
        display: 'flex', alignItems: 'baseline', gap: '10px',
      }}>
        <span style={{ fontSize: 24, fontWeight: 700, color: '#dc2626', lineHeight: 1 }}>
          {filas.length.toLocaleString('es-CO')}
        </span>
        <p style={{ margin: 0, fontSize: 14, fontWeight: 600, color: '#b91c1c' }}>
          {filas.length === 1
            ? 'registro con error — no se cargará'
            : 'registros con error — no se cargarán'}
        </p>
      </div>

      {/* Tipos de error y su conteo individual */}
      <div style={{ padding: '12px 18px' }}>
        <p style={{
          margin: '0 0 8px', fontSize: 11, fontWeight: 600, color: '#b91c1c',
          textTransform: 'uppercase', letterSpacing: '0.04em',
        }}>
          Tipos de error encontrados ({lista.length})
        </p>
        <ol style={{
          margin: 0, padding: '0 0 0 20px',
          display: 'flex', flexDirection: 'column', gap: 6,
        }}>
          {lista.map(([categoria, cuenta]) => (
            <li key={categoria} style={{ fontSize: 13, color: '#b91c1c' }}>
              {categoria}
              <span style={{ color: '#7f1d1d', marginLeft: 6 }}>
                ({cuenta.toLocaleString('es-CO')} {cuenta === 1 ? 'fila' : 'filas'})
              </span>
            </li>
          ))}
        </ol>
      </div>
    </div>
  );
}

// ── Panel de apoyo: LiderID válidos de la cartera (modo "Cartera Completo") ───
//
// En modo completo el Excel trae una columna LiderID por fila. Este panel lista
// los líderes con relación ACTIVA en la cartera seleccionada mostrando su ID;
// un clic copia el LiderID al portapapeles para pegarlo en el Excel. Mismo
// patrón que el panel de nombres de "Gestión de Histórico de Virtualidad".
function BotonCopiarLider({ liderID, nombre }: { liderID: number; nombre: string }) {
  const [copiado, setCopiado] = useState(false);
  const copiar = async () => {
    await navigator.clipboard.writeText(String(liderID));
    setCopiado(true);
    setTimeout(() => setCopiado(false), 1200);
  };
  return (
    <button
      onClick={copiar}
      title={`Clic para copiar el LiderID ${liderID}`}
      style={{
        display: 'flex', alignItems: 'center', gap: 8,
        padding: '5px 10px', borderRadius: 6, cursor: 'pointer',
        border: `0.5px solid ${copiado ? '#86efac' : 'var(--z-border)'}`,
        background: copiado ? '#f0fdf4' : 'var(--z-bg-card)',
        color: 'var(--z-text-primary)', fontSize: 12.5,
      }}
    >
      <span style={{
        fontWeight: 700, color: copiado ? '#15803d' : 'var(--z-brand)',
        fontVariantNumeric: 'tabular-nums',
      }}>
        {liderID}
      </span>
      <span style={{ color: 'var(--z-text-muted)' }}>{nombre}</span>
      <span style={{ fontSize: 11, color: copiado ? '#15803d' : 'var(--z-text-muted)' }}>
        {copiado ? '✓ copiado' : '⧉'}
      </span>
    </button>
  );
}

function PanelLideresApoyo({ carteraID }: { carteraID: number }) {
  const { data: relaciones = [], isLoading } = useQuery({
    queryKey: ['relaciones-lider-cartera'],
    queryFn:  listarRelaciones,
    staleTime: 10 * 60 * 1000,
  });

  const lideres = useMemo(
    () => relaciones
      .filter(r => r.carteraID === carteraID && r.activo)
      .map(r => ({ liderID: r.liderID, nombre: r.nombreLider }))
      .sort((a, b) => a.nombre.localeCompare(b.nombre, 'es-CO')),
    [relaciones, carteraID],
  );

  return (
    <div style={{
      background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)', borderRadius: 8,
      padding: 16, display: 'flex', flexDirection: 'column', gap: 10,
    }}>
      <p style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-primary)', textTransform: 'uppercase', letterSpacing: '0.04em', margin: 0 }}>
        LiderID válidos de esta cartera
      </p>
      <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>
        En modo <strong>Completo</strong> cada fila del Excel debe llevar el <strong>LiderID</strong> del líder al que se asigna el titular.
        Haz clic en un líder para copiar su ID y pégalo en la columna <strong>LiderID</strong>. Solo se aceptan líderes activos de esta cartera.
      </p>
      {isLoading ? (
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>Cargando líderes...</p>
      ) : lideres.length === 0 ? (
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>Esta cartera no tiene líderes activos asignados.</p>
      ) : (
        <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
          {lideres.map(l => <BotonCopiarLider key={l.liderID} liderID={l.liderID} nombre={l.nombre} />)}
        </div>
      )}
    </div>
  );
}

// ── Componente principal ─────────────────────────────────────────────────────

export function GestionAsignacionesPage() {
  const inputArchivoRef = useRef<HTMLInputElement>(null);

  const [etapa,         setEtapa]         = useState<Etapa>('inicial');
  const [seleccion,     setSeleccion]     = useState('');        // "carteraID_liderID"
  const [archivoNombre, setArchivoNombre] = useState('');
  const [archivoKB,     setArchivoKB]     = useState(0);
  const [preview,       setPreview]       = useState<PreviewAsignacionDto | null>(null);
  const [mensajeFinal,  setMensajeFinal]  = useState('');
  const [carteraLabel,  setCarteraLabel]  = useState('');
  const [errorMensaje,  setErrorMensaje]  = useState('');

  // Toggles para los detalles asociados a cada chip de conteo
  const [verErrores, setVerErrores] = useState(false);
  const [verValidos, setVerValidos] = useState(false);

  // Dropdown opciones Cartera+Líder (reutiliza calificador)
  const { data: opciones = [], isLoading: cargandoOpciones } = useQuery({
    queryKey: ['asignaciones-opciones'],
    queryFn:  obtenerOpcionesCalificador,
    staleTime: 10 * 60 * 1000,
  });

  const sel        = parseSeleccion(seleccion);
  const esCompleto = sel?.esCompleto ?? false;

  // Mutación: validar Excel
  const mutValidar = useMutation({
    mutationFn: async (archivo: File) => {
      return validarArchivo(archivo, sel!.carteraID, sel!.liderID);
    },
    onSuccess: (data) => {
      setPreview(data);
      // Si hay errores, abrir el panel de errores por defecto para que sean visibles;
      // los válidos quedan colapsados (regla del proyecto: vista previa máx. 10).
      setVerErrores(data.totalErrores > 0);
      setVerValidos(false);
      setEtapa('preview');
    },
    onError: () => setErrorMensaje('No se pudo validar el archivo. Verifica el formato.'),
  });

  // Mutación: confirmar cargue
  const mutConfirmar = useMutation({
    mutationFn: () => {
      return confirmarCargue({
        carteraID:       sel!.carteraID,
        liderID:         sel!.liderID,                     // null → modo completo
        titularIDs:      esCompleto ? [] : preview!.titularIDsValidos,
        asignaciones:    esCompleto ? preview!.asignacionesValidas : [],
        nuevosTitulares: preview!.nuevosTitulares,
      });
    },
    onSuccess: (data) => {
      const opcion = opciones.find(o => `${o.carteraID}_${o.liderID}` === seleccion);
      setCarteraLabel(opcion?.etiqueta ?? '');
      setMensajeFinal(data.mensaje);
      setEtapa('resultado');
    },
    onError: () => setErrorMensaje('Ocurrió un error al confirmar el cargue.'),
  });

  const handleSeleccionarArchivo = (e: React.ChangeEvent<HTMLInputElement>) => {
    const archivo = e.target.files?.[0];
    if (!archivo) return;
    setArchivoNombre(archivo.name);
    setArchivoKB(archivo.size / 1024);
    setErrorMensaje('');
    if (!seleccion) {
      setErrorMensaje('Selecciona primero la cartera y el líder.');
      return;
    }
    mutValidar.mutate(archivo);
  };

  const handleNuevaCarga = () => {
    setEtapa('inicial');
    setPreview(null);
    setArchivoNombre('');
    setArchivoKB(0);
    setMensajeFinal('');
    setCarteraLabel('');
    setErrorMensaje('');
    setVerErrores(false);
    setVerValidos(false);
    if (inputArchivoRef.current) inputArchivoRef.current.value = '';
  };

  const esCargando = mutValidar.isPending || mutConfirmar.isPending;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '900px' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Gestión de Asignaciones
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Cargue masivo de titulares a una cartera mediante plantilla Excel.
          </p>
        </div>
      </div>

      {/* ── Etapa: cargando ── */}
      {esCargando && (
        <CargandoSpiral mensaje={mutValidar.isPending ? 'Validando archivo...' : 'Confirmando cargue...'} />
      )}

      {/* ── Etapa: inicial ── */}
      {!esCargando && etapa === 'inicial' && (
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: '8px', padding: '24px', display: 'flex', flexDirection: 'column', gap: '20px',
        }}>

          {/* Selector Cartera+Líder */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <label style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Cartera + Líder
            </label>
            {cargandoOpciones ? (
              <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>Cargando opciones...</p>
            ) : (
              <select
                value={seleccion}
                onChange={e => setSeleccion(e.target.value)}
                style={{
                  padding: '8px 10px', fontSize: '13px', borderRadius: '6px',
                  border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                  color: 'var(--z-text-primary)', outline: 'none', maxWidth: '400px',
                }}
              >
                <option value="">— Selecciona cartera y líder —</option>
                {opciones.map(o => (
                  <option key={`${o.carteraID}_${o.liderID}`} value={`${o.carteraID}_${o.liderID}`}>
                    {o.etiqueta}
                  </option>
                ))}
              </select>
            )}
          </div>

          {/* Panel de apoyo con LiderID — solo en modo "Cartera Completo" */}
          {esCompleto && sel && <PanelLideresApoyo carteraID={sel.carteraID} />}

          {/* Botón generar plantilla */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 1 — Descarga la plantilla
            </p>
            <button
              onClick={() => generarPlantilla(esCompleto)}
              style={{
                padding: '8px 16px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                background: 'var(--z-bg-page)', color: 'var(--z-text-primary)',
                fontSize: '13px', fontWeight: 600, cursor: 'pointer', width: 'fit-content',
              }}
            >
              {esCompleto ? 'Generar Plantilla (con LiderID)' : 'Generar Plantilla'}
            </button>
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              {esCompleto ? (
                <>Llena <strong>NumeroDocumento</strong>, <strong>Nombre</strong> y <strong>LiderID</strong> (usa el panel de arriba para copiar el ID correcto). Los titulares que no existan en BD se crearán automáticamente.</>
              ) : (
                <>Llena <strong>NumeroDocumento</strong> con la cédula y <strong>Nombre</strong> con el nombre completo. Los titulares que no existan en BD se crearán automáticamente.</>
              )}
            </p>
          </div>

          {/* Área de carga */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 2 — Sube el archivo completado
            </p>
            <div
              onClick={() => seleccion && inputArchivoRef.current?.click()}
              style={{
                border: `1.5px dashed ${archivoNombre ? 'var(--z-brand)' : 'var(--z-border)'}`,
                borderRadius: '8px', padding: '24px', textAlign: 'center',
                cursor: seleccion ? 'pointer' : 'not-allowed',
                background: archivoNombre ? '#eff6ff' : 'transparent',
                opacity: seleccion ? 1 : 0.5,
                transition: 'border-color 0.15s, background 0.15s',
              }}
            >
              <p style={{ fontSize: '13px', color: archivoNombre ? 'var(--z-brand)' : 'var(--z-text-muted)', marginBottom: '4px' }}>
                {archivoNombre || 'Haz clic para seleccionar un archivo .xlsx'}
              </p>
              {archivoNombre
                ? <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>{archivoKB.toFixed(1)} KB</p>
                : <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>
                    Columnas requeridas: NumeroDocumento, Nombre{esCompleto ? ', LiderID' : ''}
                  </p>
              }
            </div>
            <input
              ref={inputArchivoRef}
              type="file"
              accept=".xlsx"
              disabled={!seleccion}
              onChange={handleSeleccionarArchivo}
              style={{ display: 'none' }}
            />
            {archivoNombre && (
              <button
                onClick={() => { setArchivoNombre(''); setArchivoKB(0); if (inputArchivoRef.current) inputArchivoRef.current.value = ''; }}
                style={{ background: 'none', border: 'none', fontSize: '11px', color: 'var(--z-text-muted)', cursor: 'pointer', padding: 0, width: 'fit-content' }}
              >
                × Quitar archivo
              </button>
            )}
            {!seleccion && (
              <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
                Selecciona primero la cartera y el líder para habilitar la carga.
              </p>
            )}
          </div>

          {/* Error */}
          {errorMensaje && (
            <p style={{ fontSize: '13px', color: '#dc2626' }}>{errorMensaje}</p>
          )}
        </div>
      )}

      {/* ── Etapa: preview ── */}
      {!esCargando && etapa === 'preview' && preview && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>

          {/* Conteos — chips verde y rojo son clickeables (toggle del detalle) */}
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <ChipConteo
              numero={preview.totalValidos}
              label="listos para asignar"
              bg="#f0fdf4" border="#86efac" colorNum="#16a34a" colorLabel="#15803d"
              onClick={preview.filas.length > 0 ? () => setVerValidos(v => !v) : undefined}
              abierto={verValidos}
            />
            {preview.totalNuevos > 0 && (
              <ChipConteo
                numero={preview.totalNuevos}
                label="nuevos en base de datos"
                bg="#fefce8" border="#fde68a" colorNum="#b45309" colorLabel="#92400e"
              />
            )}
            {preview.totalErrores > 0 && (
              <ChipConteo
                numero={preview.totalErrores}
                label="errores (no se cargarán)"
                bg="#fef2f2" border="#fca5a5" colorNum="#dc2626" colorLabel="#b91c1c"
                onClick={() => setVerErrores(v => !v)}
                abierto={verErrores}
              />
            )}
          </div>

          {/* Panel de errores — TODOS los errores con su razón (click en el chip rojo) */}
          {verErrores && <PanelErrores filas={preview.filasConError} />}

          {/* Tabla de válidos — primeros 10 (click en el chip verde) */}
          {verValidos && preview.filas.length > 0 && (
            <div style={{
              background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
              borderRadius: '8px', overflow: 'hidden',
            }}>
              <p style={{
                padding: '12px 16px', fontSize: '12px', color: 'var(--z-text-muted)',
                borderBottom: '0.5px solid var(--z-border)', margin: 0,
              }}>
                Vista previa — primeros {preview.filas.length} registros sin error
              </p>
              <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                <thead>
                  <tr style={{ background: 'var(--z-bg-page)' }}>
                    {['#', 'NumeroDocumento', 'Nombre', 'Estado'].map((col, i) => (
                      <th key={i} style={{
                        padding: '9px 14px', textAlign: 'left',
                        fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)',
                        borderBottom: '0.5px solid var(--z-border)',
                        letterSpacing: '0.04em', textTransform: 'uppercase',
                      }}>
                        {col}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {preview.filas.map((fila, idx) => (
                    <tr key={idx} style={{ borderBottom: '0.5px solid var(--z-border)' }}>
                      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                        {idx + 1}
                      </td>
                      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-primary)', fontWeight: 500 }}>
                        {fila.numeroDocumento}
                      </td>
                      <td style={{ padding: '10px 14px', fontSize: '13px', color: 'var(--z-text-muted)' }}>
                        {fila.nombre ?? '—'}
                      </td>
                      <td style={{ padding: '10px 14px', fontSize: '13px', color: colorEstado(fila.estado) }}>
                        {etiquetaEstado(fila.estado, fila.razon)}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}

          {/* Aviso de reemplazo */}
          <div style={{
            background: esCompleto ? '#fef2f2' : '#fffbeb',
            border: `0.5px solid ${esCompleto ? '#fca5a5' : '#fcd34d'}`,
            borderRadius: '8px', padding: '12px 16px',
            fontSize: '13px', color: esCompleto ? '#b91c1c' : '#92400e',
          }}>
            {esCompleto ? (
              <><strong>Atención:</strong> este es un cargue de <em>cartera completa</em>. Confirmar borrará <em>todas</em> las asignaciones de la cartera para <em>todos</em> los líderes y las reemplazará con las {preview.totalValidos} asignaciones válidas del archivo. Un líder que no aparezca en el archivo quedará sin titulares en esta cartera.</>
            ) : (
              <><strong>Atención:</strong> confirmar reemplazará <em>todas</em> las asignaciones actuales de esta cartera+líder con los {preview.totalValidos} titulares válidos del archivo.</>
            )}
          </div>

          {/* Acciones */}
          <div style={{ display: 'flex', gap: '10px' }}>
            <button
              onClick={() => mutConfirmar.mutate()}
              disabled={preview.totalValidos === 0}
              style={{
                padding: '9px 20px', borderRadius: '6px',
                border: 'none', background: preview.totalValidos > 0 ? 'var(--z-brand)' : 'var(--z-border)',
                color: preview.totalValidos > 0 ? '#fff' : 'var(--z-text-muted)',
                fontSize: '13px', fontWeight: 600,
                cursor: preview.totalValidos > 0 ? 'pointer' : 'not-allowed',
              }}
            >
              {(() => { const n = preview.totalValidos; return `Reemplazar con ${n} asignacion${n !== 1 ? 'es' : ''}`; })()}
            </button>
            <button
              onClick={handleNuevaCarga}
              style={{
                padding: '9px 16px', borderRadius: '6px',
                border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                color: 'var(--z-text-muted)', fontSize: '13px', cursor: 'pointer',
              }}
            >
              Cancelar
            </button>
          </div>

          {errorMensaje && (
            <p style={{ fontSize: '13px', color: '#dc2626' }}>{errorMensaje}</p>
          )}
        </div>
      )}

      {/* ── Etapa: resultado ── */}
      {!esCargando && etapa === 'resultado' && (
        <div style={{
          background: '#f0fdf4', border: '0.5px solid #86efac',
          borderRadius: '8px', padding: '24px',
          display: 'flex', flexDirection: 'column', gap: '16px',
        }}>
          <p style={{ fontSize: '15px', fontWeight: 600, color: '#15803d' }}>
            Cargue completado
          </p>
          {carteraLabel && (
            <p style={{ fontSize: '12px', color: '#15803d', fontWeight: 600,
              background: '#dcfce7', borderRadius: '6px', padding: '6px 10px',
              width: 'fit-content' }}>
              {carteraLabel}
            </p>
          )}
          <p style={{ fontSize: '13px', color: '#166534' }}>{mensajeFinal}</p>
          <button
            onClick={handleNuevaCarga}
            style={{
              padding: '8px 16px', borderRadius: '6px',
              border: '0.5px solid #86efac', background: '#fff',
              color: '#15803d', fontSize: '13px', fontWeight: 600,
              cursor: 'pointer', width: 'fit-content',
            }}
          >
            Nueva carga
          </button>
        </div>
      )}

    </div>
  );
}
