import { useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { obtenerPeriodosDisponiblesPagos, contarPeriodoPagos, borrarPeriodoPagos } from '../../../api/pagosApi';
import { formatearMoneda } from '../../../utils/formatters';
import { ZDropdown } from '../../../components/ZDropdown';
import { CargandoSpiral } from '../../../components/CargandoSpiral';

// ─────────────────────────────────────────────────────────────────────────────
// Borrar un período mensual de pagos (una cartera o todas). Operación
// irreversible sobre datos financieros reales — por eso el flujo es de dos
// pasos obligatorios: 1) consultar cuántos pagos hay y por cuánto dinero,
// 2) confirmar escribiendo "BORRAR" en un modal antes de ejecutar el borrado.
// Restringido a Admin en el backend (más estricto que el resto del módulo).
//
// Los 3 selects (Mes / Año / Cartera) son dinámicos y mutuamente dependientes:
// solo muestran combinaciones que realmente tienen pagos cargados (según
// GET /api/pagos/periodos-disponibles), y cada uno se filtra por lo que el
// usuario ya eligió en los otros dos — igual que los selectores dependientes
// de líder/cartera en Dashboard.
// ─────────────────────────────────────────────────────────────────────────────

const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

const labelStyle: React.CSSProperties = {
  display: 'block', fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)',
  textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: 6,
};
const inputStyle: React.CSSProperties = {
  padding: '8px 12px', borderRadius: 6, border: '1px solid var(--z-border)',
  background: 'var(--z-bg-card)', color: 'var(--z-text-primary)', fontSize: 14, cursor: 'text',
};

function ConfirmarBorradoModal({ carteraNombre, mesLabel, anio, cantidad, valorPagadoTotal, honorariosTotal, borrando, onConfirmar, onCancelar }: {
  carteraNombre: string; mesLabel: string; anio: number;
  cantidad: number; valorPagadoTotal: number; honorariosTotal: number;
  borrando: boolean; onConfirmar: () => void; onCancelar: () => void;
}) {
  const [texto, setTexto] = useState('');
  const habilitado = texto.trim() === 'BORRAR' && !borrando;

  return (
    <div
      onClick={onCancelar}
      style={{
        position: 'fixed', inset: 0, zIndex: 300,
        background: 'rgba(15,23,42,0.55)', backdropFilter: 'blur(2px)',
        display: 'flex', alignItems: 'center', justifyContent: 'center', padding: 16,
      }}
    >
      <div
        onClick={e => e.stopPropagation()}
        style={{
          width: 'min(440px, 100%)', background: 'var(--z-bg-card)', borderRadius: 14,
          border: '1px solid var(--z-border)', boxShadow: '0 24px 60px rgba(0,0,0,0.25)',
          padding: '26px 26px 22px',
        }}
      >
        <h2 style={{ fontSize: 16, fontWeight: 700, color: '#dc2626', margin: 0 }}>
          ⚠ Borrar período de pagos
        </h2>
        <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '8px 0 16px', lineHeight: 1.5 }}>
          Esta acción es <strong>irreversible</strong>. Vas a borrar permanentemente:
        </p>

        <div style={{ background: '#fef2f2', border: '0.5px solid #fca5a5', borderRadius: 8, padding: '12px 14px', marginBottom: 16 }}>
          <p style={{ fontSize: 13, margin: '0 0 4px', color: 'var(--z-text-primary)' }}>
            <strong>{cantidad.toLocaleString('es-CO')}</strong> pago{cantidad !== 1 ? 's' : ''} de <strong>{mesLabel} {anio}</strong> — {carteraNombre}
          </p>
          <p style={{ fontSize: 12, margin: 0, color: 'var(--z-text-muted)' }}>
            Valor pagado: <strong>{formatearMoneda(valorPagadoTotal)}</strong> · Honorarios: <strong>{formatearMoneda(honorariosTotal)}</strong>
          </p>
        </div>

        <label style={labelStyle}>Escribe BORRAR para confirmar</label>
        <input
          autoFocus
          value={texto}
          onChange={e => setTexto(e.target.value)}
          placeholder="BORRAR"
          style={{ ...inputStyle, width: '100%' }}
        />

        <div style={{ display: 'flex', gap: 10, marginTop: 20 }}>
          <button
            onClick={onCancelar}
            style={{ flex: 1, padding: 11, borderRadius: 8, border: '0.5px solid var(--z-border)', background: 'transparent', color: 'var(--z-text-muted)', fontSize: 13, fontWeight: 600, cursor: 'pointer' }}
          >
            Cancelar
          </button>
          <button
            onClick={onConfirmar}
            disabled={!habilitado}
            style={{
              flex: 2, padding: 11, borderRadius: 8, border: 'none',
              background: habilitado ? '#dc2626' : 'var(--z-border)',
              color: habilitado ? '#fff' : 'var(--z-text-muted)',
              fontSize: 13, fontWeight: 700, cursor: habilitado ? 'pointer' : 'not-allowed',
            }}
          >
            {borrando ? 'Borrando…' : 'Sí, borrar permanentemente'}
          </button>
        </div>
      </div>
    </div>
  );
}

