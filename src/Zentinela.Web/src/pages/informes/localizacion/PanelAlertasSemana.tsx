import { useQuery } from '@tanstack/react-query';
import { obtenerAlertasSemanaLocalizacion, problemaLocalizacion } from '../../../api/informesLocalizacionApi';
import { AvatarPersona } from '../../../components/AvatarPersona';
import type { AlertaSemana } from '../../../types/informesLocalizacion';
import { Tarjeta, fmtFecha, fmtNum, fmtPct, type Alcance } from './compartidos';

const ETIQUETA: Record<string, string> = {
  CAIDA_INDICE: 'Índice en caída',
  CARTERA_SIN_CONTACTO: 'Sin contacto con saldo',
  LOTE_SIN_RETORNO: 'Lote sin retorno',
  COLA_SIN_LOTES: 'Cola sin enviar',
  FUENTE_SIN_CARGUE: 'Fuente sin datos nuevos',
};

const colorSeveridad = (s: string) => (s === 'alta' ? '#dc2626' : '#ca8a04');

// ╔══════════════════════════════════════════════════════════════════════════════╗
// ║  ALERTAS DE LA SEMANA (ADR-040, etapa 4)                                     ║
// ║  Se calculan al abrir el Resumen, para el mismo alcance. Solo se muestran    ║
// ║  aquí: no se envían por WhatsApp, correo ni ningún otro canal.              ║
// ╚══════════════════════════════════════════════════════════════════════════════╝
export function PanelAlertasSemana({ alcance }: { alcance: Alcance }) {
  const { data, isLoading, isError, error } = useQuery({
    queryKey: ['loc-alertas-semana', alcance.liderID, alcance.carteraID],
    queryFn: () => obtenerAlertasSemanaLocalizacion({ liderID: alcance.liderID, carteraID: alcance.carteraID }),
    staleTime: 5 * 60 * 1000,
  });

  const cuerpo = () => {
    if (isLoading) return <p style={{ margin: 0, fontSize: 13, color: 'var(--z-text-muted)' }}>Revisando la semana…</p>;
    if (isError || !data) {
      return (
        <p style={{ margin: 0, fontSize: 13, color: 'var(--z-text-muted)' }}>
          {problemaLocalizacion(error)?.mensaje || 'No se pudieron calcular las alertas de la semana.'}
        </p>
      );
    }
    return (
      <div style={{ display: 'flex', flexDirection: 'column', gap: 10 }}>
        {data.alertas.length === 0 ? (
          <p style={{ margin: 0, fontSize: 13, color: 'var(--z-text-muted)' }}>Nada que revisar esta semana para este alcance.</p>
        ) : (
          <ul style={{ margin: 0, padding: 0, listStyle: 'none', display: 'flex', flexDirection: 'column', gap: 8 }}>
            {data.alertas.map((a, i) => <FilaAlerta key={`${a.tipo}-${i}`} a={a} />)}
          </ul>
        )}
        {data.avisos.map(t => (
          <p key={t} style={{ margin: 0, fontSize: 12, color: 'var(--z-text-muted)' }}>ⓘ {t}</p>
        ))}
        <p style={{ margin: 0, fontSize: 11, color: 'var(--z-text-muted)', lineHeight: 1.5 }}>
          Reglas: índice que cae más de {fmtNum(data.umbrales.caidaIndicePuntos)} puntos contra el último corte de la semana
          anterior{data.corteSemanaAnterior ? ` (${fmtFecha(data.corteSemanaAnterior.fechaCorte)})` : ''}; lotes enviados hace más
          de {data.umbrales.diasLoteSinRetorno} días sin cargue de retorno; fuentes Funcionales sin datos nuevos en más
          de {data.umbrales.diasFuenteSinCargue} días; carteras con más de {fmtPct(data.umbrales.sinContactoConSaldoPct, 0)} sin
          contacto y con saldo; líderes con más de {fmtNum(data.umbrales.colaGrande)} búsquedas sugeridas y ningún lote en
          {' '}{data.umbrales.diasSinLotes} días. Relaciones internas (sin externas ni BASE GENERAL). Solo se muestran aquí.
        </p>
      </div>
    );
  };

  return (
    <Tarjeta
      titulo={data ? `Alertas de la semana (${data.alertas.length})` : 'Alertas de la semana'}
      derecha={data && <span style={{ fontSize: 12, color: 'var(--z-text-muted)' }}>Semana del {fmtFecha(data.semana)}</span>}
    >
      {cuerpo()}
    </Tarjeta>
  );
}

function FilaAlerta({ a }: { a: AlertaSemana }) {
  const color = colorSeveridad(a.severidad);
  return (
    <li style={{
      display: 'flex', gap: 10, alignItems: 'center', fontSize: 13, color: 'var(--z-text-primary)',
      padding: '8px 12px', borderRadius: 8,
      background: a.severidad === 'alta' ? 'rgba(220,38,38,0.07)' : 'rgba(202,138,4,0.08)',
      borderLeft: `3px solid ${color}`,
    }}>
      <span style={{ fontSize: 10, fontWeight: 700, textTransform: 'uppercase', color, whiteSpace: 'nowrap', minWidth: 120 }}>
        {ETIQUETA[a.tipo] ?? a.tipo}
      </span>
      {a.nombreLider && a.documentoLider != null && (
        <AvatarPersona documento={a.documentoLider} nombre={a.nombreLider} tam={24} />
      )}
      <span>{a.mensaje}</span>
    </li>
  );
}
