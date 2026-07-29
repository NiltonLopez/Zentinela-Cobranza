import { useState } from 'react';
import { useAuth } from '../../context/AuthContext';
import { PagoIndividualForm } from './cargue-pagos/PagoIndividualForm';
import { CargueMasivo } from './cargue-pagos/CargueMasivo';
import { BorrarPeriodoPagos } from './cargue-pagos/BorrarPeriodoPagos';

type TabPago = 'individual' | 'masivo' | 'borrar-periodo';

export function CarguePagosPage() {
  const { usuario } = useAuth();
  // Borrar período es irreversible sobre pagos ya confirmados — el backend ya lo
  // restringe a Admin (el resto del módulo también permite Auxiliar de Sistemas);
  // se oculta la pestaña para no llevar a un 403 al final del flujo.
  const esAdmin = usuario?.rol === 'Admin';

  const TABS: [TabPago, string][] = [
    ['individual', 'Pago individual'],
    ['masivo',     'Cargue masivo'],
    ...(esAdmin ? [['borrar-periodo', 'Eliminar pagos'] as [TabPago, string]] : []),
  ];

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
      {tab === 'individual' && <PagoIndividualForm />}
      {tab === 'masivo' && <CargueMasivo />}
      {tab === 'borrar-periodo' && esAdmin && <BorrarPeriodoPagos />}
    </div>
  );
}
