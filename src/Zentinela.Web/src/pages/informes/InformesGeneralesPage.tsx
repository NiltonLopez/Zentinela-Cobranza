import { useState } from 'react';
import { UsoBaseDatos } from './informes-generales/UsoBaseDatos';

// Informes transversales que no pertenecen a un módulo concreto. Nace con un solo
// tab; la unión está pensada para crecer sin reestructurar el shell.
type TabGeneral = 'uso-base-datos';

const TABS: [TabGeneral, string][] = [
  ['uso-base-datos', 'Uso de Base de Datos'],
];

export function InformesGeneralesPage() {
  const [tab, setTab] = useState<TabGeneral>('uso-base-datos');

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 24, maxWidth: 1280 }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Informes Generales
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Cruza los teléfonos de tus archivos contra las fuentes de localización y acredita el
            consumo de la base de datos por líder.
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
        {TABS.map(([id, label]) => (
          <button
            key={id}
            onClick={() => setTab(id)}
            className={`z-tab${tab === id ? ' z-tab-activa' : ''}`}
            style={{
              padding: '9px 20px', fontSize: 13,
              fontWeight: tab === id ? 600 : 400,
              color: tab === id ? 'var(--z-brand)' : 'var(--z-text-muted)',
              background: 'none', border: 'none',
              cursor: 'pointer', marginBottom: -1,
            }}
          >
            {label}
          </button>
        ))}
      </div>

      {/* Contenido del tab */}
      {tab === 'uso-base-datos' && <UsoBaseDatos />}
    </div>
  );
}
