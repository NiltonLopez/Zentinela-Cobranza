import { useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { obtenerOpcionesCalificador } from '../../api/calificadorApi';
import { generarPlantilla, validarArchivo, confirmarCargue } from '../../api/asignacionesApi';
import type { PreviewAsignacionDto, EstadoValidacion, FilaValidacionDto } from '../../types/asignaciones';
import { CargandoSpiral } from '../../components/CargandoSpiral';

// ── Helpers de estilo ────────────────────────────────────────────────────────

type Etapa = 'inicial' | 'preview' | 'resultado';

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
      <div style={{
        padding: '12px 16px', borderBottom: '1px solid #fca5a5',
        display: 'flex', alignItems: 'center', gap: '10px',
      }}>
        <span style={{
          display: 'inline-flex', alignItems: 'center', justifyContent: 'center',
          width: 24, height: 24, borderRadius: '50%',
          background: '#dc2626', color: '#fff', fontSize: '13px', fontWeight: 700, flexShrink: 0,
        }}>
          {filas.length}
        </span>
        <p style={{ margin: 0, fontSize: '13px', fontWeight: 600, color: '#b91c1c' }}>
          {filas.length === 1
            ? '1 registro con error — no se cargará'
            : `${filas.length} registros con error — no se cargarán`}
        </p>
      </div>
      <ol style={{
        margin: 0, padding: '12px 16px 12px 36px',
        display: 'flex', flexDirection: 'column', gap: 6,
      }}>
        {lista.map(([categoria, cuenta]) => (
          <li key={categoria} style={{ fontSize: 13, color: '#b91c1c' }}>
            {categoria}
            {cuenta > 1 && (
              <span style={{ color: '#dc2626', fontWeight: 600, marginLeft: 6 }}>
                ×{cuenta}
              </span>
            )}
          </li>
        ))}
      </ol>
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

  // Mutación: validar Excel
  const mutValidar = useMutation({
    mutationFn: async (archivo: File) => {
      const [carteraIDStr, liderIDStr] = seleccion.split('_');
      return validarArchivo(archivo, Number(carteraIDStr), Number(liderIDStr));
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
      const [carteraIDStr, liderIDStr] = seleccion.split('_');
      return confirmarCargue({
        carteraID:       Number(carteraIDStr),
        liderID:         Number(liderIDStr),
        titularIDs:      preview!.titularIDsValidos,
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

          {/* Botón generar plantilla */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 1 — Descarga la plantilla
            </p>
            <button
              onClick={() => generarPlantilla()}
              style={{
                padding: '8px 16px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                background: 'var(--z-bg-page)', color: 'var(--z-text-primary)',
                fontSize: '13px', fontWeight: 600, cursor: 'pointer', width: 'fit-content',
              }}
            >
              Generar Plantilla
            </button>
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              Llena <strong>NumeroDocumento</strong> con la cédula y <strong>Nombre</strong> con el nombre completo. Los titulares que no existan en BD se crearán automáticamente.
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
                    Columnas requeridas: NumeroDocumento, Nombre
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
            background: '#fffbeb', border: '0.5px solid #fcd34d',
            borderRadius: '8px', padding: '12px 16px',
            fontSize: '13px', color: '#92400e',
          }}>
            <strong>Atención:</strong> confirmar reemplazará <em>todas</em> las asignaciones actuales de esta cartera+líder con los {preview.totalValidos} titulares válidos del archivo.
          </div>

          {/* Acciones */}
          <div style={{ display: 'flex', gap: '10px' }}>
            <button
              onClick={() => mutConfirmar.mutate()}
              disabled={preview.titularIDsValidos.length === 0}
              style={{
                padding: '9px 20px', borderRadius: '6px',
                border: 'none', background: preview.titularIDsValidos.length > 0 ? 'var(--z-brand)' : 'var(--z-border)',
                color: preview.titularIDsValidos.length > 0 ? '#fff' : 'var(--z-text-muted)',
                fontSize: '13px', fontWeight: 600,
                cursor: preview.titularIDsValidos.length > 0 ? 'pointer' : 'not-allowed',
              }}
            >
              {(() => { const n = preview.titularIDsValidos.length; return `Reemplazar con ${n} asignacion${n !== 1 ? 'es' : ''}`; })()}
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
