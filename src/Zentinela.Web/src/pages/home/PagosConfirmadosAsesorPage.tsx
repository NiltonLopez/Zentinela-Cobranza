import { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { obtenerPagosAsesor } from '../../api/dashboardApi';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { FiltrosPeriodo, calcularRango } from '../../components/pagos/FiltrosPeriodo';
import { TarjetaResumenPagos } from '../../components/pagos/TarjetaResumenPagos';
import { formatearMoneda, formatearFecha, formatearFechaHora } from '../../utils/formatters';
import type { Preset } from '../../components/pagos/FiltrosPeriodo';
import type { PagoAsesorDto } from '../../types/dashboard';

// ── Fila de tabla ─────────────────────────────────────────────────────────────

function FilaPago({ pago, idx }: { pago: PagoAsesorDto; idx: number }) {
  const bg = idx % 2 === 0 ? 'var(--z-bg-card)' : 'var(--z-bg-page)';
  const tdStyle: React.CSSProperties = {
    padding: '10px 14px',
    fontSize: '13px',
    color: 'var(--z-text-primary)',
    borderBottom: '0.5px solid var(--z-border)',
    background: bg,
  };
  return (
    <tr>
      <td style={tdStyle}>
        {pago.nombreCliente}
        <span style={{ fontSize: '11px', color: 'var(--z-text-muted)', marginLeft: '6px' }}>
          {pago.cedulaTitular}
        </span>
      </td>
      <td style={tdStyle}>{pago.nombreCartera}</td>
      <td style={tdStyle}>{formatearFecha(pago.fechaPago)}</td>
      <td style={{ ...tdStyle, textAlign: 'right', fontWeight: 500 }}>
        {formatearMoneda(pago.valorPagado)}
      </td>
      <td style={{ ...tdStyle, textAlign: 'right' }}>
        {formatearMoneda(pago.honorarios)}
      </td>
      <td style={{ ...tdStyle, fontSize: '12px', color: 'var(--z-text-muted)' }}>
        {formatearFechaHora(pago.fechaRegistro)}
      </td>
    </tr>
  );
}

// ── Componente principal ──────────────────────────────────────────────────────

export function PagosConfirmadosAsesorPage() {
  const [preset,       setPreset]       = useState<Preset>('ciclo');
  const [customDesde,  setCustomDesde]  = useState('');
  const [customHasta,  setCustomHasta]  = useState('');
  const [carteraFiltro, setCarteraFiltro] = useState('');

  const rango = calcularRango(preset, customDesde, customHasta);

  const { data: pagos = [], isLoading, isError } = useQuery({
    queryKey: ['asesor-pagos-detalle', rango.desde, rango.hasta],
    queryFn: () => obtenerPagosAsesor(rango.desde, rango.hasta),
    staleTime: 5 * 60 * 1000,
  });

  // Carteras únicas derivadas del resultado — para el selector client-side
  const carteras = useMemo(() => {
    const vistas = new Set<string>();
    return pagos
      .map(p => p.nombreCartera)
      .filter(n => n && !vistas.has(n) && vistas.add(n))
      .sort();
  }, [pagos]);

  const pagosFiltrados = carteraFiltro
    ? pagos.filter(p => p.nombreCartera === carteraFiltro)
    : pagos;

  const totalRecaudo    = pagosFiltrados.reduce((s, p) => s + p.valorPagado, 0);
  const totalHonorarios = pagosFiltrados.reduce((s, p) => s + p.honorarios, 0);

  const COLS = ['Cliente', 'Cartera', 'Fecha de pago', 'Valor pagado', 'Honorarios', 'Registrado'];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1100px' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Pagos confirmados
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Pagos registrados a tu nombre
          </p>
        </div>
      </div>

      {/* Filtros */}
      <div style={{ background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: '8px', padding: '16px 20px', display: 'flex', flexDirection: 'column', gap: '14px' }}>
        {/* Cartera (client-side) */}
        <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '220px' }}>
            <label style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>Cartera</label>
            <select value={carteraFiltro} onChange={e => setCarteraFiltro(e.target.value)}
              style={{ padding: '7px 10px', fontSize: '13px', borderRadius: '6px', border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }}>
              <option value="">Todas mis carteras</option>
              {carteras.map(c => <option key={c} value={c}>{c}</option>)}
            </select>
          </div>
          {carteraFiltro && (
            <button onClick={() => setCarteraFiltro('')}
              style={{ padding: '7px 14px', borderRadius: '6px', border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-muted)', fontSize: '12px', cursor: 'pointer' }}>
              Ver todas
            </button>
          )}
        </div>
        <FiltrosPeriodo
          preset={preset}           onPresetChange={setPreset}
          customDesde={customDesde} onCustomDesdeChange={setCustomDesde}
          customHasta={customHasta} onCustomHastaChange={setCustomHasta}
        />
      </div>

      {/* Resumen */}
      {!isLoading && (
        <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
          <TarjetaResumenPagos label="Total pagos"        valor={pagosFiltrados.length.toLocaleString('es-CO')} />
          <TarjetaResumenPagos label="Recaudo total"      valor={formatearMoneda(totalRecaudo)} />
          <TarjetaResumenPagos label="Honorarios totales" valor={formatearMoneda(totalHonorarios)} />
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
        ) : pagosFiltrados.length === 0 ? (
          <div style={{ padding: '48px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
            No tienes pagos confirmados con los filtros seleccionados.
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: '700px' }}>
              <thead>
                <tr style={{ background: 'var(--z-bg-page)' }}>
                  {COLS.map((col) => (
                    <th key={col} style={{
                      padding: '10px 14px',
                      textAlign: (col === 'Valor pagado' || col === 'Honorarios') ? 'right' : 'left',
                      fontSize: '11px', fontWeight: 600,
                      color: 'var(--z-text-muted)',
                      borderBottom: '0.5px solid var(--z-border)',
                      letterSpacing: '0.04em',
                      textTransform: 'uppercase',
                      whiteSpace: 'nowrap',
                    }}>
                      {col}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {pagosFiltrados.map((p, i) => (
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
