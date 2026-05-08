import { useState, useRef } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import {
  obtenerColumnas,
  obtenerPreview,
  exportarExcel,
  exportarCsv,
  obtenerOpcionesCreador,
} from '../../api/creadorApi';
import type { FiltroBaseMadreRequest, ColumnaBaseMadre } from '../../types/creador';

interface SeleccionCartera {
  carteraID: number;
  liderID:   number | null;
}

const claveSeleccion = (carteraID: number, liderID: number | null) =>
  `${carteraID}|${liderID ?? ''}`;

// ── Tipificaciones conocidas para dropdowns ───────────────────────────────────
const TIPIFICACIONES = [
  'Acuerdo de pago abono', 'Acuerdo de pago total', 'Incumplido', 'No hubo acuerdo',
  'Paz y salvo', 'PQR', 'Renuente', 'Realizo abono', 'Recordar',
  'Fraude o suplantacion', 'Potencial acuerdo', 'No gestionar', 'Consignación por aplicar',
  'Mensaje con tercero', 'Mensaje en buzón', 'Fallecido', 'No contacto',
  'Mensaje contestadora/buzon', 'Numero equivocado', 'Ilocalizado',
  'Datos de localización', 'Sin gestion', 'Volver a llamar',
];

const FUENTES_EMAIL = [
  { key: 'Cajacopi',      label: 'Cajacopi'       },
  { key: 'Camara',        label: 'Cámara'         },
  { key: 'SaludTotal',    label: 'Salud Total'    },
  { key: 'Positiva',      label: 'Positiva'       },
  { key: 'Tigo',          label: 'Tigo'           },
  { key: 'Claro',         label: 'Claro'          },
  { key: 'MutualSer',     label: 'MutualSer'      },
];

// MutualSer expone 2 celulares como columnas independientes — entran como
// dos chips separados (excepción consciente a "1 fuente = 1 entrada").
const FUENTES_CELULAR = [
  { key: 'Cajacopi',      label: 'Cajacopi'       },
  { key: 'Camara',        label: 'Cámara'         },
  { key: 'SaludTotal',    label: 'Salud Total'    },
  { key: 'Positiva',      label: 'Positiva'       },
  { key: 'Tigo',          label: 'Tigo'           },
  { key: 'SOS',           label: 'SOS'            },
  { key: 'Claro',         label: 'Claro'          },
  { key: 'MutualSer',     label: 'MutualSer (1)'  },
  { key: 'MutualSer2',    label: 'MutualSer (2)'  },
];

const inputStyle: React.CSSProperties = {
  width: '100%', padding: '7px 10px', fontSize: '13px',
  border: '1px solid var(--z-border)', borderRadius: '6px',
  background: 'var(--z-bg-card)', color: 'var(--z-text-primary)', outline: 'none',
};

const labelStyle: React.CSSProperties = {
  display: 'block', fontSize: '11px', fontWeight: 600,
  color: 'var(--z-text-muted)', textTransform: 'uppercase',
  letterSpacing: '0.05em', marginBottom: '5px',
};

