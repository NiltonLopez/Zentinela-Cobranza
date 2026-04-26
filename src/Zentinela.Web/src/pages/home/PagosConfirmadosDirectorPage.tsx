import { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  obtenerPagosDirector,
  obtenerLideresDropdown,
  obtenerCarterasLider,
} from '../../api/dashboardApi';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import type { PagoDirectorDto } from '../../types/dashboard';

// ── Tipos ─────────────────────────────────────────────────────────────────────

type Preset = 'hoy' | 'ciclo' | 'mes-pasado' | '7d' | '30d' | 'custom';

interface Rango { desde: string; hasta: string; }

// ── Helpers ───────────────────────────────────────────────────────────────────

function isoHoy(): string {
  return new Date().toISOString().slice(0, 10);
}

function isoFecha(d: Date): string {
  return d.toISOString().slice(0, 10);
}

function calcularRango(preset: Preset, customDesde: string, customHasta: string): Rango {
  const hoy = new Date();
  switch (preset) {
    case 'hoy':
      return { desde: isoHoy(), hasta: isoHoy() };
    case 'ciclo': {
      const inicio = new Date(hoy.getFullYear(), hoy.getMonth(), 1);
      return { desde: isoFecha(inicio), hasta: isoHoy() };
    }
    case 'mes-pasado': {
      const inicio = new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1);
      const fin    = new Date(hoy.getFullYear(), hoy.getMonth(), 0);
      return { desde: isoFecha(inicio), hasta: isoFecha(fin) };
    }
    case '7d': {
      const inicio = new Date(hoy);
      inicio.setDate(hoy.getDate() - 6);
      return { desde: isoFecha(inicio), hasta: isoHoy() };
    }
    case '30d': {
      const inicio = new Date(hoy);
      inicio.setDate(hoy.getDate() - 29);
      return { desde: isoFecha(inicio), hasta: isoHoy() };
    }
    case 'custom':
      return { desde: customDesde || isoHoy(), hasta: customHasta || isoHoy() };
  }
}

function formatearMoneda(valor: number): string {
  return new Intl.NumberFormat('es-CO', {
    style: 'currency', currency: 'COP', maximumFractionDigits: 0,
  }).format(valor);
}

function formatearFecha(iso: string): string {
  return new Date(iso + 'T00:00:00').toLocaleDateString('es-CO', {
    day: 'numeric', month: 'short', year: 'numeric',
  });
}

function formatearFechaHora(iso: string): string {
  return new Date(iso).toLocaleString('es-CO', {
    day: 'numeric', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  });
}

// ── Constantes de presets ─────────────────────────────────────────────────────

const PRESETS: { valor: Preset; label: string }[] = [
  { valor: 'hoy',        label: 'Hoy' },
  { valor: 'ciclo',      label: 'Este mes' },
  { valor: 'mes-pasado', label: 'Mes pasado' },
  { valor: '7d',         label: 'Últimos 7 días' },
  { valor: '30d',        label: 'Últimos 30 días' },
  { valor: 'custom',     label: 'Personalizado' },
];

// ── Ordenamiento ──────────────────────────────────────────────────────────────

type SortDir = 'asc' | 'desc';

function useSortable<T extends Record<string, unknown>>(items: T[]) {
  const [key, setKey] = useState<keyof T | null>(null);
  const [dir, setDir] = useState<SortDir>('asc');

  const handleSort = (k: keyof T) => {
    if (key === k) setDir(d => d === 'asc' ? 'desc' : 'asc');
    else { setKey(k); setDir('asc'); }
  };

  const sorted = useMemo(() => {
    if (!key) return items;
    return [...items].sort((a, b) => {
      const va = a[key], vb = b[key];
      const cmp = typeof va === 'string'
        ? (va as string).localeCompare(vb as string, 'es')
        : (va as number) - (vb as number);
      return dir === 'asc' ? cmp : -cmp;
    });
  }, [items, key, dir]);

  return { sorted, sortKey: key, sortDir: dir, handleSort };
}

