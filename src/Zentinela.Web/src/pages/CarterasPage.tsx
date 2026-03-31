import { useQuery } from '@tanstack/react-query';
import { TarjetaCartera } from '../components/TarjetaCartera';
import { obtenerCarteras } from '../api/carterasApi';
import type { CarteraDto } from '../types/carteras';

export function CarterasPage() {
  const { data: carteras, isLoading, isError } = useQuery({
    queryKey: ['carteras'],
    queryFn: obtenerCarteras,
  });

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '20px', maxWidth: '900px' }}>
      <div>
        <h1 style={{ fontSize: '16px', fontWeight: 500, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Mis carteras
        </h1>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          {carteras ? `${carteras.length} cartera${carteras.length !== 1 ? 's' : ''} asignada${carteras.length !== 1 ? 's' : ''}` : 'Cargando...'}
        </p>
      </div>

      {isLoading && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '12px' }}>
          {[1, 2, 3].map((n) => (
            <div key={n} style={{
              background: 'var(--z-bg-card)',
              border: '0.5px solid var(--z-border)',
              borderRadius: '8px',
              height: '80px',
              animation: 'pulse 1.5s infinite',
            }} />
          ))}
        </div>
      )}

      {isError && (
        <div style={{
          background: '#fff5f5',
          border: '0.5px solid #fecaca',
          borderRadius: '8px',
          padding: '16px',
          fontSize: '13px',
          color: '#dc2626',
        }}>
          No se pudieron cargar las carteras. Intenta de nuevo.
        </div>
      )}

      {!isLoading && !isError && carteras?.length === 0 && (
        <div style={{
          background: 'var(--z-bg-card)',
          border: '0.5px solid var(--z-border)',
          borderRadius: '8px',
          padding: '40px',
          textAlign: 'center',
        }}>
          <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>No tienes carteras asignadas.</p>
          <p style={{ fontSize: '12px', color: 'var(--z-border)', marginTop: '4px' }}>Contacta al administrador.</p>
        </div>
      )}

      {!isLoading && !isError && carteras && carteras.length > 0 && (
        <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '12px' }}>
          {carteras.map((cartera: CarteraDto) => (
            <TarjetaCartera key={cartera.id} cartera={cartera} onClick={(c) => console.log('cartera seleccionada', c.id)} />
          ))}
        </div>
      )}
    </div>
  );
}
