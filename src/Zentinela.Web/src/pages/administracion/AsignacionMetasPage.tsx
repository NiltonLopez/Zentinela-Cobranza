import { useState, useMemo } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import {
  obtenerEstadoMetasMesActual,
  guardarMetaLider,
  guardarMetaAgente,
} from '../../api/metasApi';
import type {
  TipoMeta,
  FilaMetaLiderCarteraDto,
  FilaMetaAgenteDto,
  GuardarMetaLiderRequest,
  GuardarMetaAgenteRequest,
} from '../../types/metas';
import { CargandoSpiral } from '../../components/CargandoSpiral';

// ── Helpers ───────────────────────────────────────────────────────────────────

const MESES = [
  'Enero', 'Febrero', 'Marzo', 'Abril', 'Mayo', 'Junio',
  'Julio', 'Agosto', 'Septiembre', 'Octubre', 'Noviembre', 'Diciembre',
];

type TabMeta = 'lider' | 'agente';

// ── Estilos base ──────────────────────────────────────────────────────────────

const TH: React.CSSProperties = {
  padding: '10px 12px', fontSize: 11, fontWeight: 600,
  color: 'var(--z-text-muted)', textTransform: 'uppercase',
  letterSpacing: '0.04em', whiteSpace: 'nowrap', textAlign: 'left',
};
const TD: React.CSSProperties = {
  padding: '8px 12px', fontSize: 13,
  borderBottom: '1px solid var(--z-border)', whiteSpace: 'nowrap',
};

// ── Toggle Recaudo / Honorarios ───────────────────────────────────────────────

function ToggleTipo({ value, onChange }: { value: TipoMeta; onChange: (v: TipoMeta) => void }) {
  return (
    <div style={{ display: 'flex', borderRadius: 6, overflow: 'hidden', border: '1px solid var(--z-border)', width: 'fit-content' }}>
      {([0, 1] as TipoMeta[]).map(t => {
        const active = value === t;
        return (
          <button
            key={t}
            onClick={() => onChange(t)}
            style={{
              padding: '4px 10px', fontSize: 11, fontWeight: active ? 600 : 400,
              background: active ? (t === 0 ? '#2563eb' : '#7c3aed') : 'transparent',
              color: active ? '#fff' : 'var(--z-text-muted)',
              border: 'none', cursor: 'pointer', whiteSpace: 'nowrap',
            }}
          >
            {t === 0 ? 'Recaudo' : 'Honorarios'}
          </button>
        );
      })}
    </div>
  );
}

// ── Input numérico en tabla ───────────────────────────────────────────────────

function InputMeta({ value, onChange }: { value: string; onChange: (v: string) => void }) {
  const display = value === '' ? '' : Number(value).toLocaleString('es-CO');

  const handleChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const soloDigitos = e.target.value.replace(/\D/g, '');
    if (soloDigitos === '') { onChange(''); return; }
    const n = parseInt(soloDigitos, 10);
    onChange(isNaN(n) ? '' : String(n));
  };

  return (
    <input
      type="text"
      inputMode="numeric"
      value={display}
      onChange={handleChange}
      placeholder="0"
      style={{
        width: 130, padding: '5px 8px', borderRadius: 6,
        border: '1px solid var(--z-border)',
        background: 'var(--z-bg-page)', color: 'var(--z-text-primary)',
        fontSize: 13, outline: 'none',
      }}
    />
  );
}

// ── Badge "Sin meta" / tipo de meta ──────────────────────────────────────────

