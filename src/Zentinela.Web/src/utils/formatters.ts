export function formatearMoneda(valor: number): string {
  return new Intl.NumberFormat('es-CO', {
    style: 'currency', currency: 'COP', maximumFractionDigits: 0,
  }).format(valor);
}

export function formatearFecha(iso: string): string {
  return new Date(iso + 'T00:00:00').toLocaleDateString('es-CO', {
    day: 'numeric', month: 'short', year: 'numeric',
  });
}

export function formatearFechaHora(iso: string): string {
  return new Date(iso).toLocaleString('es-CO', {
    day: 'numeric', month: 'short', year: 'numeric',
    hour: '2-digit', minute: '2-digit',
  });
}

// Tiempo transcurrido en estilo "Hace 1 hora, 5 minutos".
// Si la fecha es de hoy muestra horas + minutos combinados; si es de otro día,
// cae a la fecha absoluta (mismo criterio que el dashboard de Asesor).
export function formatearTiempoTranscurrido(iso: string | null): string {
  if (!iso) return 'Sin datos';
  const fecha = new Date(iso);
  const ahora = new Date();
  const esHoy =
    fecha.getFullYear() === ahora.getFullYear() &&
    fecha.getMonth()    === ahora.getMonth()    &&
    fecha.getDate()     === ahora.getDate();

  if (esHoy) {
    const diffMin = Math.floor((ahora.getTime() - fecha.getTime()) / 60_000);
    if (diffMin < 1) return 'Hace menos de un minuto';
    const horas   = Math.floor(diffMin / 60);
    const minutos = diffMin % 60;
    const partes: string[] = [];
    if (horas   > 0) partes.push(`${horas} hora${horas !== 1 ? 's' : ''}`);
    if (minutos > 0) partes.push(`${minutos} minuto${minutos !== 1 ? 's' : ''}`);
    return `Hace ${partes.join(', ')}`;
  }

  return fecha.toLocaleDateString('es-CO', { day: 'numeric', month: 'long', year: 'numeric' });
}