// ── Página ────────────────────────────────────────────────────────────────────
export function CreadorZentinelaPage() {
  const [seleccion,        setSeleccion]        = useState<SeleccionCartera | null>(null);
  const [ultimoPerfil,     setUltimoPerfil]     = useState('');
  const [mejorPerfil,      setMejorPerfil]      = useState('');
  const [promesasVigentes, setPromesasVigentes] = useState<'all' | 'vigentes' | 'sin'>('all');
  const [rotacionMaxima,   setRotacionMaxima]   = useState('');
  const [saldoMinimo,      setSaldoMinimo]      = useState('');
  const [tipoAfiliado,     setTipoAfiliado]     = useState('');
  const [estadoFosyga,     setEstadoFosyga]     = useState('');
  const [fuentesCelular,      setFuentesCelular]      = useState<string[]>([]);
  const [coincidenciasCelular, setCoincidenciasCelular] = useState<string[]>([]);
  const [fuentesEmail,         setFuentesEmail]         = useState<string[]>([]);
  const [coincidenciasEmail,   setCoincidenciasEmail]   = useState<string[]>([]);
  const [filtrosAbiertos,  setFiltrosAbiertos]  = useState(false);
  const [columnasSeleccionadas, setColumnasSeleccionadas] = useState<string[]>([]);
  // Columnas siempre presentes en el Excel — no se pueden desmarcar.
  const COLUMNAS_FIJAS = ['NumeroDocumento', 'Nombre'];
  const [filtroActivo,     setFiltroActivo]     = useState<FiltroBaseMadreRequest | null>(null);

  const anchorRef = useRef<HTMLAnchorElement>(null);

  // ── Queries ────────────────────────────────────────────────────────────────
  const { data: opciones = [] } = useQuery({
    queryKey: ['creador-opciones'],
    queryFn: obtenerOpcionesCreador,
  });

  const { data: columnas = [] } = useQuery({
    queryKey: ['creador-columnas'],
    queryFn: obtenerColumnas,
    staleTime: Infinity,
  });

  const { data: preview, isFetching: cargandoPreview } = useQuery({
    queryKey: ['creador-preview', filtroActivo],
    queryFn: () => obtenerPreview(filtroActivo!),
    enabled: filtroActivo !== null,
  });

  // ── Mutaciones descarga ────────────────────────────────────────────────────
  // Helper compartido: dispara la descarga del blob con el nombre de archivo
  // construido a partir de la cartera seleccionada y la extensión correcta.
  const dispararDescarga = (blob: Blob, extension: 'xlsx' | 'csv') => {
    const url = URL.createObjectURL(blob);
    const etiqueta = seleccion
      ? opciones.find(o => o.carteraID === seleccion.carteraID && o.liderID === seleccion.liderID)?.etiqueta
      : undefined;
    const carteraNombre = etiqueta ?? 'Base';
    const fecha         = new Date().toISOString().slice(0, 10).replace(/-/g, '');
    if (anchorRef.current) {
      anchorRef.current.href     = url;
      anchorRef.current.download = `BaseMadre_${carteraNombre}_${fecha}.${extension}`;
      anchorRef.current.click();
    }
    setTimeout(() => URL.revokeObjectURL(url), 5000);
  };

  const exportMutation = useMutation({
    mutationFn: (filtro: FiltroBaseMadreRequest) => exportarExcel(filtro),
    onSuccess: (blob) => dispararDescarga(blob, 'xlsx'),
  });

  const exportCsvMutation = useMutation({
    mutationFn: (filtro: FiltroBaseMadreRequest) => exportarCsv(filtro),
    onSuccess: (blob) => dispararDescarga(blob, 'csv'),
  });

  // ── Handlers ───────────────────────────────────────────────────────────────
  const construirFiltro = (): FiltroBaseMadreRequest => ({
    carteraID:          seleccion!.carteraID,
    liderID:            seleccion!.liderID,
    ultimoPerfil:       ultimoPerfil || undefined,
    mejorPerfil:        mejorPerfil  || undefined,
    promesasVigentes:   promesasVigentes === 'vigentes' ? true : promesasVigentes === 'sin' ? false : undefined,
    rotacionMaxima:     rotacionMaxima ? Number(rotacionMaxima) : undefined,
    saldoMinimo:        saldoMinimo  ? Number(saldoMinimo) : undefined,
    tipoAfiliado:       tipoAfiliado || undefined,
    estadoFosyga:       estadoFosyga || undefined,
    fuentesCelular,
    coincidenciasCelular,
    fuentesEmail,
    coincidenciasEmail,
    columnasSeleccionadas,
  });

  const toggleFuente = (fuente: string) =>
    setFuentesCelular(prev =>
      prev.includes(fuente) ? prev.filter(f => f !== fuente) : [...prev, fuente],
    );

  const toggleCoincidencia = (fuente: string) =>
    setCoincidenciasCelular(prev =>
      prev.includes(fuente) ? prev.filter(f => f !== fuente) : [...prev, fuente],
    );

  const toggleFuenteEmail = (fuente: string) =>
    setFuentesEmail(prev =>
      prev.includes(fuente) ? prev.filter(f => f !== fuente) : [...prev, fuente],
    );

  const toggleCoincidenciaEmail = (fuente: string) =>
    setCoincidenciasEmail(prev =>
      prev.includes(fuente) ? prev.filter(f => f !== fuente) : [...prev, fuente],
    );

  const handleVerPreview  = () => { if (seleccion) setFiltroActivo(construirFiltro()); };
  const handleExportar    = () => { if (seleccion) exportMutation.mutate(construirFiltro()); };
  const handleExportarCsv = () => { if (seleccion) exportCsvMutation.mutate(construirFiltro()); };

  const toggleColumna = (nombre: string) => {
    if (COLUMNAS_FIJAS.includes(nombre)) return; // Doc + Nombre no se desmarcan
    setColumnasSeleccionadas(prev =>
      prev.includes(nombre) ? prev.filter(c => c !== nombre) : [...prev, nombre],
    );
  };

  // Columnas visibles en tabla = seleccionadas o todas si lista vacía
  const columnasVisibles: ColumnaBaseMadre[] =
    columnasSeleccionadas.length === 0
      ? columnas
      : columnas.filter(c => columnasSeleccionadas.includes(c.nombre));

  // ── Render ─────────────────────────────────────────────────────────────────
  return (
    <div style={{ maxWidth: '960px' }}>
      {/* eslint-disable-next-line jsx-a11y/anchor-has-content */}
      <a ref={anchorRef} style={{ display: 'none' }} />

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12, marginBottom: 24 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Creador Zentinela
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Genera y descarga Bases Madre filtradas de tus carteras.
          </p>
        </div>
      </div>

      {/* ── Card 1: Cartera + Filtros ───────────────────────────────────────── */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '10px', padding: '20px', marginBottom: '16px',
      }}>
        <div style={{ marginBottom: '16px' }}>
          <label style={labelStyle}>Cartera</label>
          <select
            value={seleccion ? claveSeleccion(seleccion.carteraID, seleccion.liderID) : ''}
            onChange={e => {
              const v = e.target.value;
              if (!v) { setSeleccion(null); setFiltroActivo(null); return; }
              const [cid, lid] = v.split('|');
              setSeleccion({
                carteraID: Number(cid),
                liderID:   lid === '' ? null : Number(lid),
              });
              setFiltroActivo(null);
            }}
            style={{ ...inputStyle, cursor: 'pointer' }}
          >
            <option value="">Selecciona una cartera...</option>
            {opciones.map(o => (
              <option
                key={claveSeleccion(o.carteraID, o.liderID)}
                value={claveSeleccion(o.carteraID, o.liderID)}
              >
                {o.etiqueta}
              </option>
            ))}
          </select>
        </div>

        {/* Toggle filtros */}
        <button
          onClick={() => setFiltrosAbiertos(p => !p)}
          style={{
            display: 'flex', alignItems: 'center', gap: '6px', background: 'none',
            border: 'none', cursor: 'pointer', fontSize: '12px', fontWeight: 600,
            color: 'var(--z-text-muted)', padding: '0', marginBottom: filtrosAbiertos ? '16px' : '0',
          }}
        >
          <span style={{ display: 'inline-block', transform: filtrosAbiertos ? 'rotate(90deg)' : 'none', transition: 'transform 0.15s', lineHeight: 1 }}>▶</span>
          Filtros opcionales
        </button>

        {filtrosAbiertos && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: '16px' }}>

            {/* Fila 1: perfiles + saldo mínimo */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '12px 16px' }}>
              <div>
                <label style={labelStyle}>Último Perfil</label>
                <select value={ultimoPerfil} onChange={e => setUltimoPerfil(e.target.value)} style={{ ...inputStyle, cursor: 'pointer' }}>
                  <option value="">Todos</option>
                  {TIPIFICACIONES.map(t => <option key={t} value={t}>{t}</option>)}
                </select>
              </div>
              <div>
                <label style={labelStyle}>Mejor Perfil</label>
                <select value={mejorPerfil} onChange={e => setMejorPerfil(e.target.value)} style={{ ...inputStyle, cursor: 'pointer' }}>
                  <option value="">Todos</option>
                  {TIPIFICACIONES.map(t => <option key={t} value={t}>{t}</option>)}
                </select>
              </div>
              <div>
                <label style={labelStyle}>Saldo mínimo ($)</label>
                <input
                  type="number" min="0" value={saldoMinimo}
                  onChange={e => setSaldoMinimo(e.target.value)}
                  placeholder="Ej: 500000"
                  style={inputStyle}
                />
              </div>
            </div>

            {/* Fila 2: Fosyga + promesas + rotación */}
            <div style={{ display: 'grid', gridTemplateColumns: 'repeat(4, 1fr)', gap: '12px 16px', alignItems: 'end' }}>
              <div>
                <label style={labelStyle}>Tipo Afiliado (Fosyga)</label>
                <select value={tipoAfiliado} onChange={e => setTipoAfiliado(e.target.value)} style={{ ...inputStyle, cursor: 'pointer' }}>
                  <option value="">Todos</option>
                  <option value="Cotizante">Cotizante</option>
                  <option value="Beneficiario">Beneficiario</option>
                  <option value="Pensionado">Pensionado</option>
                  <option value="Subsidiado">Subsidiado</option>
                </select>
              </div>
              <div>
                <label style={labelStyle}>Estado en Fosyga</label>
                <select value={estadoFosyga} onChange={e => setEstadoFosyga(e.target.value)} style={{ ...inputStyle, cursor: 'pointer' }}>
                  <option value="">Todos</option>
                  <option value="Activo">Activo</option>
                  <option value="Inactivo">Inactivo</option>
                  <option value="Retirado">Retirado</option>
                </select>
              </div>
              <div>
                <label style={labelStyle}>Promesas de pago</label>
                <select value={promesasVigentes} onChange={e => setPromesasVigentes(e.target.value as 'all' | 'vigentes' | 'sin')} style={{ ...inputStyle, cursor: 'pointer' }}>
                  <option value="all">Todos</option>
                  <option value="vigentes">Con promesa vigente (≥ hoy)</option>
                  <option value="sin">Sin promesa vigente</option>
                </select>
              </div>
              <div>
                <label style={labelStyle}>Rotación máxima</label>
                <input
                  type="number" min="0" value={rotacionMaxima}
                  onChange={e => setRotacionMaxima(e.target.value)}
                  placeholder="Ej: 5"
                  style={inputStyle}
                />
              </div>
            </div>

            {/* Fila 3: Fuentes de celular */}
            <div>
              <label style={{ ...labelStyle, marginBottom: '8px' }}>
                Fuentes con celular requerido
                <span style={{ fontWeight: 400, textTransform: 'none', letterSpacing: 0, marginLeft: '6px', color: 'var(--z-text-muted)' }}>
                  — solo titulares con número en todas las fuentes seleccionadas
                </span>
              </label>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                {FUENTES_CELULAR.map(f => {
                  const activa = fuentesCelular.includes(f.key);
                  return (
                    <button
                      key={f.key}
                      type="button"
                      onClick={() => toggleFuente(f.key)}
                      style={{
                        padding: '5px 12px', borderRadius: '100px', fontSize: '12px', fontWeight: 500,
                        cursor: 'pointer', transition: 'all 0.15s',
                        border: activa ? '1px solid var(--z-brand)' : '1px solid var(--z-border)',
                        background: activa ? 'var(--z-brand-light)' : 'transparent',
                        color: activa ? 'var(--z-brand)' : 'var(--z-text-muted)',
                      }}
                    >
                      {f.label}
                    </button>
                  );
                })}
              </div>
              {fuentesCelular.length > 0 && (
                <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '6px' }}>
                  Filtrando titulares con celular en: {fuentesCelular.map(k => FUENTES_CELULAR.find(f => f.key === k)?.label).join(' + ')}
                </p>
              )}
            </div>

            {/* Fila 4: Coincidencias exactas de celular */}
            <div>
              <label style={{ ...labelStyle, marginBottom: '8px' }}>
                Coincidencias de celular entre:
                <span style={{ fontWeight: 400, textTransform: 'none', letterSpacing: 0, marginLeft: '6px', color: 'var(--z-text-muted)' }}>
                  — solo titulares donde el número sea exactamente igual en todas las fuentes elegidas
                </span>
              </label>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                {FUENTES_CELULAR.map(f => {
                  const activa = coincidenciasCelular.includes(f.key);
                  return (
                    <button
                      key={f.key}
                      type="button"
                      onClick={() => toggleCoincidencia(f.key)}
                      style={{
                        padding: '5px 12px', borderRadius: '100px', fontSize: '12px', fontWeight: 500,
                        cursor: 'pointer', transition: 'all 0.15s',
                        border: activa ? '1px solid #16a34a' : '1px solid var(--z-border)',
                        background: activa ? '#dcfce7' : 'transparent',
                        color: activa ? '#16a34a' : 'var(--z-text-muted)',
                      }}
                    >
                      {f.label}
                    </button>
                  );
                })}
              </div>
              <p style={{ fontSize: '11px', color: coincidenciasCelular.length === 1 ? '#d97706' : 'var(--z-text-muted)', marginTop: '6px' }}>
                {coincidenciasCelular.length === 0 && 'Selecciona al menos 2 fuentes para activar este filtro.'}
                {coincidenciasCelular.length === 1 && 'Selecciona al menos 1 fuente más para activar la comparación.'}
                {coincidenciasCelular.length >= 2 && (
                  <>Comparando: {coincidenciasCelular.map(k => FUENTES_CELULAR.find(f => f.key === k)?.label).join(' = ')}</>
                )}
              </p>
            </div>

            {/* Fila 5: Fuentes de email requerido */}
            <div>
              <label style={{ ...labelStyle, marginBottom: '8px' }}>
                Fuentes con email requerido
                <span style={{ fontWeight: 400, textTransform: 'none', letterSpacing: 0, marginLeft: '6px', color: 'var(--z-text-muted)' }}>
                  — solo titulares con email en todas las fuentes seleccionadas
                </span>
              </label>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                {FUENTES_EMAIL.map(f => {
                  const activa = fuentesEmail.includes(f.key);
                  return (
                    <button
                      key={f.key}
                      type="button"
                      onClick={() => toggleFuenteEmail(f.key)}
                      style={{
                        padding: '5px 12px', borderRadius: '100px', fontSize: '12px', fontWeight: 500,
                        cursor: 'pointer', transition: 'all 0.15s',
                        border: activa ? '1px solid var(--z-brand)' : '1px solid var(--z-border)',
                        background: activa ? 'var(--z-brand-light)' : 'transparent',
                        color: activa ? 'var(--z-brand)' : 'var(--z-text-muted)',
                      }}
                    >
                      {f.label}
                    </button>
                  );
                })}
              </div>
              {fuentesEmail.length > 0 && (
                <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '6px' }}>
                  Filtrando titulares con email en: {fuentesEmail.map(k => FUENTES_EMAIL.find(f => f.key === k)?.label).join(' + ')}
                </p>
              )}
            </div>

            {/* Fila 6: Coincidencias exactas de email */}
            <div>
              <label style={{ ...labelStyle, marginBottom: '8px' }}>
                Coincidencias de email entre:
                <span style={{ fontWeight: 400, textTransform: 'none', letterSpacing: 0, marginLeft: '6px', color: 'var(--z-text-muted)' }}>
                  — solo titulares donde el email sea exactamente igual en todas las fuentes elegidas
                </span>
              </label>
              <div style={{ display: 'flex', flexWrap: 'wrap', gap: '8px' }}>
                {FUENTES_EMAIL.map(f => {
                  const activa = coincidenciasEmail.includes(f.key);
                  return (
                    <button
                      key={f.key}
                      type="button"
                      onClick={() => toggleCoincidenciaEmail(f.key)}
                      style={{
                        padding: '5px 12px', borderRadius: '100px', fontSize: '12px', fontWeight: 500,
                        cursor: 'pointer', transition: 'all 0.15s',
                        border: activa ? '1px solid #16a34a' : '1px solid var(--z-border)',
                        background: activa ? '#dcfce7' : 'transparent',
                        color: activa ? '#16a34a' : 'var(--z-text-muted)',
                      }}
                    >
                      {f.label}
                    </button>
                  );
                })}
              </div>
              <p style={{ fontSize: '11px', color: coincidenciasEmail.length === 1 ? '#d97706' : 'var(--z-text-muted)', marginTop: '6px' }}>
                {coincidenciasEmail.length === 0 && 'Selecciona al menos 2 fuentes para activar este filtro.'}
                {coincidenciasEmail.length === 1 && 'Selecciona al menos 1 fuente más para activar la comparación.'}
                {coincidenciasEmail.length >= 2 && (
                  <>Comparando: {coincidenciasEmail.map(k => FUENTES_EMAIL.find(f => f.key === k)?.label).join(' = ')}</>
                )}
              </p>
            </div>

          </div>
        )}

        <div style={{ marginTop: '16px' }}>
          <button
            onClick={handleVerPreview}
            disabled={!seleccion || cargandoPreview}
            style={{
              padding: '8px 20px', borderRadius: '6px', border: 'none',
              background: seleccion ? 'var(--z-brand)' : 'var(--z-border)',
              color: 'white', fontSize: '13px', fontWeight: 600,
              cursor: seleccion ? 'pointer' : 'not-allowed',
              opacity: cargandoPreview ? 0.7 : 1,
            }}
          >
            {cargandoPreview ? 'Consultando...' : 'Ver preview'}
          </button>
        </div>
      </div>

      {/* ── Card 2: Selector de columnas ────────────────────────────────────── */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '10px', padding: '20px', marginBottom: '16px',
      }}>
        <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '14px' }}>
          <p style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)' }}>
            Columnas en el Excel
          </p>
          <div style={{ display: 'flex', gap: '8px' }}>
            <button
              onClick={() => setColumnasSeleccionadas([])}
              style={{ padding: '4px 10px', fontSize: '11px', borderRadius: '4px', border: '1px solid var(--z-border)', background: 'transparent', cursor: 'pointer', color: 'var(--z-text-muted)' }}
            >
              Todas
            </button>
            <button
              onClick={() => setColumnasSeleccionadas(columnas.map(c => c.nombre).filter(n => COLUMNAS_FIJAS.includes(n) || n.startsWith('Celular')))}
              style={{ padding: '4px 10px', fontSize: '11px', borderRadius: '4px', border: '1px solid var(--z-border)', background: 'transparent', cursor: 'pointer', color: 'var(--z-text-muted)' }}
            >
              Solo celulares
            </button>
            <button
              onClick={() => setColumnasSeleccionadas(columnas.map(c => c.nombre).filter(n => COLUMNAS_FIJAS.includes(n) || n.startsWith('Email')))}
              style={{ padding: '4px 10px', fontSize: '11px', borderRadius: '4px', border: '1px solid var(--z-border)', background: 'transparent', cursor: 'pointer', color: 'var(--z-text-muted)' }}
            >
              Solo emails
            </button>
            <button
              onClick={() => setColumnasSeleccionadas([...COLUMNAS_FIJAS])}
              style={{ padding: '4px 10px', fontSize: '11px', borderRadius: '4px', border: '1px solid var(--z-border)', background: 'transparent', cursor: 'pointer', color: 'var(--z-text-muted)' }}
            >
              Ninguna
            </button>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '6px 12px' }}>
          {columnas.map(col => {
            const esFija = COLUMNAS_FIJAS.includes(col.nombre);
            const activa = esFija || columnasSeleccionadas.length === 0 || columnasSeleccionadas.includes(col.nombre);
            return (
              <label key={col.nombre} style={{
                display: 'flex', alignItems: 'center', gap: '7px', fontSize: '12px',
                cursor: esFija ? 'not-allowed' : 'pointer',
                color: activa ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
              }}>
                <input
                  type="checkbox"
                  checked={activa}
                  disabled={esFija}
                  onChange={() => {
                    if (esFija) return; // defensa: el disabled ya bloquea, esto cubre clicks programáticos
                    if (columnasSeleccionadas.length === 0) {
                      setColumnasSeleccionadas(columnas.map(c => c.nombre).filter(n => n !== col.nombre));
                    } else {
                      toggleColumna(col.nombre);
                    }
                  }}
                />
                {col.etiqueta}
              </label>
            );
          })}
        </div>

        <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginTop: '10px' }}>
          {columnasSeleccionadas.length === 0
            ? `Todas las columnas (${columnas.length})`
            : `${columnasSeleccionadas.length} de ${columnas.length} columnas seleccionadas`}
        </p>
      </div>

      {/* ── Card 3: Preview + descarga ──────────────────────────────────────── */}
      {filtroActivo && (
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: '10px', padding: '20px',
        }}>
          <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'center', marginBottom: '14px' }}>
            <p style={{ fontSize: '13px', fontWeight: 600, color: 'var(--z-text-primary)' }}>
              {preview
                ? `${preview.total.toLocaleString('es-CO')} titular${preview.total !== 1 ? 'es' : ''} encontrado${preview.total !== 1 ? 's' : ''}`
                : 'Consultando...'}
              {preview && preview.total > 10 && (
                <span style={{ fontWeight: 400, color: 'var(--z-text-muted)' }}> — mostrando primeros 10</span>
              )}
            </p>

            <div style={{ display: 'flex', gap: '8px' }}>
              <button
                onClick={handleExportarCsv}
                disabled={!preview || preview.total === 0 || exportCsvMutation.isPending}
                title="Recomendado para carteras grandes — descarga en streaming, abre directo en Excel"
                style={{
                  display: 'flex', alignItems: 'center', gap: '6px',
                  padding: '8px 18px', borderRadius: '6px',
                  border: '1px solid #16a34a', background: 'transparent',
                  color: preview && preview.total > 0 ? '#16a34a' : 'var(--z-text-muted)',
                  fontSize: '13px', fontWeight: 600,
                  cursor: preview && preview.total > 0 ? 'pointer' : 'not-allowed',
                  opacity: exportCsvMutation.isPending ? 0.7 : 1,
                }}
              >
                {exportCsvMutation.isPending ? 'Generando...' : '↓ Descargar CSV'}
              </button>

              <button
                onClick={handleExportar}
                disabled={!preview || preview.total === 0 || exportMutation.isPending}
                style={{
                  display: 'flex', alignItems: 'center', gap: '6px',
                  padding: '8px 18px', borderRadius: '6px', border: 'none',
                  background: preview && preview.total > 0 ? '#16a34a' : 'var(--z-border)',
                  color: 'white', fontSize: '13px', fontWeight: 600,
                  cursor: preview && preview.total > 0 ? 'pointer' : 'not-allowed',
                  opacity: exportMutation.isPending ? 0.7 : 1,
                }}
              >
                {exportMutation.isPending ? 'Generando...' : '↓ Descargar Excel'}
              </button>
            </div>
          </div>

          {(exportMutation.isError || exportCsvMutation.isError) && (
            <p style={{ fontSize: '12px', color: '#dc2626', marginBottom: '12px' }}>
              Error al generar la descarga. Intenta de nuevo.
            </p>
          )}

          {cargandoPreview ? (
            <CargandoSpiral mensaje="Generando previsualización..." />
          ) : preview && preview.filas.length > 0 ? (
            <div style={{ overflowX: 'auto', borderRadius: '6px', border: '0.5px solid var(--z-border)' }}>
              <table style={{ width: '100%', borderCollapse: 'collapse', fontSize: '12px', minWidth: '600px' }}>
                <thead>
                  <tr style={{ background: 'var(--z-bg-surface)' }}>
                    {columnasVisibles.map(col => (
                      <th key={col.nombre} style={{
                        padding: '8px 12px', textAlign: 'left', fontSize: '11px', fontWeight: 600,
                        color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em',
                        whiteSpace: 'nowrap', borderBottom: '0.5px solid var(--z-border)',
                      }}>
                        {col.etiqueta}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {preview.filas.map((fila, i) => (
                    <tr key={i} style={{ borderBottom: i < preview.filas.length - 1 ? '0.5px solid var(--z-border)' : 'none' }}>
                      {columnasVisibles.map(col => (
                        <td key={col.nombre} style={{
                          padding: '7px 12px', color: 'var(--z-text-primary)',
                          textAlign: col.tipo === 'Numero' ? 'right' : 'left',
                          whiteSpace: 'nowrap',
                        }}>
                          {formatearValor(fila[col.nombre], col.tipo)}
                        </td>
                      ))}
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          ) : preview && preview.filas.length === 0 ? (
            <div style={{ padding: '24px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
              No hay titulares que cumplan los filtros aplicados.
            </div>
          ) : null}
        </div>
      )}
    </div>
  );
}

function formatearValor(valor: unknown, tipo: ColumnaBaseMadre['tipo']): string {
  if (valor === null || valor === undefined) return '';
  if (tipo === 'Fecha' && typeof valor === 'string' && valor) {
    const f = new Date(valor);
    if (!isNaN(f.getTime()))
      return f.toLocaleDateString('es-CO', { day: '2-digit', month: '2-digit', year: 'numeric' });
  }
  return String(valor);
}
