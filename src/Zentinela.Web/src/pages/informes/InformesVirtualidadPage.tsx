import { useState, useRef } from 'react';
import { useMutation, useQuery } from '@tanstack/react-query';
import {
  BarChart, Bar, XAxis, YAxis, Tooltip, ResponsiveContainer,
  Cell, Legend, CartesianGrid,
} from 'recharts';
import { obtenerOpcionesCalificador } from '../../api/calificadorApi';
import { useAuth } from '../../context/AuthContext';
import { ZDropdown } from '../../components/ZDropdown';
import {
  getInformeMensajeria,
  getInformeIVR, getAnalisisIvr,
  validarLlamadasPerdidas, confirmarLlamadasPerdidas, getInformeLlamadasPerdidas,
  validarLlamadasEntrantes, confirmarLlamadasEntrantes, getInformeEntrantes,
  getInformePagosImpacto,
} from '../../api/virtualidadApi';
import { AnalisisIvrPanel } from './virtualidad/AnalisisIvrPanel';
import type {
  ValidarLlamadasResponse, FilaLlamada,
  ValidarEntrantesResponse,
} from '../../types/virtualidad';

// ── Paleta ────────────────────────────────────────────────────────────────────
const C = {
  brand:      '#F54927',   // Naranja principal Zentinela
  brandLight: '#FEF0EC',
  success:    '#16a34a',
  warning:    '#d97706',
  danger:     '#dc2626',
  gray:       '#64748b',
  mensajes:   '#7c3aed',
  ivr:        '#0891b2',
  llamadas:   '#059669',
  entrantes:  '#ea580c',
  impacto:    '#F54927',
};

type Tab = 'mensajeria' | 'ivr' | 'llamadas-perdidas' | 'llamadas-entrantes' | 'pagos-impacto';

const AÑO_HOY = new Date().getFullYear();

// ── Alcance por rol ───────────────────────────────────────────────────────────
/**
 * Un Líder ve el informe tal cual el administrador, pero solo con sus datos: el
 * selector de líder no se muestra y el filtro queda fijado a su propio liderID.
 * Esto es presentación — la restricción real la impone el servidor, que para el rol
 * Líder ignora el liderID de la URL y usa el del token (AlcanceLiderExtensions).
 */
function useAlcanceLider() {
  const { usuario } = useAuth();
  const esLider = usuario?.rol === 'Líder';
  return { esLider, liderPropio: esLider ? (usuario?.liderID ?? 0) : 0 };
}

// ── Utilidades de fecha ───────────────────────────────────────────────────────
type Preset = 'hoy' | 'ciclo' | 'mes-pasado' | '7d' | '30d' | 'custom';

const PRESETS_PERIODO: { valor: Preset; label: string }[] = [
  { valor: 'hoy',        label: 'Hoy'            },
  { valor: 'ciclo',      label: 'Este mes'        },
  { valor: 'mes-pasado', label: 'Mes pasado'      },
  { valor: '7d',         label: 'Últimos 7 días'  },
  { valor: '30d',        label: 'Últimos 30 días' },
  { valor: 'custom',     label: 'Personalizado'   },
];

function isoFecha(d: Date) { return d.toISOString().slice(0, 10); }
function isoHoy()           { return isoFecha(new Date()); }
function formatoDDMMYYYY(iso: string) {
  if (!iso) return '';
  const [y, m, d] = iso.split('-');
  return `${d}/${m}/${y}`;
}

const HOY    = isoHoy();
const HACE30 = (() => { const d = new Date(); d.setDate(d.getDate() - 29); return isoFecha(d); })();

function calcularRangoPeriodo(preset: Preset, cd: string, ch: string): { desde: string; hasta: string } {
  const hoy = new Date();
  switch (preset) {
    case 'hoy':        return { desde: isoHoy(), hasta: isoHoy() };
    case 'ciclo':      return { desde: isoFecha(new Date(hoy.getFullYear(), hoy.getMonth(), 1)), hasta: isoHoy() };
    case 'mes-pasado': return {
      desde: isoFecha(new Date(hoy.getFullYear(), hoy.getMonth() - 1, 1)),
      hasta: isoFecha(new Date(hoy.getFullYear(), hoy.getMonth(), 0)),
    };
    case '7d':  { const d = new Date(hoy); d.setDate(hoy.getDate() - 6);  return { desde: isoFecha(d), hasta: isoHoy() }; }
    case '30d': { const d = new Date(hoy); d.setDate(hoy.getDate() - 29); return { desde: isoFecha(d), hasta: isoHoy() }; }
    case 'custom': return { desde: cd || isoHoy(), hasta: ch || isoHoy() };
  }
}

// ── Componentes base ──────────────────────────────────────────────────────────