function ThSort({ label, k, sortKey, sortDir, onSort, align = 'left' }: {
  label: string; k: string;
  sortKey: string | null; sortDir: SortDir;
  onSort: (k: string) => void;
  align?: 'left' | 'right' | 'center';
}) {
  const active = sortKey === k;
  return (
    <th
      onClick={() => onSort(k)}
      style={{
        padding: '10px 14px',
        textAlign: align,
        fontSize: '11px', fontWeight: 600,
        color: active ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
        borderBottom: '0.5px solid var(--z-border)',
        letterSpacing: '0.04em',
        textTransform: 'uppercase',
        whiteSpace: 'nowrap',
        cursor: 'pointer',
        userSelect: 'none',
      }}
    >
      {label}&nbsp;
      <span style={{ fontSize: '10px', opacity: active ? 1 : 0.25 }}>
        {active ? (sortDir === 'asc' ? '▲' : '▼') : '▲▼'}
      </span>
    </th>
  );
}

// ── Tarjeta de resumen ────────────────────────────────────────────────────────

function TarjetaResumen({ label, valor }: { label: string; valor: string }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '0.5px solid var(--z-border)',
      borderRadius: '8px',
      padding: '16px 20px',
      flex: '1 1 160px',
      minWidth: '160px',
    }}>
      <p style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginBottom: '6px', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
        {label}
      </p>
      <p style={{ fontSize: '20px', fontWeight: 600, color: 'var(--z-text-primary)', wordBreak: 'break-word' }}>
        {valor}
      </p>
    </div>
  );
}

// ── Fila de tabla ─────────────────────────────────────────────────────────────

const TD: React.CSSProperties = {
  padding: '10px 14px',
  fontSize: '13px',
  color: 'var(--z-text-primary)',
  borderBottom: '0.5px solid var(--z-border)',
  whiteSpace: 'nowrap',
};

function FilaPago({ pago, idx }: { pago: PagoDirectorDto; idx: number }) {
  const bg = idx % 2 === 0 ? 'var(--z-bg-card)' : 'var(--z-bg-page)';
  return (
    <tr>
      <td style={{ ...TD, background: bg }}>
        {pago.nombreCliente}
        <span style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginLeft: '6px' }}>
          {pago.cedulaTitular}
        </span>
      </td>
      <td style={{ ...TD, background: bg }}>{pago.nombreAsesor}</td>
      <td style={{ ...TD, background: bg }}>{pago.nombreLider}</td>
      <td style={{ ...TD, background: bg }}>{pago.nombreCartera}</td>
      <td style={{ ...TD, background: bg }}>{formatearFecha(pago.fechaPago)}</td>
      <td style={{ ...TD, background: bg, textAlign: 'right', fontWeight: 500 }}>
        {formatearMoneda(pago.valorPagado)}
      </td>
      <td style={{ ...TD, background: bg, textAlign: 'right' }}>
        {formatearMoneda(pago.honorarios)}
      </td>
      <td style={{ ...TD, background: bg, fontSize: '12px', color: 'var(--z-text-muted)' }}>
        {formatearFechaHora(pago.fechaRegistro)}
      </td>
    </tr>
  );
}

// ── Componente principal ──────────────────────────────────────────────────────

