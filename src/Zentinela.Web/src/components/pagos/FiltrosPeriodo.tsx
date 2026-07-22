import React from 'react';

// ── Tipos ─────────────────────────────────────────────────────────────────────

export type Preset = 'hoy' | 'ciclo' | 'mes-pasado' | '7d' | '30d' | 'custom';

interface Rango { desde: string; hasta: string; }

// ── Helpers internos ──────────────────────────────────────────────────────────

function isoHoy(): string {
  return new Date().toISOString().slice(0, 10);
}

function isoFecha(d: Date): string {
  return d.toISOString().slice(0, 10);
}

// ── Función exportada — usada por las páginas para computar el rango ──────────

export function calcularRango(preset: Preset, customDesde: string, customHasta: string): Rango {
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
    case 'custom': return { desde: customDesde || isoHoy(), hasta: customHasta || isoHoy() };
  }
}

// ── Constantes ────────────────────────────────────────────────────────────────

const PRESETS: { valor: Preset; label: string }[] = [
  { valor: 'hoy',        label: 'Hoy' },
  { valor: 'ciclo',      label: 'Este mes' },
  { valor: 'mes-pasado', label: 'Mes pasado' },
  { valor: '7d',         label: 'Últimos 7 días' },
  { valor: '30d',        label: 'Últimos 30 días' },
  { valor: 'custom',     label: 'Personalizado' },
];

// ── Componente ────────────────────────────────────────────────────────────────

interface FiltrosPeriodoProps {
  preset: Preset;
  onPresetChange: (p: Preset) => void;
  customDesde: string;
  customHasta: string;
  onCustomDesdeChange: (v: string) => void;
  onCustomHastaChange: (v: string) => void;
}

export function FiltrosPeriodo({
  preset, onPresetChange,
  customDesde, customHasta,
  onCustomDesdeChange, onCustomHastaChange,
}: FiltrosPeriodoProps) {
  const btnStyle = (activo: boolean): React.CSSProperties => ({
    padding: '5px 12px', borderRadius: '20px',
    border: activo ? '0.5px solid var(--z-brand)' : '0.5px solid var(--z-border)',
    background: activo ? 'var(--z-brand)' : 'var(--z-bg-page)',
    color: activo ? '#FFFFFF' : 'var(--z-text-muted)',
    fontSize: '12px', fontWeight: activo ? 600 : 400,
    cursor: 'pointer', whiteSpace: 'nowrap' as const,
  });

  return (
    <div>
      <p style={{ fontSize: '11px', fontWeight: 600, color: 'var(--z-text-muted)', textTransform: 'uppercase', letterSpacing: '0.04em', marginBottom: '8px' }}>
        Período
      </p>
      <div style={{ display: 'flex', gap: '6px', flexWrap: 'wrap', alignItems: 'center' }}>
        {PRESETS.map(p => (
          <button key={p.valor} onClick={() => onPresetChange(p.valor)} style={btnStyle(preset === p.valor)}>
            {p.label}
          </button>
        ))}
      </div>

      {preset === 'custom' && (
        <div style={{ display: 'flex', gap: '10px', marginTop: '10px', flexWrap: 'wrap', alignItems: 'flex-end' }}>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            <label style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Desde</label>
            <input
              type="date" value={customDesde} onChange={e => onCustomDesdeChange(e.target.value)}
              style={{ padding: '6px 10px', fontSize: '13px', borderRadius: '6px', border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }}
            />
          </div>
          <div style={{ display: 'flex', flexDirection: 'column', gap: '4px' }}>
            <label style={{ fontSize: '11px', color: 'var(--z-text-muted)' }}>Hasta</label>
            <input
              type="date" value={customHasta} onChange={e => onCustomHastaChange(e.target.value)}
              style={{ padding: '6px 10px', fontSize: '13px', borderRadius: '6px', border: '0.5px solid var(--z-border)', background: 'var(--z-bg-page)', color: 'var(--z-text-primary)', outline: 'none' }}
            />
          </div>
        </div>
      )}
    </div>
  );
}
