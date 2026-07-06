import { useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  obtenerPagosLider,
  obtenerCarterasLiderPropias,
} from '../../api/dashboardApi';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ThOrdenable } from '../../components/ThOrdenable';
import { useSort } from '../../hooks/useSort';
import { FiltrosPeriodo, calcularRango } from '../../components/pagos/FiltrosPeriodo';
import { TarjetaResumenPagos } from '../../components/pagos/TarjetaResumenPagos';
import { formatearMoneda, formatearFecha, formatearFechaHora } from '../../utils/formatters';
import type { Preset } from '../../components/pagos/FiltrosPeriodo';
import type { PagoDirectorDto } from '../../types/dashboard';

// Vista previa: sin filtro de cartera la tabla solo muestra los primeros N pagos para
// evitar renderizar miles de filas. Los totales siguen reflejando el universo completo.
const PREVIEW_LIMIT = 10;

// ── Fila de tabla ─────────────────────────────────────────────────────────────

const TD: React.CSSProperties = {
  padding: '10px 14px', fontSize: '13px',
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

// ── Página principal ──────────────────────────────────────────────────────────

export function PagosConfirmadosLiderPage() {
  const [carteraID, setCarteraID] = useState<number | null>(null);

  const [preset,      setPreset]      = useState<Preset>('ciclo');
  const [customDesde, setCustomDesde] = useState<string>('');
  const [customHasta, setCustomHasta] = useState<string>('');

  const rango = calcularRango(preset, customDesde, customHasta);

  const { data: carteras = [] } = useQuery({
    queryKey: ['lider-carteras-propias'],
    queryFn: obtenerCarterasLiderPropias,
    staleTime: 10 * 60 * 1000,
  });

  const { data: pagos = [], isLoading, isError } = useQuery({
    queryKey: ['lider-pagos', carteraID, rango.desde, rango.hasta],
    queryFn: () => obtenerPagosLider(carteraID, rango.desde, rango.hasta),
    staleTime: 3 * 60 * 1000,
  });

  const { items: sorted, sort, toggle } = useSort<PagoDirectorDto>(
    pagos,
    { key: 'fechaPago', dir: 'desc' },
    (f, k) => (f as unknown as Record<string, string | number>)[k],
  );

  const totalRecaudo    = pagos.reduce((s, p) => s + p.valorPagado, 0);
  const totalHonorarios = pagos.reduce((s, p) => s + p.honorarios, 0);

  // En modo preview (sin cartera elegida) la tabla se limita a PREVIEW_LIMIT filas.
  const enModoPreview = carteraID === null;
  const filasVisibles = enModoPreview ? sorted.slice(0, PREVIEW_LIMIT) : sorted;

  const selectStyle: React.CSSProperties = {
    padding: '7px 10px', fontSize: '13px', borderRadius: '6px',
    border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
    color: 'var(--z-text-primary)', outline: 'none',
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '1200px' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Pagos confirmados
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Pagos registrados en mis carteras
          </p>
        </div>
      </div>

      {/* Filtros */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '8px', padding: '16px 20px',
        display: 'flex', flexDirection: 'column', gap: '14px',
      }}>
        {/* Cartera */}
        <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '220px' }}>
            <label style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em' }}>
              Cartera
            </label>
            <select value={carteraID ?? ''} onChange={e => setCarteraID(Number(e.target.value) || null)} style={selectStyle}>
              <option value="">Todas mis carteras</option>
              {carteras.map(c => (
                <option key={c.id} value={c.id}>{c.nombreCartera}</option>
              ))}
            </select>
          </div>

          {carteraID !== null && (
            <button
              onClick={() => setCarteraID(null)}
              style={{
                padding: '7px 14px', borderRadius: '6px',
                border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)',
                color: 'var(--z-text-muted)', fontSize: '12px', cursor: 'pointer',
              }}
            >
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
          <TarjetaResumenPagos label="Total pagos"        valor={pagos.length.toLocaleString('es-CO')} />
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

      {/* Aviso de vista previa */}
      {!isLoading && enModoPreview && pagos.length > PREVIEW_LIMIT && (
        <div style={{
          background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
          borderRadius: '8px', padding: '12px 16px',
          fontSize: '13px', color: 'var(--z-text-muted)',
          display: 'flex', alignItems: 'center', gap: '8px', flexWrap: 'wrap',
        }}>
          <span style={{ fontWeight: 600, color: 'var(--z-text-primary)' }}>Vista previa</span>
          Mostrando {PREVIEW_LIMIT} de {pagos.length.toLocaleString('es-CO')} pagos. Filtra por cartera para ver el detalle completo.
        </div>
      )}

      {/* Tabla */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '8px', overflow: 'hidden',
      }}>
        {isLoading ? (
          <CargandoSpiral mensaje="Cargando pagos..." />
        ) : pagos.length === 0 ? (
          <div style={{ padding: '48px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>
            No hay pagos confirmados con los filtros seleccionados.
          </div>
        ) : (
          <div style={{ overflowX: 'auto' }}>
            <table style={{ width: '100%', borderCollapse: 'collapse', minWidth: '800px' }}>
              <thead>
                <tr style={{ background: 'var(--z-bg-page)' }}>
                  <ThOrdenable label="Cliente"       sortKey="nombreCliente" sort={sort} onSort={toggle} />
                  <ThOrdenable label="Asesor"        sortKey="nombreAsesor"  sort={sort} onSort={toggle} />
                  <ThOrdenable label="Cartera"       sortKey="nombreCartera" sort={sort} onSort={toggle} />
                  <ThOrdenable label="Fecha de pago" sortKey="fechaPago"     sort={sort} onSort={toggle} />
                  <ThOrdenable label="Valor pagado"  sortKey="valorPagado"   sort={sort} onSort={toggle} align="right" />
                  <ThOrdenable label="Honorarios"    sortKey="honorarios"    sort={sort} onSort={toggle} align="right" />
                  <ThOrdenable label="Registrado"    sortKey="fechaRegistro" sort={sort} onSort={toggle} />
                </tr>
              </thead>
              <tbody>
                {filasVisibles.map((p, i) => (
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
