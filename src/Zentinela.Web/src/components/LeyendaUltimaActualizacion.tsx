import { useQuery } from '@tanstack/react-query';

export function formatearUltimaActualizacion(iso: string | null): string {
  if (!iso) return 'Sin registros aún';
  const fecha = new Date(iso);
  const ahora = new Date();
  const esHoy =
    fecha.getFullYear() === ahora.getFullYear() &&
    fecha.getMonth()    === ahora.getMonth()    &&
    fecha.getDate()     === ahora.getDate();

  if (esHoy) {
    const diffMin = Math.floor((ahora.getTime() - fecha.getTime()) / 60_000);
    if (diffMin < 1)  return 'hace menos de un minuto';
    if (diffMin < 60) return `hace ${diffMin} minuto${diffMin !== 1 ? 's' : ''}`;
    const diffH = Math.floor(diffMin / 60);
    return `hace ${diffH} hora${diffH !== 1 ? 's' : ''}`;
  }

  return fecha.toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' });
}

interface Props {
  carteraID: number | '';
  queryKey:  string;
  queryFn:   (id: number) => Promise<{ fechaActualizacion: string | null }>;
}

export function LeyendaUltimaActualizacion({ carteraID, queryKey, queryFn }: Props) {
  const id = Number(carteraID);

  const { data, isLoading } = useQuery({
    queryKey:  [queryKey, id],
    queryFn:   () => queryFn(id),
    enabled:   id > 0,
    staleTime: 30_000,
  });

  if (!id || isLoading) return null;

  const texto    = formatearUltimaActualizacion(data?.fechaActualizacion ?? null);
  const sinDatos = !data?.fechaActualizacion;

  return (
    <span style={{
      fontSize: '12px',
      color:      sinDatos ? 'var(--z-text-muted)' : '#15803d',
      background: sinDatos ? 'var(--z-bg-page)'    : '#f0fdf4',
      border:     `0.5px solid ${sinDatos ? 'var(--z-border)' : '#86efac'}`,
      borderRadius: '6px',
      padding:    '3px 10px',
      whiteSpace: 'nowrap',
    }}>
      {sinDatos ? 'Sin registros aún' : `Actualizado ${texto}`}
    </span>
  );
}
