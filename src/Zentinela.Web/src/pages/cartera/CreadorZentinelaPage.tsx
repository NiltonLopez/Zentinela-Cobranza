import { useState, useRef } from 'react';
import { useQuery, useMutation } from '@tanstack/react-query';
import { obtenerCarteras } from '../../api/carterasApi';
import { obtenerColumnas, obtenerPreview, exportarExcel } from '../../api/creadorApi';
import type { FiltroBaseMadreRequest, ColumnaBaseMadre } from '../../types/creador';

// ── Tipificaciones conocidas para dropdowns ───────────────────────────────────
const TIPIFICACIONES = [
  'Acuerdo de pago abono', 'Acuerdo de pago total', 'Incumplido', 'No hubo acuerdo',
  'Paz y salvo', 'PQR', 'Renuente', 'Realizo abono', 'Recordar',
  'Fraude o suplantacion', 'Potencial acuerdo', 'No gestionar', 'Consignación por aplicar',
  'Mensaje con tercero', 'Mensaje en buzón', 'Fallecido', 'No contacto',
  'Mensaje contestadora/buzon', 'Numero equivocado', 'Ilocalizado',
  'Datos de localización', 'Sin gestion', 'Volver a llamar',
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
  const [carteraID,        setCarteraID]        = useState<number | null>(null);
  const [nombre,           setNombre]           = useState('');
  const [ultimoPerfil,     setUltimoPerfil]     = useState('');
  const [mejorPerfil,      setMejorPerfil]      = useState('');
  const [departamento,     setDepartamento]     = useState('');
  const [municipio,        setMunicipio]        = useState('');
  const [conPromesa,       setConPromesa]       = useState(false);
  const [conSaldo,         setConSaldo]         = useState(false);
  const [filtrosAbiertos,  setFiltrosAbiertos]  = useState(false);
  const [columnasSeleccionadas, setColumnasSeleccionadas] = useState<string[]>([]);
  const [filtroActivo,     setFiltroActivo]     = useState<FiltroBaseMadreRequest | null>(null);

  const anchorRef = useRef<HTMLAnchorElement>(null);

  // ── Queries ────────────────────────────────────────────────────────────────
  const { data: carteras = [] } = useQuery({
    queryKey: ['carteras'],
    queryFn: obtenerCarteras,
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

  // ── Mutación descarga ──────────────────────────────────────────────────────
  const exportMutation = useMutation({
    mutationFn: (filtro: FiltroBaseMadreRequest) => exportarExcel(filtro),
    onSuccess: (blob) => {
      const url          = URL.createObjectURL(blob);
      const carteraNombre = carteras.find(c => c.id === carteraID)?.nombreCartera ?? 'Base';
      const fecha        = new Date().toISOString().slice(0, 10).replace(/-/g, '');
      if (anchorRef.current) {
        anchorRef.current.href     = url;
        anchorRef.current.download = `BaseMadre_${carteraNombre}_${fecha}.xlsx`;
        anchorRef.current.click();
      }
      setTimeout(() => URL.revokeObjectURL(url), 5000);
    },
  });

  // ── Handlers ───────────────────────────────────────────────────────────────
  const construirFiltro = (): FiltroBaseMadreRequest => ({
    carteraID:           carteraID!,
    nombre:              nombre        || undefined,
    ultimoPerfil:        ultimoPerfil  || undefined,
    mejorPerfil:         mejorPerfil   || undefined,
    departamento:        departamento  || undefined,
    municipio:           municipio     || undefined,
    tieneFechaPromesa:   conPromesa    || undefined,
    tieneSaldo:          conSaldo      || undefined,
    columnasSeleccionadas,
  });

  const handleVerPreview  = () => { if (carteraID) setFiltroActivo(construirFiltro()); };
  const handleExportar    = () => { if (carteraID) exportMutation.mutate(construirFiltro()); };

  const toggleColumna = (nombre: string) =>
    setColumnasSeleccionadas(prev =>
      prev.includes(nombre) ? prev.filter(c => c !== nombre) : [...prev, nombre],
    );

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
      <div style={{ marginBottom: '24px' }}>
        <h2 style={{ fontSize: '18px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Creador Zentinela
        </h2>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Genera y descarga Bases Madre filtradas de tus carteras.
        </p>
      </div>

      {/* ── Card 1: Cartera + Filtros ───────────────────────────────────────── */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '10px', padding: '20px', marginBottom: '16px',
      }}>
        <div style={{ marginBottom: '16px' }}>
          <label style={labelStyle}>Cartera</label>
          <select
            value={carteraID ?? ''}
            onChange={e => { setCarteraID(e.target.value ? Number(e.target.value) : null); setFiltroActivo(null); }}
            style={{ ...inputStyle, cursor: 'pointer' }}
          >
            <option value="">Selecciona una cartera...</option>
            {carteras.map(c => (
              <option key={c.id} value={c.id}>{c.nombreCartera}</option>
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
          <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '12px 16px' }}>
            <div>
              <label style={labelStyle}>Nombre (contiene)</label>
              <input type="text" value={nombre} onChange={e => setNombre(e.target.value)}
                placeholder="Buscar por nombre..." style={inputStyle} />
            </div>
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
              <label style={labelStyle}>Departamento</label>
              <input type="text" value={departamento} onChange={e => setDepartamento(e.target.value)}
                placeholder="Ej: Atlántico" style={inputStyle} />
            </div>
            <div>
              <label style={labelStyle}>Municipio</label>
              <input type="text" value={municipio} onChange={e => setMunicipio(e.target.value)}
                placeholder="Ej: Barranquilla" style={inputStyle} />
            </div>
            <div style={{ display: 'flex', flexDirection: 'column', justifyContent: 'center', gap: '8px' }}>
              <label style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', cursor: 'pointer', color: 'var(--z-text-primary)' }}>
                <input type="checkbox" checked={conPromesa} onChange={e => setConPromesa(e.target.checked)} />
                Solo con fecha promesa
              </label>
              <label style={{ display: 'flex', alignItems: 'center', gap: '8px', fontSize: '13px', cursor: 'pointer', color: 'var(--z-text-primary)' }}>
                <input type="checkbox" checked={conSaldo} onChange={e => setConSaldo(e.target.checked)} />
                Solo con saldo
              </label>
            </div>
          </div>
        )}

        <div style={{ marginTop: '16px' }}>
          <button
            onClick={handleVerPreview}
            disabled={!carteraID || cargandoPreview}
            style={{
              padding: '8px 20px', borderRadius: '6px', border: 'none',
              background: carteraID ? 'var(--z-brand)' : 'var(--z-border)',
              color: 'white', fontSize: '13px', fontWeight: 600,
              cursor: carteraID ? 'pointer' : 'not-allowed',
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
              onClick={() => setColumnasSeleccionadas(columnas.map(c => c.nombre).filter(n => n === 'NumeroDocumento' || n === 'Nombre'))}
              style={{ padding: '4px 10px', fontSize: '11px', borderRadius: '4px', border: '1px solid var(--z-border)', background: 'transparent', cursor: 'pointer', color: 'var(--z-text-muted)' }}
            >
              Solo básicas
            </button>
          </div>
        </div>

        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '6px 12px' }}>
          {columnas.map(col => {
            const activa = columnasSeleccionadas.length === 0 || columnasSeleccionadas.includes(col.nombre);
            return (
              <label key={col.nombre} style={{ display: 'flex', alignItems: 'center', gap: '7px', fontSize: '12px', cursor: 'pointer', color: activa ? 'var(--z-text-primary)' : 'var(--z-text-muted)' }}>
                <input
                  type="checkbox"
                  checked={activa}
                  onChange={() => {
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
              {preview && preview.total > 100 && (
                <span style={{ fontWeight: 400, color: 'var(--z-text-muted)' }}> — mostrando primeros 100</span>
              )}
            </p>

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

          {exportMutation.isError && (
            <p style={{ fontSize: '12px', color: '#dc2626', marginBottom: '12px' }}>
              Error al generar el Excel. Intenta de nuevo.
            </p>
          )}

          {cargandoPreview ? (
            <div style={{ padding: '24px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
              Consultando datos...
            </div>
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