function BadgeMeta({ tieneMeta, tipo }: { tieneMeta: boolean; tipo: TipoMeta | null }) {
  if (!tieneMeta) {
    return (
      <span style={{
        display: 'inline-block', padding: '2px 8px', borderRadius: 99,
        fontSize: 11, fontWeight: 600,
        background: '#fff7ed', color: '#ea580c', border: '1px solid #fed7aa',
      }}>
        Sin meta
      </span>
    );
  }
  const esHon = tipo === 1;
  return (
    <span style={{
      display: 'inline-block', padding: '2px 8px', borderRadius: 99,
      fontSize: 11, fontWeight: 600,
      background: esHon ? '#f5f3ff' : '#eff6ff',
      color:      esHon ? '#7c3aed' : '#2563eb',
      border:     `1px solid ${esHon ? '#c4b5fd' : '#93c5fd'}`,
    }}>
      {esHon ? 'Honorarios' : 'Recaudo'}
    </span>
  );
}

// ── Fila de Líder-Cartera ─────────────────────────────────────────────────────

function FilaLider({
  fila, año, mes, onGuardado,
}: {
  fila: FilaMetaLiderCarteraDto;
  año: number; mes: number;
  onGuardado: () => void;
}) {
  const [tipo,    setTipo]    = useState<TipoMeta>(fila.tipoMeta ?? 0);
  const [valor,   setValor]   = useState(fila.valorMeta?.toString() ?? '');
  const [guardado, setGuardado] = useState(false);

  const { mutate, isPending } = useMutation({
    mutationFn: (req: GuardarMetaLiderRequest) => guardarMetaLider(req),
    onSuccess: () => { setGuardado(true); onGuardado(); setTimeout(() => setGuardado(false), 2500); },
  });

  const modificado   = tipo !== (fila.tipoMeta ?? 0) || valor !== (fila.valorMeta?.toString() ?? '');
  const puedeGuardar = Number(valor) > 0;
  const sinMeta      = !fila.tieneMeta;

  return (
    <tr style={sinMeta ? { background: '#fff7ed' } : {}}>
      <td style={{ ...TD, fontWeight: 500, color: 'var(--z-text-primary)' }}>{fila.nombreCartera}</td>
      <td style={{ ...TD, color: 'var(--z-text-muted)' }}>{fila.nombreLider}</td>
      <td style={{ ...TD, textAlign: 'center' }}>{fila.capacidadActual}</td>
      <td style={{ ...TD, textAlign: 'center' }}>{fila.cantidadAsignaciones.toLocaleString('es-CO')}</td>
      <td style={TD}>
        <ToggleTipo value={tipo} onChange={t => { setTipo(t); setValor(''); }} />
      </td>
      <td style={TD}>
        <InputMeta value={valor} onChange={setValor} />
      </td>
      <td style={{ ...TD, textAlign: 'center' }}>
        <BadgeMeta tieneMeta={(fila.tieneMeta && !modificado) || guardado} tipo={guardado ? tipo : fila.tipoMeta} />
      </td>
      <td style={TD}>
        <button
          onClick={() => mutate({
            liderID: fila.liderID, carteraID: fila.carteraID,
            año: año as GuardarMetaLiderRequest['año'],
            mes: mes as GuardarMetaLiderRequest['mes'],
            tipoMeta: tipo,
            valor: Number(valor),
          })}
          disabled={isPending || !puedeGuardar}
          style={{
            padding: '5px 14px', borderRadius: 6, border: 'none',
            cursor: puedeGuardar ? 'pointer' : 'not-allowed',
            background: guardado ? '#16a34a' : puedeGuardar ? 'var(--z-brand)' : 'var(--z-border)',
            color: puedeGuardar || guardado ? '#fff' : 'var(--z-text-muted)',
            fontSize: 12, fontWeight: 600, transition: 'background 0.2s',
          }}
        >
          {isPending ? '…' : guardado ? 'Guardado' : 'Guardar'}
        </button>
      </td>
    </tr>
  );
}

// ── Fila de Agente ────────────────────────────────────────────────────────────

