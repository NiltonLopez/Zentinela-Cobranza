import { useRef, useState } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import { generarPlantillaReporte, validarArchivoReporte, confirmarCargueReporte, obtenerUltimaActualizacion } from '../../api/reportesApi';
import { obtenerCarteras } from '../../api/carterasApi';
import { LeyendaUltimaActualizacion } from '../../components/LeyendaUltimaActualizacion';
import type { FilaValidacionReporteDto, EstadoFilaReporte, PreviewReporteDto } from '../../types/reportes';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ZDropdown } from '../../components/ZDropdown';

// ── Helpers ───────────────────────────────────────────────────────────────────

type Etapa = 'inicial' | 'preview' | 'resultado';

// ── Badge de estado ───────────────────────────────────────────────────────────

function BadgeEstado({ estado }: { estado: EstadoFilaReporte }) {
  if (estado === 'Ok') {
    return (
      <span style={{
        display: 'inline-block', padding: '2px 8px', borderRadius: 99,
        fontSize: 11, fontWeight: 600,
        background: '#f0fdf4', color: '#16a34a', border: '1px solid #86efac',
      }}>
        Listo
      </span>
    );
  }
  if (estado === 'NuevoTitular') {
    return (
      <span style={{
        display: 'inline-block', padding: '2px 8px', borderRadius: 99,
        fontSize: 11, fontWeight: 600,
        background: '#eff6ff', color: '#1d4ed8', border: '1px solid #93c5fd',
      }}>
        Titular nuevo
      </span>
    );
  }
  return (
    <span style={{
      display: 'inline-block', padding: '2px 8px', borderRadius: 99,
      fontSize: 11, fontWeight: 600,
      background: '#fef2f2', color: '#dc2626', border: '1px solid #fca5a5',
    }}>
      Error
    </span>
  );
}

// ── Campos opcionales a mostrar en el preview ────────────────────────────────

const CAMPOS_OPCIONALES: { key: string; label: string }[] = [
  { key: 'nombre',            label: 'Nombre'      },
  { key: 'ultimoPerfil',      label: 'Perfil'      },
  { key: 'mejorPerfil',       label: 'Mejor perf.' },

  { key: 'saldoTotal',        label: 'Saldo'       },
  { key: 'numeroMarcado',     label: 'Celular'     },
  { key: 'rotacion',          label: 'Rotación'    },
  { key: 'fechaUltimaGestion', label: 'F. gestión' },
  { key: 'fechaPromesa',      label: 'F. promesa'  },
  { key: 'valorPromesa',      label: 'V. promesa'  },
  { key: 'motivoNoPago',      label: 'Motivo'      },
  { key: 'fechaActualizacion', label: 'F. actualiz.' },
];

// ── Tabla de validación (válidos + nuevos) ────────────────────────────────────