export function BorrarPeriodoPagos() {
  const [mes, setMes]             = useState<number | ''>('');
  const [anio, setAnio]           = useState<number | ''>('');
  const [carteraID, setCarteraID] = useState<number | ''>(''); // '' = todas las carteras

  const [conteo, setConteo] = useState<{ cantidad: number; valorPagadoTotal: number; honorariosTotal: number } | null>(null);
  const [consultando, setConsultando] = useState(false);
  const [borrando, setBorrando] = useState(false);
  const [modalAbierto, setModalAbierto] = useState(false);
  const [error, setError] = useState('');
  const [resultado, setResultado] = useState('');

  const { data: periodos = [], isLoading: cargandoPeriodos } = useQuery({
    queryKey: ['pagos-periodos-disponibles'],
    queryFn: obtenerPeriodosDisponiblesPagos,
  });

  // Cada select se filtra por lo que ya está elegido en los OTROS dos — así, p. ej.,
  // elegir "Bertal" en Cartera acota Mes/Año a los períodos que Bertal realmente tiene
  // cargados, y elegir un Mes/Año acota Cartera a quiénes tienen datos en ese período.
  const opcionesCartera = useMemo(() => {
    const filtrados = periodos.filter(p => (mes === '' || p.mes === mes) && (anio === '' || p.anio === anio));
    const únicas = new Map<number, string>();
    filtrados.forEach(p => únicas.set(p.carteraID, p.nombreCartera));
    return Array.from(únicas.entries())
      .map(([id, nombre]) => ({ id, nombre }))
      .sort((a, b) => a.nombre.localeCompare(b.nombre, 'es'));
  }, [periodos, mes, anio]);

  const opcionesMes = useMemo(() => {
    const filtrados = periodos.filter(p => (carteraID === '' || p.carteraID === carteraID) && (anio === '' || p.anio === anio));
    return Array.from(new Set(filtrados.map(p => p.mes))).sort((a, b) => a - b);
  }, [periodos, carteraID, anio]);

  const opcionesAnio = useMemo(() => {
    const filtrados = periodos.filter(p => (carteraID === '' || p.carteraID === carteraID) && (mes === '' || p.mes === mes));
    return Array.from(new Set(filtrados.map(p => p.anio))).sort((a, b) => b - a);
  }, [periodos, carteraID, mes]);

  // Si una selección deja de tener datos disponibles tras cambiar otro filtro, se limpia
  // en vez de quedar "atascada" mostrando un valor que ya no aplica.
  useEffect(() => { if (mes !== '' && !opcionesMes.includes(mes)) setMes(''); }, [opcionesMes, mes]);
  useEffect(() => { if (anio !== '' && !opcionesAnio.includes(anio)) setAnio(''); }, [opcionesAnio, anio]);
  useEffect(() => { if (carteraID !== '' && !opcionesCartera.some(o => o.id === carteraID)) setCarteraID(''); }, [opcionesCartera, carteraID]);

  // Cualquier cambio de filtro invalida la vista previa ya consultada, para no
  // borrar por accidente un período distinto al que se muestra en pantalla.
  useEffect(() => { setConteo(null); setResultado(''); }, [mes, anio, carteraID]);

  const nombreCartera = carteraID === '' ? 'Todas las carteras' : (opcionesCartera.find(c => c.id === carteraID)?.nombre ?? '—');
  const puedeConsultar = mes !== '' && anio !== '';

  const consultar = async () => {
    if (!puedeConsultar) return;
    setConsultando(true); setError(''); setResultado('');
    try {
      const res = await contarPeriodoPagos(anio as number, mes as number, carteraID === '' ? null : carteraID);
      setConteo(res);
    } catch {
      setError('No se pudo consultar el período. Intenta de nuevo.');
    } finally {
      setConsultando(false);
    }
  };

  const confirmarBorrado = async () => {
    if (!puedeConsultar) return;
    setBorrando(true); setError('');
    try {
      const res = await borrarPeriodoPagos(anio as number, mes as number, carteraID === '' ? null : carteraID);
      setModalAbierto(false);
      setConteo(null);
      setResultado(res.mensaje);
    } catch {
      setError('No se pudo completar el borrado. Intenta de nuevo.');
    } finally {
      setBorrando(false);
    }
  };

  if (cargandoPeriodos) return <CargandoSpiral mensaje="Cargando períodos disponibles..." />;

  if (periodos.length === 0) {
    return (
      <div style={{ background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)', borderRadius: 10, padding: '32px 24px', textAlign: 'center', color: 'var(--z-text-muted)', fontSize: 13 }}>
        Aún no hay pagos cargados en el sistema — no hay períodos que borrar.
      </div>
    );
  }

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>
      <div style={{
        background: '#fffbeb', border: '0.5px solid #fde68a', borderRadius: 8,
        padding: '10px 14px', fontSize: 12, color: '#92400e',
      }}>
        <strong>Cuidado:</strong> esto borra pagos ya confirmados de forma permanente. Úsalo solo para corregir un período mal cargado.
      </div>

      <div style={{
        background: 'var(--z-bg-card)', border: '0.5px solid var(--z-border)',
        borderRadius: 10, padding: 20, display: 'flex', flexDirection: 'column', gap: 16,
      }}>
        <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
          <div>
            <label style={labelStyle}>Mes a borrar</label>
            <ZDropdown
              options={[
                { value: '' as number | '', label: '— Selecciona un mes —' },
                ...opcionesMes.map(m => ({ value: m as number | '', label: MESES[m - 1] })),
              ]}
              value={mes}
              onChange={setMes}
              background="var(--z-bg-page)"
              style={{ minWidth: 180 }}
            />
          </div>
          <div>
            <label style={labelStyle}>Año</label>
            <ZDropdown
              options={[
                { value: '' as number | '', label: '— Selecciona un año —' },
                ...opcionesAnio.map(a => ({ value: a as number | '', label: String(a) })),
              ]}
              value={anio}
              onChange={setAnio}
              background="var(--z-bg-page)"
              style={{ minWidth: 150 }}
            />
          </div>
          <div>
            <label style={labelStyle}>Cartera</label>
            <ZDropdown
              options={[
                { value: '' as number | '', label: 'Todas las carteras' },
                ...opcionesCartera.map(c => ({ value: c.id as number | '', label: c.nombre })),
              ]}
              value={carteraID}
              onChange={setCarteraID}
              background="var(--z-bg-page)"
              style={{ minWidth: 220 }}
            />
          </div>
        </div>

        <button
          onClick={consultar}
          disabled={consultando || !puedeConsultar}
          title={puedeConsultar ? undefined : 'Selecciona mes y año primero'}
          style={{
            padding: '9px 20px', borderRadius: 6, border: 'none', width: 'fit-content',
            background: (consultando || !puedeConsultar) ? 'var(--z-border)' : 'var(--z-brand)',
            color: (consultando || !puedeConsultar) ? 'var(--z-text-muted)' : '#fff',
            fontSize: 13, fontWeight: 600,
            cursor: (consultando || !puedeConsultar) ? 'not-allowed' : 'pointer',
          }}
        >
          {consultando ? 'Consultando…' : 'Consultar período'}
        </button>

        {error && (
          <div style={{ background: '#fef2f2', border: '0.5px solid #fca5a5', borderRadius: 6, padding: '10px 14px', fontSize: 13, color: '#dc2626' }}>
            {error}
          </div>
        )}
        {resultado && (
          <div style={{ background: '#f0fdf4', border: '0.5px solid #86efac', borderRadius: 6, padding: '10px 14px', fontSize: 13, color: '#15803d' }}>
            {resultado}
          </div>
        )}

        {conteo && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 14 }}>
            <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
              <div style={{ flex: '1 1 150px', background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)', borderRadius: 8, padding: '12px 16px' }}>
                <p style={{ ...labelStyle, marginBottom: 4 }}>Pagos encontrados</p>
                <p style={{ fontSize: 20, fontWeight: 700, color: 'var(--z-text-primary)', margin: 0 }}>{conteo.cantidad.toLocaleString('es-CO')}</p>
              </div>
              <div style={{ flex: '1 1 150px', background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)', borderRadius: 8, padding: '12px 16px' }}>
                <p style={{ ...labelStyle, marginBottom: 4 }}>Valor pagado</p>
                <p style={{ fontSize: 18, fontWeight: 700, color: 'var(--z-text-primary)', margin: 0 }}>{formatearMoneda(conteo.valorPagadoTotal)}</p>
              </div>
              <div style={{ flex: '1 1 150px', background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)', borderRadius: 8, padding: '12px 16px' }}>
                <p style={{ ...labelStyle, marginBottom: 4 }}>Honorarios</p>
                <p style={{ fontSize: 18, fontWeight: 700, color: 'var(--z-text-primary)', margin: 0 }}>{formatearMoneda(conteo.honorariosTotal)}</p>
              </div>
            </div>

            {conteo.cantidad === 0 ? (
              <p style={{ fontSize: 13, color: 'var(--z-text-muted)' }}>No hay pagos en este período — nada que borrar.</p>
            ) : (
              <button
                onClick={() => setModalAbierto(true)}
                style={{
                  padding: '10px 20px', borderRadius: 6, border: 'none', width: 'fit-content',
                  background: '#dc2626', color: '#fff', fontSize: 13, fontWeight: 700, cursor: 'pointer',
                }}
              >
                Borrar período…
              </button>
            )}
          </div>
        )}
      </div>

      {modalAbierto && conteo && mes !== '' && anio !== '' && (
        <ConfirmarBorradoModal
          carteraNombre={nombreCartera}
          mesLabel={MESES[mes - 1]}
          anio={anio}
          cantidad={conteo.cantidad}
          valorPagadoTotal={conteo.valorPagadoTotal}
          honorariosTotal={conteo.honorariosTotal}
          borrando={borrando}
          onConfirmar={confirmarBorrado}
          onCancelar={() => setModalAbierto(false)}
        />
      )}
    </div>
  );
}