function FilaAgente({
  fila, año, mes, onGuardado,
}: {
  fila: FilaMetaAgenteDto;
  año: number; mes: number;
  onGuardado: () => void;
}) {
  const [tipo,     setTipo]     = useState<TipoMeta>(fila.tipoMeta ?? 0);
  const [valor,    setValor]    = useState(fila.valorMeta?.toString() ?? '');
  const [guardado, setGuardado] = useState(false);

  const { mutate, isPending } = useMutation({
    mutationFn: (req: GuardarMetaAgenteRequest) => guardarMetaAgente(req),
    onSuccess: () => { setGuardado(true); onGuardado(); setTimeout(() => setGuardado(false), 2500); },
  });

  const modificado   = tipo !== (fila.tipoMeta ?? 0) || valor !== (fila.valorMeta?.toString() ?? '');
  const puedeGuardar = Number(valor) > 0;
  const sinMeta      = !fila.tieneMeta;

  return (
    <tr style={sinMeta ? { background: '#fff7ed' } : {}}>
      <td style={{ ...TD, fontWeight: 500, color: 'var(--z-text-primary)' }}>{fila.nombreAgente}</td>
      <td style={{ ...TD, color: 'var(--z-text-muted)' }}>{fila.nombreCartera ?? '—'}</td>
      <td style={{ ...TD, color: 'var(--z-text-muted)' }}>{fila.nombreLider ?? '—'}</td>
      <td style={TD}>
        <ToggleTipo value={tipo} onChange={t => { setTipo(t); setValor(''); }} />
      </td>
      <td style={TD}>
        <InputMeta value={valor} onChange={setValor} />
      </td>
      <td style={{ ...TD, textAlign: 'center' }}>
        <BadgeMeta tieneMeta={(fila.tieneMeta && !modificado) || guardado} tipo={guardado ? tipo : fila.tipoMeta} />
      </td>
      <td style={TD}>
        <button
          onClick={() => mutate({
            agenteID: fila.agenteID,
            año: año as GuardarMetaAgenteRequest['año'],
            mes: mes as GuardarMetaAgenteRequest['mes'],
            tipoMeta: tipo,
            valor: Number(valor),
          })}
          disabled={isPending || !puedeGuardar}
          style={{
            padding: '5px 14px', borderRadius: 6, border: 'none',
            cursor: puedeGuardar ? 'pointer' : 'not-allowed',
            background: guardado ? '#16a34a' : puedeGuardar ? 'var(--z-brand)' : 'var(--z-border)',
            color: puedeGuardar || guardado ? '#fff' : 'var(--z-text-muted)',
            fontSize: 12, fontWeight: 600, transition: 'background 0.2s',
          }}
        >
          {isPending ? '…' : guardado ? 'Guardado' : 'Guardar'}
        </button>
      </td>
    </tr>
  );
}

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  PÁGINA PRINCIPAL                                                            ║
// ╚══════════════════════════════════════════════════════════════════════════════╝