function SelectorPeriodo({ onChange, color = C.brand }: { onChange: (desde: string, hasta: string) => void; color?: string }) {
  const [preset,      setPreset]      = useState<Preset>('30d');
  const [customDesde, setCustomDesde] = useState('');
  const [customHasta, setCustomHasta] = useState('');

  const chip = (activo: boolean): React.CSSProperties => ({
    padding: '5px 14px',
    borderRadius: 20,
    border: activo ? `1.5px solid ${color}` : '1px solid var(--z-border)',
    background: activo ? color : 'var(--z-bg-card)',
    color:      activo ? '#fff' : 'var(--z-text-muted)',
    fontSize: 12,
    fontWeight: activo ? 600 : 400,
    cursor: 'pointer',
  });

  const aplicar = (p: Preset, cd = customDesde, ch = customHasta) => {
    const { desde, hasta } = calcularRangoPeriodo(p, cd, ch);
    onChange(desde, hasta);
  };

  return (
    <div>
      <p style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.05em', marginBottom: 8 }}>
        Período
      </p>
      <div style={{ display: 'flex', gap: 6, flexWrap: 'wrap', alignItems: 'center' }}>
        {PRESETS_PERIODO.map(p => (
          <button key={p.valor} style={chip(preset === p.valor)}
            onClick={() => { setPreset(p.valor); if (p.valor !== 'custom') aplicar(p.valor); }}>
            {p.label}
          </button>
        ))}
      </div>
      {preset === 'custom' && (
        <div style={{ display: 'flex', gap: 10, marginTop: 12, flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <label style={{ fontSize: 11, color: 'var(--z-text-muted)', fontWeight: 600 }}>Desde</label>
            <input type="date" value={customDesde} onChange={e => setCustomDesde(e.target.value)}
              style={{ padding: '7px 10px', fontSize: 13, borderRadius: 7, border: '1px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }} />
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <label style={{ fontSize: 11, color: 'var(--z-text-muted)', fontWeight: 600 }}>Hasta</label>
            <input type="date" value={customHasta} onChange={e => setCustomHasta(e.target.value)}
              style={{ padding: '7px 10px', fontSize: 13, borderRadius: 7, border: '1px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }} />
          </div>
          <button onClick={() => aplicar('custom')}
            style={{ padding: '8px 18px', borderRadius: 7, border: 'none', background: color, color: '#fff', fontSize: 13, fontWeight: 600, cursor: 'pointer' }}>
            Aplicar
          </button>
        </div>
      )}
    </div>
  );
}

function KpiCard({ label, value, color = C.brand }: { label: string; value: string | number; color?: string }) {
  return (
    <div style={{
      background: 'var(--z-bg-card)',
      border: '1px solid var(--z-border)',
      borderTop: `3px solid ${color}`,
      borderRadius: 8,
      padding: '14px 20px',
      minWidth: 120,
      flex: 1,
    }}>
      <div style={{ fontSize: 26, fontWeight: 700, color, lineHeight: 1.1 }}>{value}</div>
      <div style={{ fontSize: 11, color: C.gray, marginTop: 5, textTransform: 'uppercase', letterSpacing: '0.04em' }}>{label}</div>
    </div>
  );
}

function DropZone({ onFile, accept = '.xlsx', label }: { onFile: (f: File) => void; accept?: string; label?: string }) {
  const ref   = useRef<HTMLInputElement>(null);
  const [hover, setHover] = useState(false);
  return (
    <div
      onClick={() => ref.current?.click()}
      onMouseEnter={() => setHover(true)}
      onMouseLeave={() => setHover(false)}
      style={{
        border: `2px dashed ${hover ? C.brand : 'var(--z-border)'}`,
        borderRadius: 12,
        padding: '36px 40px',
        textAlign: 'center',
        cursor: 'pointer',
        background: hover ? C.brandLight : 'var(--z-bg-page)',
        transition: 'border-color 0.15s, background 0.15s',
      }}
    >
      <div style={{ fontSize: 30, marginBottom: 10, opacity: 0.6 }}>↑</div>
      <div style={{ color: hover ? C.brand : C.gray, fontWeight: 600, fontSize: 14 }}>
        {label ?? `Arrastra o haz clic para subir ${accept.toUpperCase()}`}
      </div>
      <div style={{ color: C.gray, fontSize: 12, marginTop: 4, opacity: 0.7 }}>
        Máximo 50 MB · Formato {accept.toUpperCase()}
      </div>
      <input ref={ref} type="file" accept={accept} style={{ display: 'none' }}
        onChange={e => { if (e.target.files?.[0]) onFile(e.target.files[0]); }} />
    </div>
  );
}

function ErrorPanel({ mensaje }: { mensaje: string }) {
  return (
    <div style={{
      background: '#fef2f2',
      border: `1px solid ${C.danger}30`,
      borderLeft: `4px solid ${C.danger}`,
      borderRadius: 8,
      padding: '12px 16px',
      color: C.danger,
      fontSize: 13,
      display: 'flex',
      alignItems: 'center',
      gap: 8,
    }}>
      <span>⚠</span> {mensaje}
    </div>
  );
}

function SuccessBanner({ mensaje, onNuevaCarga, labelBtn = 'Nueva carga' }: { mensaje: string; onNuevaCarga: () => void; labelBtn?: string }) {
  return (
    <div style={{
      background: '#f0fdf4',
      border: `1px solid ${C.success}40`,
      borderLeft: `4px solid ${C.success}`,
      borderRadius: 8,
      padding: '14px 20px',
      color: C.success,
      display: 'flex',
      alignItems: 'center',
      gap: 14,
      fontSize: 14,
      fontWeight: 500,
    }}>
      <span style={{ fontSize: 18 }}>✓</span>
      <span style={{ flex: 1 }}>{mensaje}</span>
      <button onClick={onNuevaCarga}
        style={{ background: C.success, color: '#fff', border: 'none', borderRadius: 7, padding: '6px 16px', cursor: 'pointer', fontSize: 13, fontWeight: 600 }}>
        {labelBtn}
      </button>
    </div>
  );
}

function LoadingRow({ label }: { label: string }) {
  return (
    <div style={{
      display: 'flex', alignItems: 'center', gap: 10,
      color: C.brand, fontSize: 13, padding: '12px 16px',
      background: C.brandLight, borderRadius: 8,
    }}>
      <span>⟳</span> {label}
    </div>
  );
}

function SectionDivider({ title, color = C.brand }: { title: string; color?: string }) {
  return (
    <div style={{ display: 'flex', alignItems: 'center', gap: 10, borderTop: '1px solid var(--z-border)', paddingTop: 22, marginTop: 6 }}>
      <div style={{ width: 4, height: 18, borderRadius: 2, background: color, flexShrink: 0 }} />
      <h4 style={{ margin: 0, fontSize: 14, fontWeight: 600, color: 'var(--z-text-primary)' }}>{title}</h4>
    </div>
  );
}

function AlertPanel({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <div style={{
      background: '#fffbeb',
      border: `1px solid ${C.warning}40`,
      borderLeft: `4px solid ${C.warning}`,
      borderRadius: 8,
      padding: '12px 16px',
    }}>
      <strong style={{ color: C.warning, fontSize: 13, display: 'block', marginBottom: 8 }}>⚠ {title}</strong>
      {children}
    </div>
  );
}

const tooltipStyle = {
  contentStyle: {
    background: 'var(--z-bg-card)',
    border: '1px solid var(--z-border)',
    borderRadius: 7,
    fontSize: 12,
    boxShadow: '0 2px 8px rgba(0,0,0,0.08)',
  },
};

const thStyle: React.CSSProperties = {
  padding: '8px 12px',
  textAlign: 'left',
  color: C.gray,
  fontSize: 11,
  fontWeight: 600,
  textTransform: 'uppercase',
  letterSpacing: '0.04em',
  borderBottom: '2px solid var(--z-border)',
  background: 'var(--z-bg-page)',
};

const tdStyle: React.CSSProperties = { padding: '7px 12px' };

function BtnPrimary({ children, onClick, disabled, color = C.brand }: { children: React.ReactNode; onClick?: () => void; disabled?: boolean; color?: string }) {
  return (
    <button onClick={onClick} disabled={disabled}
      style={{ background: disabled ? '#ccc' : color, color: '#fff', border: 'none', borderRadius: 8, padding: '9px 22px', cursor: disabled ? 'not-allowed' : 'pointer', fontWeight: 600, fontSize: 13 }}>
      {children}
    </button>
  );
}

function BtnSecondary({ children, onClick }: { children: React.ReactNode; onClick?: () => void }) {
  return (
    <button onClick={onClick}
      style={{ background: 'transparent', border: '1px solid var(--z-border)', borderRadius: 8, padding: '9px 18px', cursor: 'pointer', color: C.gray, fontSize: 13 }}>
      {children}
    </button>
  );
}

// ── Tab Mensajería ────────────────────────────────────────────────────────────

function TabMensajeria() {
  const { esLider, liderPropio }          = useAlcanceLider();
  const [desde,         setDesde]         = useState(HACE30);
  const [hasta,         setHasta]         = useState(HOY);
  const [filtroLider,   setFiltroLider]   = useState(liderPropio);   // 0 = todos (solo Admin/Director)
  const [filtroCartera, setFiltroCartera] = useState(0);             // 0 = todas

  const { data: opciones = [] } = useQuery({
    queryKey: ['calificador-opciones'],
    queryFn:  obtenerOpcionesCalificador,
    staleTime: 10 * 60 * 1000,
  });

  const { data: informe, refetch, isFetching: cargandoInforme } = useQuery({
    queryKey: ['informe-mensajeria', desde, hasta, filtroLider, filtroCartera],
    queryFn:  () => getInformeMensajeria(
      desde, hasta,
      filtroLider   > 0 ? filtroLider   : undefined,
      filtroCartera > 0 ? filtroCartera : undefined,
    ),
    enabled:  false,
  });

  // Listas de líderes y carteras únicas (derivadas de la etiqueta "Cartera — Líder").
  // El dropdown del Calificador incluye entradas "Cartera Completo" con liderID null;
  // para el filtro de Virtualidad solo aplican las combinaciones líder-cartera concretas.
  const opcionesPorLider = opciones.filter((o): o is typeof o & { liderID: number } => o.liderID !== null);
  const lideresUnicos = Array.from(
    new Map(opcionesPorLider.map(o => [o.liderID, { id: o.liderID, nombre: o.etiqueta.split(' — ')[1] ?? `Líder ${o.liderID}` }])).values()
  ).sort((a, b) => a.nombre.localeCompare(b.nombre, 'es', { sensitivity: 'base' }));

  const carterasFiltradas = Array.from(
    new Map(
      opcionesPorLider
        .filter(o => filtroLider === 0 || o.liderID === filtroLider)
        .map(o => [o.carteraID, { id: o.carteraID, nombre: o.etiqueta.split(' — ')[0] ?? `Cartera ${o.carteraID}` }])
    ).values()
  ).sort((a, b) => a.nombre.localeCompare(b.nombre, 'es', { sensitivity: 'base' }));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* Cabecera del tab */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, paddingBottom: 16, borderBottom: '1px solid var(--z-border)' }}>
        <div style={{ width: 4, height: 32, borderRadius: 2, background: C.mensajes }} />
        <div>
          <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>Mensajería (SMS)</h3>
          <p style={{ margin: '2px 0 0', fontSize: 12, color: C.gray }}>Seguimiento de mensajes de texto enviados a titulares</p>
        </div>
      </div>

      <p style={{ margin: 0, fontSize: 12, color: C.gray, background: 'var(--z-bg-page)', border: '1px solid var(--z-border)', borderRadius: 8, padding: '10px 14px' }}>
        El registro de solicitudes SMS ahora se hace en <strong>Administración → Gestión de Solicitudes de Virtualidad</strong>. Aquí solo se consulta el informe.
      </p>

      {/* Sección informe */}
      <SectionDivider title="Informe de mensajería enviada" color={C.mensajes} />
      <SelectorPeriodo color={C.mensajes} onChange={(d, h) => { setDesde(d); setHasta(h); }} />

      {/* Filtros líder + cartera */}
      <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
        {!esLider && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <label style={{ fontSize: 11, fontWeight: 600, color: C.gray, textTransform: 'uppercase', letterSpacing: '0.04em' }}>Líder</label>
            <ZDropdown
              options={[{ value: 0, label: 'Todos' }, ...lideresUnicos.map(l => ({ value: l.id, label: l.nombre }))]}
              value={filtroLider}
              onChange={v => { setFiltroLider(v); setFiltroCartera(0); }}
              background="var(--z-bg-page)"
              style={{ minWidth: 220 }}
            />
          </div>
        )}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <label style={{ fontSize: 11, fontWeight: 600, color: C.gray, textTransform: 'uppercase', letterSpacing: '0.04em' }}>Cartera</label>
          <ZDropdown
            options={[{ value: 0, label: 'Todas' }, ...carterasFiltradas.map(c => ({ value: c.id, label: c.nombre }))]}
            value={filtroCartera}
            onChange={setFiltroCartera}
            background="var(--z-bg-page)"
            style={{ minWidth: 220 }}
          />
        </div>
      </div>

      <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
        <BtnPrimary color={C.mensajes} disabled={cargandoInforme} onClick={() => refetch()}>
          {cargandoInforme ? 'Generando…' : 'Generar informe'}
        </BtnPrimary>
        <span style={{ fontSize: 12, color: C.gray }}>
          Período: <strong>{formatoDDMMYYYY(desde)}</strong> → <strong>{formatoDDMMYYYY(hasta)}</strong>
        </span>
      </div>

      {cargandoInforme && <LoadingRow label="Calculando informe del período…" />}

      {informe && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Total mensajes"   value={informe.totalMensajes}             color={C.mensajes} />
            <KpiCard label="Combinaciones"    value={informe.porLiderCartera.length}    color={C.gray}     />
          </div>
          <div style={{ height: Math.max(230, informe.porLiderCartera.length * 30 + 60), marginTop: 4 }}>
            <p style={{ fontSize: 12, color: C.gray, margin: '0 0 8px', fontWeight: 600 }}>Mensajes por líder y cartera</p>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart
                data={informe.porLiderCartera.map(p => ({ ...p, etiqueta: `${p.nombreLider} — ${p.nombreCartera}` }))}
                layout="vertical"
              >
                <CartesianGrid strokeDasharray="3 3" stroke="var(--z-border)" horizontal={false} />
                <XAxis type="number" tick={{ fontSize: 11 }} />
                <YAxis dataKey="etiqueta" type="category" width={220} tick={{ fontSize: 11 }} />
                <Tooltip {...tooltipStyle} />
                <Bar dataKey="total" fill={C.mensajes} radius={[0, 4, 4, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>

          {/* Tabla de detalle */}
          <table style={{ fontSize: 12, borderCollapse: 'collapse', width: '100%' }}>
            <thead>
              <tr>{['Líder', 'Cartera', 'Total mensajes'].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr>
            </thead>
            <tbody>{informe.porLiderCartera.map((p, i) => (
              <tr key={`${p.liderID}_${p.carteraID}`} style={{ background: i % 2 === 0 ? 'transparent' : 'var(--z-bg-page)' }}>
                <td style={tdStyle}>{p.nombreLider}</td>
                <td style={tdStyle}>{p.nombreCartera}</td>
                <td style={{ ...tdStyle, fontWeight: 600, color: C.mensajes }}>{p.total}</td>
              </tr>
            ))}</tbody>
          </table>
          {informe.alertas.length > 0 && (
            <AlertPanel title={`Alertas en el período — líneas con >4 mensajes`}>
              <table style={{ width: '100%', fontSize: 12, borderCollapse: 'collapse' }}>
                <thead><tr><th style={thStyle}>Teléfono</th><th style={thStyle}>Cartera</th><th style={thStyle}>Total</th></tr></thead>
                <tbody>{informe.alertas.map((a, i) => (
                  <tr key={i} style={{ background: i % 2 === 0 ? 'transparent' : 'var(--z-bg-page)' }}>
                    <td style={tdStyle}>{a.numeroTelefono}</td>
                    <td style={tdStyle}>{a.nombreCartera}</td>
                    <td style={{ ...tdStyle, color: C.danger, fontWeight: 700 }}>{a.totalMensajes}</td>
                  </tr>
                ))}</tbody>
              </table>
            </AlertPanel>
          )}
        </>
      )}
    </div>
  );
}

// ── Tab IVR ───────────────────────────────────────────────────────────────────

function TabIVR() {
  const { esLider, liderPropio }          = useAlcanceLider();
  const [desde,         setDesde]         = useState(HACE30);
  const [hasta,         setHasta]         = useState(HOY);
  const [filtroLider,   setFiltroLider]   = useState(liderPropio);   // 0 = todos (solo Admin/Director)
  const [filtroCartera, setFiltroCartera] = useState(0);

  const { data: opciones = [] } = useQuery({
    queryKey: ['calificador-opciones'],
    queryFn:  obtenerOpcionesCalificador,
    staleTime: 10 * 60 * 1000,
  });

  const { data: informe, refetch, isFetching: cargandoInforme } = useQuery({
    queryKey: ['informe-ivr', desde, hasta, filtroLider, filtroCartera],
    queryFn:  () => getInformeIVR(
      desde, hasta,
      filtroLider   > 0 ? filtroLider   : undefined,
      filtroCartera > 0 ? filtroCartera : undefined,
    ),
    enabled:  false,
  });

  // Análisis de embudo y desenlaces (ADR-031). Se dispara con el mismo botón.
  const { data: analisis, refetch: refetchAnalisis, isFetching: cargandoAnalisis } = useQuery({
    queryKey: ['analisis-ivr', desde, hasta, filtroLider, filtroCartera],
    queryFn:  () => getAnalisisIvr(
      desde, hasta,
      filtroLider   > 0 ? filtroLider   : undefined,
      filtroCartera > 0 ? filtroCartera : undefined,
    ),
    enabled:  false,
  });

  // Idem al primer bloque: descartamos entradas "Cartera Completo" (liderID null)
  // — el filtro de Virtualidad opera por combinación líder-cartera concreta.
  const opcionesPorLider = opciones.filter((o): o is typeof o & { liderID: number } => o.liderID !== null);
  const lideresUnicos = Array.from(
    new Map(opcionesPorLider.map(o => [o.liderID, { id: o.liderID, nombre: o.etiqueta.split(' — ')[1] ?? `Líder ${o.liderID}` }])).values()
  ).sort((a, b) => a.nombre.localeCompare(b.nombre, 'es', { sensitivity: 'base' }));

  const carterasFiltradas = Array.from(
    new Map(
      opcionesPorLider
        .filter(o => filtroLider === 0 || o.liderID === filtroLider)
        .map(o => [o.carteraID, { id: o.carteraID, nombre: o.etiqueta.split(' — ')[0] ?? `Cartera ${o.carteraID}` }])
    ).values()
  ).sort((a, b) => a.nombre.localeCompare(b.nombre, 'es', { sensitivity: 'base' }));

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* Cabecera del tab */}
      <div style={{ display: 'flex', alignItems: 'center', gap: 12, paddingBottom: 16, borderBottom: '1px solid var(--z-border)' }}>
        <div style={{ width: 4, height: 32, borderRadius: 2, background: C.ivr }} />
        <div>
          <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>IVR</h3>
          <p style={{ margin: '2px 0 0', fontSize: 12, color: C.gray }}>Seguimiento de envíos IVR (voz automatizada) a titulares</p>
        </div>
      </div>

      <p style={{ margin: 0, fontSize: 12, color: C.gray, background: 'var(--z-bg-page)', border: '1px solid var(--z-border)', borderRadius: 8, padding: '10px 14px' }}>
        Los resultados de IVR se cargan en <strong>Administración → Gestión de Histórico de Virtualidad</strong>. Aquí solo se consulta el análisis.
      </p>

      {/* Sección informe */}
      <SectionDivider title="Análisis de IVR" color={C.ivr} />
      <SelectorPeriodo color={C.ivr} onChange={(d, h) => { setDesde(d); setHasta(h); }} />

      <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
        {!esLider && (
          <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
            <label style={{ fontSize: 11, fontWeight: 600, color: C.gray, textTransform: 'uppercase', letterSpacing: '0.04em' }}>Líder</label>
            <ZDropdown
              options={[{ value: 0, label: 'Todos' }, ...lideresUnicos.map(l => ({ value: l.id, label: l.nombre }))]}
              value={filtroLider}
              onChange={v => { setFiltroLider(v); setFiltroCartera(0); }}
              background="var(--z-bg-page)"
              style={{ minWidth: 220 }}
            />
          </div>
        )}
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <label style={{ fontSize: 11, fontWeight: 600, color: C.gray, textTransform: 'uppercase', letterSpacing: '0.04em' }}>Cartera</label>
          <ZDropdown
            options={[{ value: 0, label: 'Todas' }, ...carterasFiltradas.map(c => ({ value: c.id, label: c.nombre }))]}
            value={filtroCartera}
            onChange={setFiltroCartera}
            background="var(--z-bg-page)"
            style={{ minWidth: 220 }}
          />
        </div>
      </div>

      <div style={{ display: 'flex', gap: 10, alignItems: 'center', flexWrap: 'wrap' }}>
        <BtnPrimary
          color={C.ivr}
          disabled={cargandoInforme || cargandoAnalisis}
          onClick={() => { refetchAnalisis(); refetch(); }}
        >
          {cargandoInforme || cargandoAnalisis ? 'Generando…' : 'Generar informe'}
        </BtnPrimary>
        <span style={{ fontSize: 12, color: C.gray }}>
          Período: <strong>{formatoDDMMYYYY(desde)}</strong> → <strong>{formatoDDMMYYYY(hasta)}</strong>
        </span>
      </div>

      {(cargandoInforme || cargandoAnalisis) && <LoadingRow label="Calculando informe del período…" />}

      {analisis && !cargandoAnalisis && <AnalisisIvrPanel analisis={analisis} />}

      {informe && (
        <>
          {informe.alertas.length > 0 && (
            <AlertPanel title="Alertas en el período — líneas con >5 IVR">
              <table style={{ width: '100%', fontSize: 12, borderCollapse: 'collapse' }}>
                <thead><tr><th style={thStyle}>Teléfono</th><th style={thStyle}>Cartera</th><th style={thStyle}>Total</th></tr></thead>
                <tbody>{informe.alertas.map((a, i) => (
                  <tr key={i} style={{ background: i % 2 === 0 ? 'transparent' : 'var(--z-bg-page)' }}>
                    <td style={tdStyle}>{a.numeroTelefono}</td>
                    <td style={tdStyle}>{a.nombreCartera}</td>
                    <td style={{ ...tdStyle, color: C.danger, fontWeight: 700 }}>{a.totalIVR}</td>
                  </tr>
                ))}</tbody>
              </table>
            </AlertPanel>
          )}
        </>
      )}
    </div>
  );
}

// ── Tab Llamadas Perdidas ─────────────────────────────────────────────────────

function TabLlamadasPerdidas() {
  const [etapa,   setEtapa]   = useState<'carga' | 'preview' | 'exito'>('carga');
  const [preview, setPreview] = useState<ValidarLlamadasResponse | null>(null);

  const validar   = useMutation({ mutationFn: validarLlamadasPerdidas,                                           onSuccess: r => { setPreview(r); setEtapa('preview'); } });
  const confirmar = useMutation({ mutationFn: (f: FilaLlamada[]) => confirmarLlamadasPerdidas(f),               onSuccess: () => setEtapa('exito') });

  const { data: informe, refetch } = useQuery({
    queryKey: ['informe-llamadas'],
    queryFn:  getInformeLlamadasPerdidas,
    enabled:  false,
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      <div style={{ display: 'flex', alignItems: 'center', gap: 12, paddingBottom: 16, borderBottom: '1px solid var(--z-border)' }}>
        <div style={{ width: 4, height: 32, borderRadius: 2, background: C.llamadas }} />
        <div>
          <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>Llamadas Perdidas</h3>
          <p style={{ margin: '2px 0 0', fontSize: 12, color: C.gray }}>Registro de clientes vigentes que intentaron comunicarse. Formato: TitularID · Celular · Cartera</p>
        </div>
      </div>

      {etapa === 'carga' && (
        <>
          <DropZone onFile={f => validar.mutate(f)} label="Subir Excel de llamadas perdidas (.xlsx)" />
          {validar.isPending && <LoadingRow label="Validando archivo…" />}
          {validar.isError   && <ErrorPanel mensaje="Error al validar el archivo." />}
        </>
      )}

      {etapa === 'preview' && preview && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Total filas"   value={preview.totalFilas}     color={C.llamadas} />
            <KpiCard label="Vigentes"      value={preview.totalValidas}   color={C.success}  />
            <KpiCard label="Descartadas"   value={preview.totalInvalidas} color={C.danger}   />
          </div>
          <table style={{ fontSize: 12, borderCollapse: 'collapse', width: '100%' }}>
            <thead>
              <tr>{['TitularID', 'Celular', 'Cartera', 'Estado'].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr>
            </thead>
            <tbody>{preview.preview.map((f, i) => (
              <tr key={i} style={{ background: i % 2 === 0 ? 'transparent' : 'var(--z-bg-page)' }}>
                <td style={tdStyle}>{f.titularID}</td>
                <td style={tdStyle}>{f.celular}</td>
                <td style={tdStyle}>{f.nombreCartera}</td>
                <td style={{ ...tdStyle, color: f.estado === 'Válida' ? C.success : C.danger, fontWeight: 500 }}>
                  {f.estado === 'Válida' ? '✓ Vigente' : f.estado}
                </td>
              </tr>
            ))}</tbody>
          </table>
          <div style={{ display: 'flex', gap: 10 }}>
            <BtnPrimary color={C.llamadas} disabled={preview.totalValidas === 0 || confirmar.isPending}
              onClick={() => confirmar.mutate(preview.filasValidas)}>
              Confirmar {preview.totalValidas} llamada(s)
            </BtnPrimary>
            <BtnSecondary onClick={() => setEtapa('carga')}>Cancelar</BtnSecondary>
          </div>
        </>
      )}

      {etapa === 'exito' && (
        <SuccessBanner mensaje="Llamadas perdidas registradas correctamente."
          onNuevaCarga={() => { setEtapa('carga'); setPreview(null); }} />
      )}

      <SectionDivider title="Informe histórico de llamadas" color={C.llamadas} />
      <div>
        <BtnPrimary color={C.llamadas} onClick={() => refetch()}>Consultar historial</BtnPrimary>
      </div>

      {informe && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Total registros" value={informe.totalRegistros} color={C.llamadas} />
          </div>
          <div style={{ height: 220 }}>
            <p style={{ fontSize: 12, color: C.gray, margin: '0 0 8px', fontWeight: 600 }}>Llamadas perdidas por cartera</p>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={informe.porCartera} layout="vertical">
                <CartesianGrid strokeDasharray="3 3" stroke="var(--z-border)" horizontal={false} />
                <XAxis type="number" tick={{ fontSize: 11 }} />
                <YAxis dataKey="nombreCartera" type="category" width={130} tick={{ fontSize: 11 }} />
                <Tooltip {...tooltipStyle} />
                <Bar dataKey="total" fill={C.llamadas} radius={[0, 4, 4, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
          {informe.alertas.length > 0 && (
            <AlertPanel title="Misma línea comunicada múltiples veces">
              <table style={{ width: '100%', fontSize: 12, borderCollapse: 'collapse' }}>
                <thead><tr><th style={thStyle}>Celular</th><th style={thStyle}>Veces comunicado</th></tr></thead>
                <tbody>{informe.alertas.map((a, i) => (
                  <tr key={i} style={{ background: i % 2 === 0 ? 'transparent' : 'var(--z-bg-page)' }}>
                    <td style={tdStyle}>{a.celular}</td>
                    <td style={{ ...tdStyle, color: C.danger, fontWeight: 700 }}>{a.vecesComunicado}</td>
                  </tr>
                ))}</tbody>
              </table>
            </AlertPanel>
          )}
        </>
      )}
    </div>
  );
}

// ── Tab Llamadas Entrantes ────────────────────────────────────────────────────

function TabLlamadasEntrantes() {
  const [etapa,   setEtapa]   = useState<'carga' | 'preview' | 'exito'>('carga');
  const [preview, setPreview] = useState<ValidarEntrantesResponse | null>(null);
  const [anio,    setAnio]    = useState(AÑO_HOY);

  const validar   = useMutation({ mutationFn: validarLlamadasEntrantes,                                          onSuccess: r => { setPreview(r); setEtapa('preview'); } });
  const confirmar = useMutation({ mutationFn: () => confirmarLlamadasEntrantes(preview!.filasParaGuardar),       onSuccess: () => setEtapa('exito') });

  const { data: informe, refetch } = useQuery({
    queryKey: ['informe-entrantes', anio],
    queryFn:  () => getInformeEntrantes(anio),
    enabled:  false,
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      <div style={{ display: 'flex', alignItems: 'center', gap: 12, paddingBottom: 16, borderBottom: '1px solid var(--z-border)' }}>
        <div style={{ width: 4, height: 32, borderRadius: 2, background: C.entrantes }} />
        <div>
          <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>Llamadas Entrantes</h3>
          <p style={{ margin: '2px 0 0', fontSize: 12, color: C.gray }}>Carga semanal con celulares. El sistema resuelve titulares y detecta acuerdos de pago.</p>
        </div>
      </div>

      {etapa === 'carga' && (
        <>
          <DropZone onFile={f => validar.mutate(f)} label="Subir Excel de llamadas entrantes (.xlsx)" />
          {validar.isPending && <LoadingRow label="Procesando archivo…" />}
          {validar.isError   && <ErrorPanel mensaje="Error al validar el archivo." />}
        </>
      )}

      {etapa === 'preview' && preview && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Celulares"    value={preview.totalCelulares}               color={C.entrantes} />
            <KpiCard label="Resueltos"    value={preview.totalResueltos}               color={C.brand}     />
            <KpiCard label="Con acuerdo"  value={preview.totalConAcuerdo}              color={C.success}   />
            <KpiCard label="Efectividad"  value={`${preview.efectividad}%`}            color={C.warning}   />
          </div>
          <div style={{ display: 'flex', gap: 10 }}>
            <BtnPrimary color={C.entrantes} disabled={confirmar.isPending} onClick={() => confirmar.mutate()}>
              Guardar {preview.totalCelulares} registro(s)
            </BtnPrimary>
            <BtnSecondary onClick={() => setEtapa('carga')}>Cancelar</BtnSecondary>
          </div>
        </>
      )}

      {etapa === 'exito' && (
        <SuccessBanner mensaje="Llamadas entrantes guardadas correctamente."
          onNuevaCarga={() => { setEtapa('carga'); setPreview(null); }} />
      )}

      <SectionDivider title="Efectividad por semana" color={C.entrantes} />
      <div style={{ display: 'flex', gap: 10, alignItems: 'flex-end' }}>
        <div style={{ display: 'flex', flexDirection: 'column', gap: 4 }}>
          <label style={{ fontSize: 11, color: 'var(--z-text-muted)', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.04em' }}>Año</label>
          <input type="number" value={anio} min={2024} max={2099}
            onChange={e => setAnio(+e.target.value)}
            style={{ width: 84, padding: '7px 10px', fontSize: 13, borderRadius: 7, border: '1px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }} />
        </div>
        <BtnPrimary color={C.entrantes} onClick={() => refetch()}>Consultar</BtnPrimary>
      </div>

      {informe && (
        <div style={{ height: 240 }}>
          <p style={{ fontSize: 12, color: C.gray, margin: '0 0 8px', fontWeight: 600 }}>Llamadas entrantes por semana — {anio}</p>
          <ResponsiveContainer width="100%" height="100%">
            <BarChart data={informe.porSemana}>
              <CartesianGrid strokeDasharray="3 3" stroke="var(--z-border)" vertical={false} />
              <XAxis dataKey="semana" tick={{ fontSize: 11 }} label={{ value: 'Semana', position: 'insideBottom', offset: -2, fontSize: 11 }} />
              <YAxis tick={{ fontSize: 11 }} />
              <Tooltip {...tooltipStyle} formatter={(val, name) => [name === 'efectividad' ? `${val}%` : val, name]} />
              <Legend verticalAlign="top" wrapperStyle={{ fontSize: 12 }} />
              <Bar dataKey="totalCelulares" fill={C.gray}      name="Total"        radius={[4, 4, 0, 0]} />
              <Bar dataKey="resueltos"      fill={C.brand}     name="Resueltos"    radius={[4, 4, 0, 0]} />
              <Bar dataKey="conAcuerdo"     fill={C.success}   name="Con acuerdo"  radius={[4, 4, 0, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>
      )}
    </div>
  );
}

// ── Tab Pagos vs Impacto ──────────────────────────────────────────────────────

function TabPagosImpacto() {
  const [desde, setDesde] = useState(HACE30);
  const [hasta, setHasta] = useState(HOY);

  const { data: informe, refetch, isFetching } = useQuery({
    queryKey: ['pagos-impacto', desde, hasta],
    queryFn:  () => getInformePagosImpacto(desde, hasta),
    enabled:  false,
  });

  const barData = informe ? [
    { canal: 'Mensajería',       pagos: informe.resumen.pagosConMensajeria, fill: C.mensajes  },
    { canal: 'IVR',              pagos: informe.resumen.pagosConIVR,        fill: C.ivr       },
    { canal: 'Llamada perdida',  pagos: informe.resumen.pagosConLlamada,    fill: C.llamadas  },
    { canal: 'Sin impacto',      pagos: informe.resumen.pagosSinImpacto,    fill: C.gray      },
  ] : [];

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      <div style={{ display: 'flex', alignItems: 'center', gap: 12, paddingBottom: 16, borderBottom: '1px solid var(--z-border)' }}>
        <div style={{ width: 4, height: 32, borderRadius: 2, background: C.brand }} />
        <div>
          <h3 style={{ margin: 0, fontSize: 16, fontWeight: 700 }}>Pagos vs Canal de Impacto</h3>
          <p style={{ margin: '2px 0 0', fontSize: 12, color: C.gray }}>De los titulares que pagaron, ¿por qué canal de virtualidad fueron impactados previamente?</p>
        </div>
      </div>

      <SelectorPeriodo color={C.brand} onChange={(d, h) => { setDesde(d); setHasta(h); refetch(); }} />

      {isFetching && <LoadingRow label="Calculando relación pagos vs canales…" />}

      {informe && (
        <>
          <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
            <KpiCard label="Total pagos"     value={informe.resumen.totalPagos}         color={C.brand}    />
            <KpiCard label="Con mensajería"  value={informe.resumen.pagosConMensajeria} color={C.mensajes} />
            <KpiCard label="Con IVR"         value={informe.resumen.pagosConIVR}        color={C.ivr}      />
            <KpiCard label="Con llamada"     value={informe.resumen.pagosConLlamada}    color={C.llamadas} />
            <KpiCard label="Sin impacto"     value={informe.resumen.pagosSinImpacto}    color={C.gray}     />
          </div>

          <div style={{ height: 240, marginTop: 4 }}>
            <p style={{ fontSize: 12, color: C.gray, margin: '0 0 8px', fontWeight: 600 }}>Distribución de pagos por canal</p>
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={barData}>
                <CartesianGrid strokeDasharray="3 3" stroke="var(--z-border)" vertical={false} />
                <XAxis dataKey="canal" tick={{ fontSize: 12 }} />
                <YAxis tick={{ fontSize: 11 }} />
                <Tooltip {...tooltipStyle} />
                <Bar dataKey="pagos" radius={[4, 4, 0, 0]}>
                  {barData.map((entry, i) => <Cell key={i} fill={entry.fill} />)}
                </Bar>
              </BarChart>
            </ResponsiveContainer>
          </div>

          <SectionDivider title={`Detalle de pagos — mostrando ${Math.min(informe.filas.length, 50)} de ${informe.filas.length}`} color={C.brand} />

          <table style={{ fontSize: 12, borderCollapse: 'collapse', width: '100%' }}>
            <thead>
              <tr>{['Cédula', 'Fecha pago', 'Valor', 'Mensajería', 'IVR', 'Llamada'].map(h => <th key={h} style={thStyle}>{h}</th>)}</tr>
            </thead>
            <tbody>{informe.filas.slice(0, 50).map((f, i) => (
              <tr key={i} style={{ background: i % 2 === 0 ? 'transparent' : 'var(--z-bg-page)' }}>
                <td style={tdStyle}>{f.cedulaTitular}</td>
                <td style={tdStyle}>{f.fechaPago}</td>
                <td style={{ ...tdStyle, fontWeight: 500 }}>${f.valorPagado.toLocaleString('es-CO')}</td>
                <td style={{ ...tdStyle, color: f.impactadoMensajeria ? C.mensajes : C.gray, fontWeight: f.impactadoMensajeria ? 700 : 400 }}>
                  {f.impactadoMensajeria ? '✓' : '—'}
                </td>
                <td style={{ ...tdStyle, color: f.impactadoIVR ? C.ivr : C.gray, fontWeight: f.impactadoIVR ? 700 : 400 }}>
                  {f.impactadoIVR ? '✓' : '—'}
                </td>
                <td style={{ ...tdStyle, color: f.impactadoLlamada ? C.llamadas : C.gray, fontWeight: f.impactadoLlamada ? 700 : 400 }}>
                  {f.impactadoLlamada ? '✓' : '—'}
                </td>
              </tr>
            ))}</tbody>
          </table>
        </>
      )}
    </div>
  );
}

// ── Página principal ──────────────────────────────────────────────────────────

// `soloAdmin`: tabs que un Líder no ve. Llamadas Perdidas y Entrantes son cargas de
// archivo (operación administrativa) y sus registros no traen LiderID — solo titular
// y cartera — así que no hay forma exacta de acotarlos a un líder sin inferir por
// asignación, y eso cambia con cada reasignación. Se dejan fuera antes que mostrar
// cifras "aproximadamente suyas".
const TABS: { key: Tab; label: string; color: string; soloAdmin?: boolean }[] = [
  { key: 'mensajeria',         label: 'Mensajería',        color: C.mensajes  },
  { key: 'ivr',                label: 'IVR',               color: C.ivr       },
  { key: 'llamadas-perdidas',  label: 'Llamadas Perdidas', color: C.llamadas,  soloAdmin: true },
  { key: 'llamadas-entrantes', label: 'Llamadas Entrantes',color: C.entrantes, soloAdmin: true },
  { key: 'pagos-impacto',      label: 'Pagos vs Impacto',  color: C.brand     },
];

export function InformesVirtualidadPage() {
  const { esLider } = useAlcanceLider();
  const [tab, setTab] = useState<Tab>('mensajeria');
  const tabsVisibles = TABS.filter(t => !esLider || !t.soloAdmin);

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 20 }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ margin: 0, fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)' }}>Informes de Virtualidad</h1>
          <p style={{ margin: '4px 0 0', fontSize: 13, color: 'var(--z-text-muted)' }}>
            {esLider
              ? 'Tus canales digitales — Mensajería, IVR y análisis de impacto en pagos de tus carteras'
              : 'Gestión de canales digitales — Mensajería, IVR, Llamadas y análisis de impacto en pagos'}
          </p>
        </div>
      </div>

      {/* Tabs */}
      <style>{`
        .z-tab { position: relative; transition: color 0.2s ease; }
        .z-tab::after {
          content: '';
          position: absolute;
          left: 50%; right: 50%; bottom: -1px; height: 2px;
          background: var(--z-brand);
          transition: left 0.22s ease, right 0.22s ease;
        }
        .z-tab:hover { color: var(--z-brand); }
        .z-tab:hover::after { left: 14px; right: 14px; }
        .z-tab-activa::after,
        .z-tab-activa:hover::after { left: 0; right: 0; }
      `}</style>
      <div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--z-border)' }}>
        {tabsVisibles.map(t => (
          <button key={t.key} onClick={() => setTab(t.key)}
            className={`z-tab${tab === t.key ? ' z-tab-activa' : ''}`}
            style={{
              padding: '9px 20px',
              fontSize: 13,
              fontWeight: tab === t.key ? 600 : 400,
              color: tab === t.key ? 'var(--z-brand)' : 'var(--z-text-muted)',
              background: 'none',
              border: 'none',
              cursor: 'pointer',
              marginBottom: -1,
            }}>
            {t.label}
          </button>
        ))}
      </div>

      {/* Contenido del tab activo */}
      <div style={{ background: 'var(--z-bg-card)', border: '1px solid var(--z-border)', borderRadius: 10, padding: 28 }}>
        {tab === 'mensajeria'         && <TabMensajeria />}
        {tab === 'ivr'                && <TabIVR />}
        {tab === 'llamadas-perdidas'  && <TabLlamadasPerdidas />}
        {tab === 'llamadas-entrantes' && <TabLlamadasEntrantes />}
        {tab === 'pagos-impacto'      && <TabPagosImpacto />}
      </div>
    </div>
  );
}
