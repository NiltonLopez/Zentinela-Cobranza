import { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import {
  obtenerPagosDirector,
  obtenerLideresDropdown,
  obtenerTodasCarterasDirector,
  obtenerVinculosLiderCartera,
} from '../../api/dashboardApi';
import { CargandoSpiral } from '../../components/CargandoSpiral';
import { ZDropdown } from '../../components/ZDropdown';
import { ThOrdenable } from '../../components/ThOrdenable';
import { useSort } from '../../hooks/useSort';
import { FiltrosPeriodo, calcularRango } from '../../components/pagos/FiltrosPeriodo';
import { TarjetaResumenPagos } from '../../components/pagos/TarjetaResumenPagos';
import { formatearMoneda, formatearFecha, formatearFechaHora } from '../../utils/formatters';
import type { Preset } from '../../components/pagos/FiltrosPeriodo';
import type { PagoDirectorDto } from '../../types/dashboard';

// Vista previa: sin filtro de líder ni cartera la tabla solo muestra los primeros N pagos
// para evitar renderizar miles de filas. Los totales siguen reflejando el universo completo.
const PREVIEW_LIMIT = 10;

// ── Fila de tabla ─────────────────────────────────────────────────────────────

const TD: React.CSSProperties = {
  padding: '10px 14px',
  fontSize: '13px',
  color: 'var(--z-text-primary)',
  whiteSpace: 'nowrap',
};

function FilaPago({ pago }: { pago: PagoDirectorDto }) {
  const bg = 'var(--z-bg-card)';
  return (
    <tr style={{ borderBottom: '0.5px solid var(--z-border)' }}>
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

  // Dropdowns dependientes: Líder y Cartera se filtran mutuamente según los vínculos internos.
  const { data: lideres = [] } = useQuery({
    queryKey: ['director-lideres'],
    queryFn: obtenerLideresDropdown,
    staleTime: 10 * 60 * 1000,
  });

  const { data: carteras = [] } = useQuery({
    queryKey: ['director-carteras-todas'],
    queryFn: obtenerTodasCarterasDirector,
    staleTime: 10 * 60 * 1000,
  });

  const { data: vinculos = [] } = useQuery({
    queryKey: ['director-vinculos'],
    queryFn: obtenerVinculosLiderCartera,
    staleTime: 10 * 60 * 1000,
  });

  // Conjunto de pares válidos "liderID-carteraID" para resolver la dependencia entre selectores.
  const vinculoSet = useMemo(
    () => new Set(vinculos.map(v => `${v.liderID}-${v.carteraID}`)),
    [vinculos],
  );

  // Si hay una cartera elegida, el selector de líder solo ofrece los líderes de esa cartera; y viceversa.
  const lideresFiltrados = useMemo(
    () => carteraID == null
      ? lideres
      : lideres.filter(l => vinculoSet.has(`${l.id}-${carteraID}`)),
    [lideres, carteraID, vinculoSet],
  );

  const carterasFiltradas = useMemo(
    () => liderID == null
      ? carteras
      : carteras.filter(c => vinculoSet.has(`${liderID}-${c.id}`)),
    [carteras, liderID, vinculoSet],
  );

  const { data: pagos = [], isLoading, isError } = useQuery({
    queryKey: ['director-pagos', liderID, carteraID, rango.desde, rango.hasta],
    queryFn: () => obtenerPagosDirector(liderID, carteraID, rango.desde, rango.hasta),
    staleTime: 3 * 60 * 1000,
  });

  // Ordenamiento
  const { items: sorted, sort, toggle } = useSort<PagoDirectorDto>(
    pagos,
    { key: 'fechaPago', dir: 'desc' },
    (f, k) => (f as unknown as Record<string, string | number>)[k],
  );

  const totalRecaudo    = pagos.reduce((s, p) => s + p.valorPagado, 0);
  const totalHonorarios = pagos.reduce((s, p) => s + p.honorarios, 0);

  // En modo preview (sin líder ni cartera elegidos) la tabla se limita a PREVIEW_LIMIT filas.
  const enModoPreview = liderID === null && carteraID === null;
  const filasVisibles = enModoPreview ? sorted.slice(0, PREVIEW_LIMIT) : sorted;

  // Selectores dependientes: al elegir un líder, si la cartera actual no le pertenece, se descarta.
  const handleCambiarLider = (val: number | null) => {
    if (val === null) {
      setLiderID(null);
      setLiderNombre('');
      return;
    }
    const lider = lideres.find(l => l.id === val);
    setLiderID(val);
    setLiderNombre(lider?.nombre ?? '');
    if (carteraID != null && !vinculoSet.has(`${val}-${carteraID}`)) {
      setCarteraID(null);
    }
  };

  // Al elegir una cartera, si el líder actual no la atiende, se descarta.
  const handleCambiarCartera = (val: number | null) => {
    setCarteraID(val);
    if (val != null && liderID != null && !vinculoSet.has(`${liderID}-${val}`)) {
      setLiderID(null);
      setLiderNombre('');
    }
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

  const labelStyle: React.CSSProperties = {
    fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)',
    textTransform: 'uppercase', letterSpacing: '0.04em',
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
            Todos los pagos registrados en la empresa
            {liderNombre && (
              <> — <span style={{ fontWeight: 500, color: 'var(--z-text-primary)' }}>{liderNombre}</span></>
            )}
          </p>
        </div>
      </div>

      {/* Filtros */}
      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: '8px', padding: '16px 20px',
        display: 'flex', flexDirection: 'column', gap: '14px',
      }}>
        {/* Fila 1: Líder + Cartera (dependientes bidireccionales) + Limpiar */}
        <div style={{ display: 'flex', gap: '12px', alignItems: 'flex-end', flexWrap: 'wrap' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '200px' }}>
            <label style={labelStyle}>Líder</label>
            <ZDropdown
              options={[{ value: null, label: 'Todos los líderes' }, ...lideresFiltrados.map(l => ({ value: l.id, label: l.nombre }))]}
              value={liderID}
              onChange={handleCambiarLider}
              background="var(--z-bg-page)"
            />
          </div>

          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px', minWidth: '200px' }}>
            <label style={labelStyle}>Cartera</label>
            <ZDropdown
              options={[{ value: null, label: 'Todas las carteras' }, ...carterasFiltradas.map(c => ({ value: c.id, label: c.nombreCartera }))]}
              value={carteraID}
              onChange={handleCambiarCartera}
              background="var(--z-bg-page)"
            />
          </div>

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
          Mostrando {PREVIEW_LIMIT} de {pagos.length.toLocaleString('es-CO')} pagos. Filtra por líder o cartera para ver el detalle completo.
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
                  <ThOrdenable label="Cliente"       sortKey="nombreCliente" sort={sort} onSort={toggle} resaltado />
                  <ThOrdenable label="Asesor"        sortKey="nombreAsesor"  sort={sort} onSort={toggle} resaltado />
                  <ThOrdenable label="Líder"         sortKey="nombreLider"   sort={sort} onSort={toggle} resaltado />
                  <ThOrdenable label="Cartera"       sortKey="nombreCartera" sort={sort} onSort={toggle} resaltado />
                  <ThOrdenable label="Fecha de pago" sortKey="fechaPago"     sort={sort} onSort={toggle} resaltado />
                  <ThOrdenable label="Valor pagado"  sortKey="valorPagado"   sort={sort} onSort={toggle} align="right" resaltado />
                  <ThOrdenable label="Honorarios"    sortKey="honorarios"    sort={sort} onSort={toggle} align="right" resaltado />
                  <ThOrdenable label="Registrado"    sortKey="fechaRegistro" sort={sort} onSort={toggle} resaltado />
                </tr>
              </thead>
              <tbody>
                {filasVisibles.map(p => (
                  <FilaPago key={p.pagoID} pago={p} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

    </div>
  );
}