export function AsignacionMetasPage() {
  const qc = useQueryClient();
  const [tab,         setTab]        = useState<TabMeta>('lider');
  const [soloSinMeta, setSoloSinMeta] = useState(false);
  const [busqueda,    setBusqueda]   = useState('');

  const { data, isLoading, isError } = useQuery({
    queryKey: ['metas-mes-actual'],
    queryFn:  obtenerEstadoMetasMesActual,
  });

  const refrescar = () => qc.invalidateQueries({ queryKey: ['metas-mes-actual'] });

  const labelMes = data ? `${MESES[data.mes - 1]} ${data.año}` : '…';

  // ── Filtros ───────────────────────────────────────────────────────────────

  const filasFiltradas = useMemo(() => {
    if (!data) return { lider: [], agente: [] };
    const termino = busqueda.trim().toLowerCase();

    const lider = data.liderCarteras.filter(f => {
      if (soloSinMeta && f.tieneMeta) return false;
      if (termino && !f.nombreCartera.toLowerCase().includes(termino) &&
                     !f.nombreLider.toLowerCase().includes(termino)) return false;
      return true;
    });

    const agente = data.agentes.filter(f => {
      if (soloSinMeta && f.tieneMeta) return false;
      if (termino && !f.nombreAgente.toLowerCase().includes(termino)) return false;
      return true;
    });

    return { lider, agente };
  }, [data, soloSinMeta, busqueda]);

  // ── KPIs ──────────────────────────────────────────────────────────────────

  const kpiLider  = data ? {
    conMeta: data.liderCarteras.filter(f => f.tieneMeta).length,
    sinMeta: data.liderCarteras.filter(f => !f.tieneMeta).length,
  } : null;

  const kpiAgente = data ? {
    conMeta: data.agentes.filter(f => f.tieneMeta).length,
    sinMeta: data.agentes.filter(f => !f.tieneMeta).length,
  } : null;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20, maxWidth: 1200 }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Asignación de Metas
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Gestión de metas de recaudo y honorarios para el ciclo actual.
          </p>
        </div>
        <div style={{
          display: 'flex', alignItems: 'center', gap: 8,
          background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
          borderRadius: 8, padding: '8px 16px',
        }}>
          <span style={{ fontSize: 11, color: 'var(--z-text-muted)', fontWeight: 600, textTransform: 'uppercase' }}>
            Ciclo activo
          </span>
          <span style={{ fontSize: 14, fontWeight: 600, color: 'var(--z-brand)' }}>{labelMes}</span>
        </div>
      </div>

      {isLoading && <CargandoSpiral mensaje="Cargando metas del mes…" />}
      {isError   && (
        <div style={{ padding: 24, textAlign: 'center', color: '#dc2626', fontSize: 13 }}>
          Error al cargar los datos. Verifica la conexión e inténtalo de nuevo.
        </div>
      )}

      {data && (
        <>
          {/* Tabs */}
          <div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--z-border)' }}>
            {([['lider', 'Líderes-Carteras'], ['agente', 'Agentes']] as [TabMeta, string][]).map(([id, label]) => (
              <button
                key={id}
                onClick={() => { setTab(id); setBusqueda(''); }}
                style={{
                  padding: '9px 20px', fontSize: 13,
                  fontWeight: tab === id ? 600 : 400,
                  color: tab === id ? 'var(--z-brand)' : 'var(--z-text-muted)',
                  background: 'none', border: 'none',
                  borderBottom: `2px solid ${tab === id ? 'var(--z-brand)' : 'transparent'}`,
                  cursor: 'pointer', marginBottom: -1, transition: 'color 0.15s',
                }}
              >
                {label}
              </button>
            ))}
          </div>

          {/* KPIs + filtros */}
          <div style={{ display: 'flex', alignItems: 'center', gap: 12, flexWrap: 'wrap' }}>
            <div style={{
              display: 'flex', alignItems: 'center', gap: 8,
              background: '#f0fdf4', border: '1px solid #86efac',
              borderRadius: 8, padding: '10px 16px',
            }}>
              <span style={{ fontSize: 20, fontWeight: 700, color: '#16a34a' }}>
                {tab === 'lider' ? kpiLider?.conMeta : kpiAgente?.conMeta}
              </span>
              <span style={{ fontSize: 12, color: '#15803d' }}>con meta</span>
            </div>
            <div style={{
              display: 'flex', alignItems: 'center', gap: 8,
              background: '#fff7ed', border: '1px solid #fed7aa',
              borderRadius: 8, padding: '10px 16px',
            }}>
              <span style={{ fontSize: 20, fontWeight: 700, color: '#ea580c' }}>
                {tab === 'lider' ? kpiLider?.sinMeta : kpiAgente?.sinMeta}
              </span>
              <span style={{ fontSize: 12, color: '#c2410c' }}>sin meta</span>
            </div>

            <button
              onClick={() => setSoloSinMeta(v => !v)}
              style={{
                padding: '8px 14px', borderRadius: 6, border: '1px solid var(--z-border)',
                background: soloSinMeta ? '#fff7ed' : 'var(--z-bg-card)',
                color: soloSinMeta ? '#ea580c' : 'var(--z-text-muted)',
                fontSize: 12, fontWeight: soloSinMeta ? 600 : 400, cursor: 'pointer',
              }}
            >
              {soloSinMeta ? '▪ Solo sin meta' : 'Solo sin meta'}
            </button>

            <input
              type="text"
              placeholder={tab === 'lider' ? 'Buscar cartera o líder…' : 'Buscar agente…'}
              value={busqueda}
              onChange={e => setBusqueda(e.target.value)}
              style={{
                padding: '7px 12px', borderRadius: 6, border: '1px solid var(--z-border)',
                background: 'var(--z-bg-card)', color: 'var(--z-text-primary)',
                fontSize: 13, outline: 'none', minWidth: 200,
              }}
            />
          </div>

          {/* Tabla Líderes-Carteras */}
          {tab === 'lider' && (
            <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
              <div style={{ overflowX: 'auto' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                  <thead>
                    <tr style={{ background: 'var(--z-bg-page)', borderBottom: '1px solid var(--z-border)' }}>
                      <th style={TH}>Cartera</th>
                      <th style={TH}>Líder</th>
                      <th style={{ ...TH, textAlign: 'center' }}>Asesores</th>
                      <th style={{ ...TH, textAlign: 'center' }}>Cuentas asignadas</th>
                      <th style={TH}>Tipo de meta</th>
                      <th style={TH}>Valor meta</th>
                      <th style={{ ...TH, textAlign: 'center' }}>Estado</th>
                      <th style={TH}></th>
                    </tr>
                  </thead>
                  <tbody>
                    {filasFiltradas.lider.length === 0 ? (
                      <tr>
                        <td colSpan={8} style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)', padding: '32px 0' }}>
                          {soloSinMeta ? 'Todas las combinaciones tienen meta para este mes.' : 'No hay resultados.'}
                        </td>
                      </tr>
                    ) : filasFiltradas.lider.map(f => (
                      <FilaLider
                        key={`${f.liderID}-${f.carteraID}-${f.tipoMeta ?? 'x'}-${f.valorMeta ?? 'x'}`}
                        fila={f} año={data.año} mes={data.mes} onGuardado={refrescar}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          {/* Tabla Agentes */}
          {tab === 'agente' && (
            <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, overflow: 'hidden' }}>
              <div style={{ overflowX: 'auto' }}>
                <table style={{ width: '100%', borderCollapse: 'collapse' }}>
                  <thead>
                    <tr style={{ background: 'var(--z-bg-page)', borderBottom: '1px solid var(--z-border)' }}>
                      <th style={TH}>Agente</th>
                      <th style={TH}>Cartera</th>
                      <th style={TH}>Líder</th>
                      <th style={TH}>Tipo de meta</th>
                      <th style={TH}>Valor meta</th>
                      <th style={{ ...TH, textAlign: 'center' }}>Estado</th>
                      <th style={TH}></th>
                    </tr>
                  </thead>
                  <tbody>
                    {filasFiltradas.agente.length === 0 ? (
                      <tr>
                        <td colSpan={7} style={{ ...TD, textAlign: 'center', color: 'var(--z-text-muted)', padding: '32px 0' }}>
                          {soloSinMeta ? 'Todos los agentes tienen meta para este mes.' : 'No hay resultados.'}
                        </td>
                      </tr>
                    ) : filasFiltradas.agente.map(f => (
                      <FilaAgente
                        key={`${f.agenteID}-${f.tipoMeta ?? 'x'}-${f.valorMeta ?? 'x'}`}
                        fila={f} año={data.año} mes={data.mes} onGuardado={refrescar}
                      />
                    ))}
                  </tbody>
                </table>
              </div>
            </div>
          )}

          <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>
            Las metas solo pueden establecerse para el ciclo activo (<strong>{labelMes}</strong>).
            Los campos con fondo naranja aún no tienen meta asignada para este mes.
          </p>
        </>
      )}
    </div>
  );
}