function TablaPreview({ filas, columnasEncontradas }: {
  filas: FilaValidacionReporteDto[];
  columnasEncontradas: string[];
}) {
  if (filas.length === 0) return null;

  const encontradas = new Set(columnasEncontradas);

  const TH: React.CSSProperties = {
    padding: '8px 10px', textAlign: 'center',
    fontSize: '11px', fontWeight: 600,
    borderBottom: '0.5px solid var(--z-border)',
    letterSpacing: '0.04em', textTransform: 'uppercase', whiteSpace: 'nowrap',
  };
  const TD: React.CSSProperties = {
    padding: '8px 10px', fontSize: '13px',
    borderBottom: '0.5px solid var(--z-border)',
    textAlign: 'center', whiteSpace: 'nowrap',
  };

  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
      borderRadius: '8px', overflow: 'hidden',
    }}>
      <p style={{
        padding: '10px 16px', fontSize: '12px', color: 'var(--z-text-muted)',
        borderBottom: '0.5px solid var(--z-border)', margin: 0,
      }}>
        Muestra — primeros {filas.length} registros válidos
      </p>
      <div style={{ overflowX: 'auto' }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr style={{ background: 'var(--z-bg-page)' }}>
              <th style={{ ...TH, textAlign: 'left', paddingLeft: 14, color: 'var(--z-text-muted)' }}>#</th>
              <th style={{ ...TH, textAlign: 'left', color: 'var(--z-text-muted)' }}>TitularID</th>
              <th style={{ ...TH, textAlign: 'left', color: 'var(--z-text-muted)' }}>Asesor</th>
              <th style={{ ...TH, textAlign: 'left', color: 'var(--z-text-muted)' }}>Estado</th>
              {CAMPOS_OPCIONALES.map(c => {
                const ok = encontradas.has(c.key);
                return (
                  <th key={c.key} style={{ ...TH, color: ok ? 'var(--z-text-primary)' : 'var(--z-text-muted)' }}>
                    {c.label}&nbsp;
                    <span style={{ fontSize: 11, color: ok ? '#16a34a' : '#dc2626' }}>
                      {ok ? '✓' : '✗'}
                    </span>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody>
            {filas.map((fila, idx) => {
              const mapeados = new Set(fila.camposMapeados);
              return (
                <tr key={idx} style={{ borderBottom: '0.5px solid var(--z-border)' }}>
                  <td style={{ ...TD, textAlign: 'left', paddingLeft: 14, color: 'var(--z-text-muted)' }}>
                    {idx + 1}
                  </td>
                  <td style={{ ...TD, textAlign: 'left', fontWeight: 500, color: 'var(--z-text-primary)' }}>
                    {fila.titularID}
                  </td>
                  <td style={{ ...TD, textAlign: 'left', color: 'var(--z-text-muted)' }}>
                    {fila.usuarioControl || '—'}
                  </td>
                  <td style={{ ...TD, textAlign: 'left' }}>
                    <BadgeEstado estado={fila.estado} />
                  </td>
                  {CAMPOS_OPCIONALES.map(c => (
                    <td key={c.key} style={TD}>
                      {!encontradas.has(c.key)
                        ? <span style={{ color: 'var(--z-border)', fontSize: 12 }}>—</span>
                        : mapeados.has(c.key)
                          ? <span style={{ color: '#16a34a', fontWeight: 700, fontSize: 13 }}>✓</span>
                          : <span style={{ color: 'var(--z-text-muted)', fontSize: 12 }}>—</span>
                      }
                    </td>
                  ))}
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}

// ── Panel de errores ──────────────────────────────────────────────────────────

/**
 * Etiqueta canónica por estado para agrupar errores en el panel.
 * No usa la `razon` directamente porque puede contener el UsuarioControl variable.
 */
function categoriaEstadoReporte(estado: EstadoFilaReporte): string {
  if (estado === 'SinNombreParaCrear')  return 'TitularID no existe y no se proporcionó Nombre para crearlo';
  if (estado === 'AgenteNoEncontrado')  return 'Usuario Control no encontrado en el sistema';
  return 'Error de validación';
}

function PanelErrores({ filas }: { filas: FilaValidacionReporteDto[] }) {
  if (filas.length === 0) return null;

  const grupos = new Map<string, number>();
  for (const fila of filas) {
    const cat = categoriaEstadoReporte(fila.estado);
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

// ── Chips de conteo ───────────────────────────────────────────────────────────

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

// ── Componente principal ──────────────────────────────────────────────────────

export function CargueReportesPage() {
  const inputArchivoRef = useRef<HTMLInputElement>(null);

  const [etapa,              setEtapa]              = useState<Etapa>('inicial');
  const [generandoPlantilla, setGenerandoPlantilla] = useState(false);
  const [carteraID,          setCarteraID]          = useState<number>(0);
  const [archivoNombre,      setArchivoNombre]      = useState('');
  const [archivoKB,          setArchivoKB]          = useState(0);
  const [preview,            setPreview]            = useState<PreviewReporteDto | null>(null);
  const [resultado,          setResultado]          = useState<import('../../types/reportes').ResultadoReporteDto | null>(null);
  const [errorMensaje,       setErrorMensaje]       = useState('');

  // Toggles para los detalles asociados a cada chip de conteo
  const [verErrores, setVerErrores] = useState(false);
  const [verValidos, setVerValidos] = useState(false);

  const { data: carteras = [] } = useQuery({
    queryKey: ['carteras'],
    queryFn:  obtenerCarteras,
  });

  const mutValidar = useMutation({
    mutationFn: ({ archivo, cid }: { archivo: File; cid: number }) =>
      validarArchivoReporte(archivo, cid),
    onSuccess: (data) => {
      setPreview(data);
      // Si hay errores, abrir el panel de errores por defecto para que sean visibles;
      // los válidos quedan colapsados (memory: vista previa máx. 10).
      setVerErrores(data.totalInvalidos > 0);
      setVerValidos(false);
      setEtapa('preview');
      setErrorMensaje('');
    },
    onError: () => setErrorMensaje('No se pudo validar el archivo. Verifica que el formato sea correcto.'),
  });

  const mutConfirmar = useMutation({
    mutationFn: () => confirmarCargueReporte({ filasValidas: preview!.filasValidasRaw }),
    onSuccess: (data) => {
      setResultado(data);
      setEtapa('resultado');
    },
    onError: () => setErrorMensaje('Ocurrió un error al confirmar el cargue.'),
  });

  const handleSeleccionarArchivo = (e: React.ChangeEvent<HTMLInputElement>) => {
    const archivo = e.target.files?.[0];
    if (!archivo) return;
    if (carteraID === 0) {
      setErrorMensaje('Selecciona una cartera antes de subir el archivo.');
      return;
    }
    setArchivoNombre(archivo.name);
    setArchivoKB(archivo.size / 1024);
    setErrorMensaje('');
    mutValidar.mutate({ archivo, cid: carteraID });
  };

  const handleNuevaCarga = () => {
    setEtapa('inicial');
    setPreview(null);
    setArchivoNombre('');
    setArchivoKB(0);
    setResultado(null);
    setErrorMensaje('');
    setVerErrores(false);
    setVerValidos(false);
    if (inputArchivoRef.current) inputArchivoRef.current.value = '';
  };

  const esCargando = mutValidar.isPending || mutConfirmar.isPending;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '960px' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Cargue de Reportes
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Actualiza <strong>UltimoEstadoTitular</strong> (UPSERT) y enriquece <strong>HistoricoEstadoTitular</strong> (append) desde archivo CSV o XLSX.
          </p>
        </div>
      </div>

      {/* Cargando */}
      {esCargando && (
        <CargandoSpiral mensaje={mutValidar.isPending ? 'Validando archivo...' : 'Procesando cargue...'} />
      )}

      {/* ── Etapa: inicial ── */}
      {!esCargando && etapa === 'inicial' && (
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: '8px', padding: '24px', display: 'flex', flexDirection: 'column', gap: '20px',
        }}>

          {/* Paso 1 — Cartera */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 1 — Selecciona la cartera
            </p>
            <div style={{ display: 'flex', alignItems: 'center', gap: '10px', flexWrap: 'wrap' }}>
              <ZDropdown
                options={[{ value: 0, label: '— Seleccionar cartera —' }, ...carteras.map(c => ({ value: c.id, label: c.nombreCartera }))]}
                value={carteraID}
                onChange={setCarteraID}
                background="var(--z-bg-page)"
                style={{ width: '280px' }}
              />
              <LeyendaUltimaActualizacion
                carteraID={carteraID}
                queryKey="reporte-ultima-actualizacion"
                queryFn={obtenerUltimaActualizacion}
              />
            </div>
          </div>

          {/* Paso 2 — Plantilla */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 2 — Descarga la plantilla
            </p>
            <button
              onClick={async () => {
                setGenerandoPlantilla(true);
                await generarPlantillaReporte();
                setGenerandoPlantilla(false);
              }}
              disabled={generandoPlantilla}
              style={{
                padding: '8px 16px', borderRadius: '6px', border: '0.5px solid var(--z-border)',
                background: generandoPlantilla ? 'var(--z-border)' : 'var(--z-bg-page)',
                color: generandoPlantilla ? 'var(--z-text-muted)' : 'var(--z-text-primary)',
                fontSize: '13px', fontWeight: 600, width: 'fit-content',
                cursor: generandoPlantilla ? 'not-allowed' : 'pointer',
              }}
            >
              {generandoPlantilla ? 'Generando...' : 'Generar Plantilla'}
            </button>
            <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
              Campos obligatorios: <strong>TitularID</strong>, <strong>UsuarioControl</strong>.
              Si el TitularID es nuevo, agrega <strong>Nombre</strong> para crearlo.
              La cartera se asigna automáticamente desde el selector de arriba.
            </p>
          </div>

          {/* Paso 3 — Subir archivo */}
          <div style={{ display: 'flex', flexDirection: 'column', gap: '6px' }}>
            <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Paso 3 — Sube el archivo completado
            </p>
            <div
              onClick={() => carteraID !== 0 && inputArchivoRef.current?.click()}
              style={{
                border: `1.5px dashed ${archivoNombre ? 'var(--z-brand)' : 'var(--z-border)'}`,
                borderRadius: '8px', padding: '24px', textAlign: 'center',
                cursor: carteraID === 0 ? 'not-allowed' : 'pointer',
                background: archivoNombre ? '#eff6ff' : 'transparent',
                opacity: carteraID === 0 ? 0.5 : 1,
                transition: 'border-color 0.15s, background 0.15s',
              }}
            >
              <p style={{ fontSize: '13px', color: archivoNombre ? 'var(--z-brand)' : 'var(--z-text-muted)', marginBottom: '4px' }}>
                {archivoNombre || 'Haz clic para seleccionar un archivo .xlsx'}
              </p>
              {archivoNombre
                ? <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>{archivoKB.toFixed(1)} KB</p>
                : <p style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>
                    Campos obligatorios: TitularID (o Cédula) y AgenteID (o UsuarioControl)
                  </p>
              }
            </div>
            <input
              ref={inputArchivoRef}
              type="file"
              accept=".xlsx"
              disabled={carteraID === 0}
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
            {carteraID === 0 && (
              <p style={{ fontSize: '12px', color: 'var(--z-text-muted)' }}>
                Selecciona primero la cartera para habilitar la carga.
              </p>
            )}
          </div>

          {errorMensaje && (
            <div style={{
              background: '#fef2f2', border: '0.5px solid #fca5a5',
              borderRadius: '6px', padding: '10px 14px',
              fontSize: '13px', color: '#dc2626',
            }}>
              {errorMensaje}
            </div>
          )}
        </div>
      )}

      {/* ── Etapa: preview ── */}
      {!esCargando && etapa === 'preview' && preview && (
        <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>

          {/* Chips de conteo (verde y rojo son clickeables — toggle del detalle) */}
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <ChipConteo
              numero={preview.totalUnicos}
              label="registros únicos válidos"
              bg="#f0fdf4" border="#86efac" colorNum="#16a34a" colorLabel="#15803d"
              onClick={preview.filas.length > 0 ? () => setVerValidos(v => !v) : undefined}
              abierto={verValidos}
            />
            {preview.totalDuplicados > 0 && (
              <ChipConteo
                numero={preview.totalDuplicados}
                label="duplicados — se quedará el más reciente"
                bg="#fefce8" border="#fde68a" colorNum="#b45309" colorLabel="#92400e"
              />
            )}
            {preview.totalNuevos > 0 && (
              <ChipConteo
                numero={preview.totalNuevos}
                label="titulares nuevos"
                bg="#eff6ff" border="#93c5fd" colorNum="#1d4ed8" colorLabel="#1e40af"
              />
            )}
            {preview.totalInvalidos > 0 && (
              <ChipConteo
                numero={preview.totalInvalidos}
                label="con error — no se cargarán"
                bg="#fef2f2" border="#fca5a5" colorNum="#dc2626" colorLabel="#b91c1c"
                onClick={() => setVerErrores(v => !v)}
                abierto={verErrores}
              />
            )}
          </div>

          {/* Panel de errores — TODOS los errores (click en el chip rojo) */}
          {verErrores && <PanelErrores filas={preview.filasConError} />}

          {/* Tabla de válidos — primeros 10 (click en el chip verde) */}
          {verValidos && preview.filas.length > 0 && (
            <TablaPreview filas={preview.filas} columnasEncontradas={preview.columnasEncontradas} />
          )}

          {/* Aviso de comportamiento */}
          <div style={{
            background: '#eff6ff', border: '0.5px solid #93c5fd',
            borderRadius: '8px', padding: '12px 16px',
            fontSize: '13px', color: '#1e40af',
          }}>
            <strong>UltimoEstadoTitular:</strong> los {preview.totalValidos} registros válidos se actualizarán
            por UPSERT (si existen, se modifican; si no, se crean).
            {preview.totalNuevos > 0 && (
              <> <strong>{preview.totalNuevos}</strong> titular(es) nuevo(s) se crearán en la tabla <strong>Titular</strong>.</>
            )}
            <br />
            <strong>HistoricoEstadoTitular:</strong> se agregarán solo los registros cuya combinación
            TitularID+CarteraID+AgenteID no exista aún.
          </div>

          {/* Acciones */}
          <div style={{ display: 'flex', gap: '10px', alignItems: 'center' }}>
            <button
              onClick={() => mutConfirmar.mutate()}
              disabled={preview.totalUnicos === 0}
              style={{
                padding: '9px 20px', borderRadius: '6px', border: 'none',
                background: preview.totalUnicos > 0 ? 'var(--z-brand)' : 'var(--z-border)',
                color: preview.totalUnicos > 0 ? '#fff' : 'var(--z-text-muted)',
                fontSize: '13px', fontWeight: 600,
                cursor: preview.totalUnicos > 0 ? 'pointer' : 'not-allowed',
              }}
            >
              Confirmar {preview.totalUnicos} registro{preview.totalUnicos !== 1 ? 's' : ''} único{preview.totalUnicos !== 1 ? 's' : ''}
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
            {preview.totalInvalidos > 0 && preview.totalUnicos === 0 && (
              <p style={{ fontSize: '13px', color: '#dc2626', margin: 0 }}>
                No hay registros válidos. Corrige los errores y vuelve a subir el archivo.
              </p>
            )}
          </div>

          {errorMensaje && (
            <div style={{
              background: '#fef2f2', border: '0.5px solid #fca5a5',
              borderRadius: '6px', padding: '10px 14px',
              fontSize: '13px', color: '#dc2626',
            }}>
              {errorMensaje}
            </div>
          )}
        </div>
      )}

      {/* ── Etapa: resultado ── */}
      {!esCargando && etapa === 'resultado' && resultado && (
        <div style={{
          background: '#f0fdf4', border: '0.5px solid #86efac',
          borderRadius: '8px', padding: '24px',
          display: 'flex', flexDirection: 'column', gap: '16px',
        }}>
          <p style={{ fontSize: '15px', fontWeight: 600, color: '#15803d', margin: 0 }}>
            Cargue completado
          </p>

          {/* Métricas del resultado */}
          <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
            <div style={{
              background: '#dcfce7', border: '0.5px solid #86efac',
              borderRadius: '8px', padding: '12px 18px',
              display: 'flex', alignItems: 'center', gap: '8px',
            }}>
              <span style={{ fontSize: '22px', fontWeight: 700, color: '#15803d' }}>
                {resultado.procesados.toLocaleString('es-CO')}
              </span>
              <span style={{ fontSize: '13px', color: '#166534' }}>registros únicos procesados</span>
            </div>

            {resultado.duplicados > 0 && (
              <div style={{
                background: '#fefce8', border: '0.5px solid #fde68a',
                borderRadius: '8px', padding: '12px 18px',
                display: 'flex', alignItems: 'center', gap: '8px',
              }}>
                <span style={{ fontSize: '22px', fontWeight: 700, color: '#b45309' }}>
                  {resultado.duplicados.toLocaleString('es-CO')}
                </span>
                <span style={{ fontSize: '13px', color: '#92400e' }}>duplicados eliminados</span>
              </div>
            )}

            {resultado.titularesNuevos > 0 && (
              <div style={{
                background: '#eff6ff', border: '0.5px solid #93c5fd',
                borderRadius: '8px', padding: '12px 18px',
                display: 'flex', alignItems: 'center', gap: '8px',
              }}>
                <span style={{ fontSize: '22px', fontWeight: 700, color: '#1d4ed8' }}>
                  {resultado.titularesNuevos.toLocaleString('es-CO')}
                </span>
                <span style={{ fontSize: '13px', color: '#1e40af' }}>titulares nuevos creados</span>
              </div>
            )}
          </div>

          <p style={{ fontSize: '13px', color: '#166534', margin: 0 }}>{resultado.mensaje}</p>

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
