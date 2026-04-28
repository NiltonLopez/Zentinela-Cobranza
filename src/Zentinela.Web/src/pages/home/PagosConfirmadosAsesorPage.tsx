import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { obtenerPagosAsesor } from '../../api/dashboardApi';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import type { PagoAsesorDto } from '../../types/dashboard';

function primerDiaMes(): string {
  const hoy = new Date();
  return new Date(hoy.getFullYear(), hoy.getMonth(), 1).toISOString().slice(0, 10);
}

function hoyISO(): string {
  return new Date().toISOString().slice(0, 10);
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

// ── Tarjetas de resumen ───────────────────────────────────────────────────────

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
  const [fechaDesde, setFechaDesde] = useState(primerDiaMes);
  const [fechaHasta, setFechaHasta] = useState(hoyISO);

  const { data: pagos = [], isLoading, isError } = useQuery({
    queryKey: ['asesor-pagos-detalle', fechaDesde, fechaHasta],
    queryFn: () => obtenerPagosAsesor(fechaDesde || undefined, fechaHasta || undefined),
    staleTime: 5 * 60 * 1000,
  });

  const totalRecaudo    = pagos.reduce((s, p) => s + p.valorPagado, 0);
  const totalHonorarios = pagos.reduce((s, p) => s + p.honorarios, 0);

  const COLS = ['Cliente', 'Cartera', 'Fecha de pago', 'Valor pagado', 'Honorarios', 'Registrado'];

  const inputStyle: React.CSSProperties = {
    padding: '6px 10px', fontSize: '13px', borderRadius: '6px',
    border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
    color: 'var(--z-text-primary)', outline: 'none',
  };

  const labelStyle: React.CSSProperties = {
    fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)',
    textTransform: 'uppercase', letterSpacing: '0.04em',
  };

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

      {/* Filtro de fechas */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '8px', padding: '14px 20px',
        display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap',
      }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label style={labelStyle}>Fecha desde</label>
          <input type="date" value={fechaDesde} onChange={e => setFechaDesde(e.target.value)} style={inputStyle} />
        </div>
        <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
          <label style={labelStyle}>Fecha hasta</label>
          <input type="date" value={fechaHasta} onChange={e => setFechaHasta(e.target.value)} style={inputStyle} />
        </div>
      </div>

      {/* Resumen */}
      {!isLoading && (
        <div style={{ display: 'flex', gap: '12px', flexWrap: 'wrap' }}>
          <TarjetaResumen label="Total pagos" valor={String(pagos.length)} />
          <TarjetaResumen label="Recaudo total" valor={formatearMoneda(totalRecaudo)} />
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
            No tienes pagos confirmados registrados.
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
                {pagos.map((p, i) => (
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
