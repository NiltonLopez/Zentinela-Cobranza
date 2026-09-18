import { useState } from 'react';
import { AsistenteSms } from './creador-virtualidad/AsistenteSms';

// Canales de virtualidad. Solo SMS está implementado (ADR-037); Correo e IVR quedan
// visibles pero deshabilitados para que el shell no cambie cuando se sumen.
type Canal = 'sms' | 'correo' | 'ivr';

const TABS: { id: Canal; label: string; disponible: boolean }[] = [
  { id: 'sms',    label: 'SMS',    disponible: true  },
  { id: 'correo', label: 'Correo', disponible: false },
  { id: 'ivr',    label: 'IVR',    disponible: false },
];

export function CreadorVirtualidadPage() {
  const [tab, setTab] = useState<Canal>('sms');

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24, maxWidth: 1280 }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Creador de Virtualidad
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Genera el archivo que se entrega al área de virtualidad con un mensaje autorizado, la base depurada
            y el contacto de tus asesores.
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
        .z-tab:not(:disabled):hover { color: var(--z-brand); }
        .z-tab:not(:disabled):hover::after { left: 14px; right: 14px; }
        .z-tab-activa::after,
        .z-tab-activa:hover::after { left: 0; right: 0; }
      `}</style>
      <div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--z-border)' }}>
        {TABS.map(t => (
          <button
            key={t.id}
            onClick={() => t.disponible && setTab(t.id)}
            disabled={!t.disponible}
            title={t.disponible ? undefined : 'Próximamente'}
            className={`z-tab${tab === t.id ? ' z-tab-activa' : ''}`}
            style={{
              padding: '9px 20px', fontSize: 13,
              fontWeight: tab === t.id ? 600 : 400,
              color: tab === t.id ? 'var(--z-brand)' : 'var(--z-text-muted)',
              background: 'none', border: 'none',
              cursor: t.disponible ? 'pointer' : 'not-allowed', marginBottom: -1,
              opacity: t.disponible ? 1 : 0.5,
              display: 'flex', alignItems: 'center', gap: 6,
            }}
          >
            {t.label}
            {!t.disponible && (
              <span style={{ fontSize: 9, fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.04em', background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)', borderRadius: 999, padding: '1px 6px' }}>
                próximamente
              </span>
            )}
          </button>
        ))}
      </div>

      {tab === 'sms' && <AsistenteSms />}
    </div>
  );
}
