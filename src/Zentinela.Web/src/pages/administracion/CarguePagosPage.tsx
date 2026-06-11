import { useState } from 'react';
import { PagoIndividualForm } from './cargue-pagos/PagoIndividualForm';
import { CargueMasivo } from './cargue-pagos/CargueMasivo';

type TabPago = 'individual' | 'masivo';

const TABS: [TabPago, string][] = [
  ['individual', 'Pago individual'],
  ['masivo',     'Cargue masivo'],
];

export function CarguePagosPage() {
  const [tab, setTab] = useState<TabPago>('individual');

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '960px' }}>

      {/* Encabezado */}
      <div style={{ display: 'flex', justifyContent: 'space-between', alignItems: 'flex-start', flexWrap: 'wrap', gap: 12 }}>
        <div>
          <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
            Cargue de Pagos
          </h1>
          <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
            Registra un pago a la vez o carga varios por archivo para alimentar los informes de recaudo.
          </p>
        </div>
      </div>

      {/* Tabs */}
      <div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--z-border)' }}>
        {TABS.map(([id, label]) => (
          <button
            key={id}
            onClick={() => setTab(id)}
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

      {/* Contenido del tab */}
      {tab === 'individual' ? <PagoIndividualForm /> : <CargueMasivo />}
    </div>
  );
}