export function PagosConfirmadosDirectorPage() {
  const [liderID,     setLiderID]     = useState<number | null>(null);
  const [liderNombre, setLiderNombre] = useState<string>('');
  const [carteraID,   setCarteraID]   = useState<number | null>(null);

  // Selector de período
  const [preset,      setPreset]      = useState<Preset>('ciclo');
  const [customDesde, setCustomDesde] = useState<string>('');
  const [customHasta, setCustomHasta] = useState<string>('');

  const rango = calcularRango(preset, customDesde, customHasta);

  // Dropdowns
  const { data: lideres = [] } = useQuery({
    queryKey: ['director-lideres'],
    queryFn: obtenerLideresDropdown,
    staleTime: 10 * 60 * 1000,
  });

  const { data: carteras = [] } = useQuery({
    queryKey: ['director-carteras', liderID],
    queryFn: () => obtenerCarterasLider(liderID!),
    enabled: liderID !== null,
    staleTime: 5 * 60 * 1000,
  });

  const { data: pagos = [], isLoading, isError } = useQuery({
    queryKey: ['director-pagos', liderID, carteraID, rango.desde, rango.hasta],
    queryFn: () => obtenerPagosDirector(liderID, carteraID, rango.desde, rango.hasta),
    staleTime: 3 * 60 * 1000,
  });

  // Ordenamiento
  const { sorted, sortKey, sortDir, handleSort } =
    useSortable(pagos as unknown as Record<string, unknown>[]);

  const th = (label: string, k: string, align?: 'left' | 'right') => (
    <ThSort
      label={label} k={k}
      sortKey={sortKey as string | null} sortDir={sortDir}
      onSort={handleSort}
      align={align ?? 'left'}
    />
  );

  const totalRecaudo    = pagos.reduce((s, p) => s + p.valorPagado, 0);
  const totalHonorarios = pagos.reduce((s, p) => s + p.honorarios, 0);

  const handleCambiarLider = (e: React.ChangeEvent<HTMLSelectElement>) => {
    const val = Number(e.target.value);
    if (!val) {
      setLiderID(null);
      setLiderNombre('');
      setCarteraID(null);
    } else {
      const lider = lideres.find(l => l.id === val);
      setLiderID(val);
      setLiderNombre(lider?.nombre ?? '');
      setCarteraID(null);
    }
  };

  const handleCambiarCartera = (e: React.ChangeEvent<HTMLSelectElement>) => {
    setCarteraID(Number(e.target.value) || null);
  };

  const limpiarFiltros = () => {
    setLiderID(null);
    setLiderNombre('');
    setCarteraID(null);
    setPreset('ciclo');
    setCustomDesde('');
    setCustomHasta('');
  };

  const hayFiltrosActivos = liderID !== null || carteraID !== null || preset !== 'ciclo';

  const selectStyle: React.CSSProperties = {
    padding: '7px 10px', fontSize: '13px', borderRadius: '6px',
    border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
    color: 'var(--z-text-primary)', outline: 'none',
  };

  const labelStyle: React.CSSProperties = {
    fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)',
    textTransform: 'uppercase', letterSpacing: '0.04em',
  };

  const estiloBtnPreset = (activo: boolean): React.CSSProperties => ({
    padding: '5px 12px',
    borderRadius: '20px',
    border: '0.5px solid var(--z-border)',
    background: activo ? 'var(--z-text-primary)' : 'var(--z-bg-page)',
    color: activo ? 'var(--z-bg-page)' : 'var(--z-text-muted)',
    fontSize: '12px',
    fontWeight: activo ? 600 : 400,
    cursor: 'pointer',
    whiteSpace: 'nowrap',
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1200px' }}>

      {/* Encabezado */}
      <div>
        <h1 style={{ fontSize: '16px', fontWeight: 600, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Pagos confirmados
        </h1>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Todos los pagos registrados en la empresa
          {liderNombre && (
            <> — <span style={{ fontWeight: 500, color: 'var(--z-text-primary)' }}>{liderNombre}</span></>
          )}
        </p>
      </div>

      {/* Filtros */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '8px', padding: '16px 20px',
        display: 'flex', flexDirection: 'column', gap: '14px',
      }}>
        {/* Fila 1: Líder + Cartera + Limpiar */}
        <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '200px' }}>
            <label style={labelStyle}>Líder</label>
            <select value={liderID ?? ''} onChange={handleCambiarLider} style={selectStyle}>
              <option value="">Todos los líderes</option>
              {lideres.map(l => (
                <option key={l.id} value={l.id}>{l.nombre}</option>
              ))}
            </select>
          </div>

          {liderID !== null && (
            <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '200px' }}>
              <label style={labelStyle}>Cartera</label>
              <select value={carteraID ?? ''} onChange={handleCambiarCartera} style={selectStyle}>
                <option value="">Todas las carteras</option>
                {carteras.map(c => (
                  <option key={c.id} value={c.id}>{c.nombreCartera}</option>
                ))}
              </select>
            </div>
          )}

          {hayFiltrosActivos && (
            <button
              onClick={limpiarFiltros}
              style={{
                padding: '7px 14px', borderRadius: '6px',
                border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                color: 'var(--z-text-muted)', fontSize: '12px', cursor: 'pointer',
              }}
            >
              Limpiar filtros
            </button>
          )}
        </div>

        {/* Fila 2: Selector de período */}
        <div>
          <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '8px' }}>
            Período
          </p>
          <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', alignItems: 'center' }}>
            {PRESETS.map(p => (
              <button
                key={p.valor}
                onClick={() => setPreset(p.valor)}
                style={estiloBtnPreset(preset === p.valor)}
              >
                {p.label}
              </button>
            ))}
          </div>

          {preset === 'custom' && (
            <div style={{ display: 'flex', gap: '10px', marginTop: '10px', flexWrap: 'wrap', alignItems: 'flex-end' }}>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                <label style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Desde</label>
                <input
                  type="date"
                  value={customDesde}
                  onChange={e => setCustomDesde(e.target.value)}
                  style={{
                    padding: '6px 10px', fontSize: '13px', borderRadius: '6px',
                    border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                    color: 'var(--z-text-primary)', outline: 'none',
                  }}
                />
              </div>
              <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
                <label style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Hasta</label>
                <input
                  type="date"
                  value={customHasta}
                  onChange={e => setCustomHasta(e.target.value)}
                  style={{
                    padding: '6px 10px', fontSize: '13px', borderRadius: '6px',
                    border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                    color: 'var(--z-text-primary)', outline: 'none',
                  }}
                />
              </div>
            </div>
          )}
        </div>
      </div>

      {/* Resumen */}
      {!isLoading && (
        <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
          <TarjetaResumen label="Total pagos"        valor={pagos.length.toLocaleString('es-CO')} />
          <TarjetaResumen label="Recaudo total"      valor={formatearMoneda(totalRecaudo)} />
          <TarjetaResumen label="Honorarios totales" valor={formatearMoneda(totalHonorarios)} />
        </div>
      )}

      {/* Error */}
      {isError && (
        <div style={{
          background: '#fff5f5', border: '0.5px solid #fecaca',
          borderRadius: '8px', padding: '14px 16px',
          fontSize: '13px', color: '#dc2626',
        }}>
          No se pudieron cargar los pagos. Intenta de nuevo.
        </div>
      )}

      {/* Tabla */}
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '8px',
        overflow: 'hidden',
      }}>
        {isLoading ? (
          <CargandoSpiral mensaje="Cargando pagos..." />
        ) : pagos.length === 0 ? (
          <div style={{ padding: '48px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
            No hay pagos confirmados con los filtros seleccionados.
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: '900px' }}>
              <thead>
                <tr style={{ background: 'var(--z-bg-page)' }}>
                  {th('Cliente',       'nombreCliente')}
                  {th('Asesor',        'nombreAsesor')}
                  {th('Líder',         'nombreLider')}
                  {th('Cartera',       'nombreCartera')}
                  {th('Fecha de pago', 'fechaPago')}
                  {th('Valor pagado',  'valorPagado',  'right')}
                  {th('Honorarios',    'honorarios',   'right')}
                  {th('Registrado',    'fechaRegistro')}
                </tr>
              </thead>
              <tbody>
                {(sorted as unknown as PagoDirectorDto[]).map((p, i) => (
                  <FilaPago key={p.pagoID} pago={p} idx={i} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

    </div>
  );
}
