import { useQuery } from '@tanstack/react-query';
import { MetricCard } from '../components/MetricCard';
import { ZButton } from '../components/ZButton';
import { obtenerCarteras } from '../api/carterasApi';
import { useAuth } from '../context/AuthContext';

export function GeneradorPage() {
  const { usuario } = useAuth();
  const { data: carteras } = useQuery({ queryKey: ['carteras'], queryFn: obtenerCarteras });

  const totalCarteras = carteras?.length ?? 0;
  const nombreCartera = totalCarteras === 1
    ? carteras![0].nombreCartera
    : `${totalCarteras} carteras`;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: '24px', maxWidth: '900px' }}>
      {/* Encabezado */}
      <div>
        <h1 style={{ fontSize: '16px', fontWeight: 500, color: 'var(--z-text-primary)', marginBottom: '4px' }}>
          Generador de bases
        </h1>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          {usuario?.nombreCompleto} — {totalCarteras > 0 ? nombreCartera : 'Sin carteras asignadas'}
        </p>
      </div>

      {/* Métricas */}
      <div style={{ display: 'grid', gridTemplateColumns: 'repeat(3, 1fr)', gap: '12px' }}>
        <MetricCard label="Titulares totales" value={1284} />
        <MetricCard label="Filtrados" value={347} variant="brand" />
        <MetricCard label="Última descarga" value="Hoy 9:42am" />
      </div>

      {/* Área de tabla (placeholder) */}
      <div style={{
        background: 'var(--z-bg-card)',
        border: '0.5px solid var(--z-border)',
        borderRadius: '8px',
        minHeight: '320px',
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'center',
      }}>
        <p style={{ fontSize: '13px', color: 'var(--z-text-muted)' }}>
          Selecciona filtros para previsualizar los datos
        </p>
      </div>

      {/* Acciones */}
      <div style={{ display: 'flex', gap: '8px' }}>
        <ZButton variant="primary">Descargar Excel</ZButton>
        <ZButton variant="ghost">Limpiar filtros</ZButton>
      </div>
    </div>
  );
}
