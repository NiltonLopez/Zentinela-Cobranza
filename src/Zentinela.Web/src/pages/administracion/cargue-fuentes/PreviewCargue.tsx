// Vista previa de un cargue validado (estado PreviewListo): conteos, errores/advertencias
// agrupados sobre TODO el archivo, muestra de filas ya limpias y los botones para
// confirmar o descartar. Nada se ha escrito todavía en la base de datos.
import type { CargueJob, PreviewCargue as Preview } from '../../../types/cargueFuentes';
import { Aviso } from '../../../components/Aviso';
import {
  TH, TD, ChipConteo, ListaAgrupada, btnPrimario, btnSecundario, cajaError,
} from '../historico-virtualidad/compartidos';
import { fechaLegible, num } from './formato';

// Paletas de chip (mismas que usan los hermanos de Administración).
const VERDE = { bg: '#f0fdf4', border: '#86efac', colorNum: '#16a34a', colorLabel: '#15803d' };
const AZUL  = { bg: '#eff6ff', border: '#93c5fd', colorNum: '#1d4ed8', colorLabel: '#1e40af' };
const ROJO  = { bg: '#fef2f2', border: '#fca5a5', colorNum: '#dc2626', colorLabel: '#b91c1c' };
const AMBAR = { bg: '#fffbeb', border: '#fbbf24', colorNum: '#d97706', colorLabel: '#92400e' };
const GRIS  = { bg: 'var(--z-bg-page)', border: 'var(--z-border)', colorNum: 'var(--z-text-primary)', colorLabel: 'var(--z-text-muted)' };

export function PreviewCargue({ job, onConfirmar, onDescartar, confirmando, descartando }: {
  job: CargueJob;
  onConfirmar: () => void;
  onDescartar: () => void;
  confirmando: boolean;
  descartando: boolean;
}) {
  const p = job.preview;
  if (!p) return <div style={cajaError}>El servidor no devolvió la vista previa del cargue.</div>;

  const totalAdvertencias = p.advertencias.reduce((s, a) => s + a.cantidad, 0);
  const faltanColumnas = p.columnasFaltantes.length > 0;
  const puedeConfirmar = job.puedeConfirmar && !faltanColumnas && !confirmando && !descartando;

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 16 }}>
      {faltanColumnas && (
        <div style={cajaError}>
          <strong>Faltan columnas obligatorias:</strong> {p.columnasFaltantes.join(', ')}. No se puede confirmar este archivo.
          <div style={{ marginTop: 6, fontSize: 12 }}>
            Columnas encontradas en el archivo: {p.columnasEncontradas.join(', ') || '(ninguna)'}
          </div>
        </div>
      )}

      <div style={{ display: 'flex', gap: 12, flexWrap: 'wrap' }}>
        <ChipConteo numero={p.totalLeidas}       label="filas leídas"        {...GRIS} />
        <ChipConteo numero={p.excluidas}         label="excluidas"           {...(p.excluidas > 0 ? ROJO : GRIS)} />
        <ChipConteo numero={p.insertadas}        label="se insertarán"       {...VERDE} />
        <ChipConteo numero={p.refrescadas}       label="se refrescarán"      {...AZUL} />
        <ChipConteo numero={p.descartadasVacias} label="vacías descartadas"  {...GRIS} />
        <ChipConteo numero={p.titularesNuevos}   label="titulares nuevos"    {...AZUL} />
        <ChipConteo numero={p.nombresReparados}  label="nombres reparados"   {...AZUL} />
        <ChipConteo numero={p.relacionesNuevas}  label="relaciones nuevas"   {...AZUL} />
        <ChipConteo numero={totalAdvertencias}   label="advertencias"        {...(totalAdvertencias > 0 ? AMBAR : GRIS)} />
      </div>

      <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0, lineHeight: 1.6 }}>
        Candidatas: <strong>{num(p.candidatas)}</strong>
        {' · '}Repetidas en el archivo: <strong>{num(p.duplicadasEnArchivo)}</strong>
        {' · '}Filas vacías que se purgarán de la tabla: <strong>{num(p.vaciasQuePurgara)}</strong>
        {p.filasSinCarteraLider > 0 && <>{' · '}Sin cartera/líder: <strong>{num(p.filasSinCarteraLider)}</strong></>}
      </p>

      <ListaAgrupada titulo="Errores — estas filas no se cargarán" items={p.errores} tono="error" />
      <ListaAgrupada titulo="Advertencias — se cargan con ajustes" items={p.advertencias} tono="aviso" />
      <ListaAgrupada titulo="Información" items={p.info} tono="info" />

      {p.columnasOpcionalesFaltantes.length > 0 && (
        <Aviso tipo="warning">
          El archivo no trae las columnas opcionales <strong>{p.columnasOpcionalesFaltantes.join(', ')}</strong>:
          esos datos quedarán vacíos en las filas cargadas.
        </Aviso>
      )}
      {p.nombresIrrecuperables > 0 && (
        <Aviso tipo="warning">
          <strong>{num(p.nombresIrrecuperables)}</strong> nombre{p.nombresIrrecuperables !== 1 ? 's' : ''} llegaron
          dañados sin forma de repararlos (traen "?"). Suele pasar con un CSV guardado como ANSI: si puedes,
          vuelve a exportarlo como <strong>CSV UTF-8</strong> y súbelo de nuevo.
        </Aviso>
      )}

      <MuestraFilas preview={p} />

      <Aviso tipo="info">
        Las filas quedarán con fecha de consulta <strong>{fechaLegible(p.selloPrevisto)}</strong>.
      </Aviso>

      <div style={{ display: 'flex', gap: 10, flexWrap: 'wrap' }}>
        <button onClick={onConfirmar} disabled={!puedeConfirmar} style={btnPrimario(puedeConfirmar)}>
          {confirmando ? 'Confirmando…' : 'Confirmar carga'}
        </button>
        <button onClick={onDescartar} disabled={confirmando || descartando} style={btnSecundario}>
          {descartando ? 'Descartando…' : 'Descartar'}
        </button>
      </div>
    </div>
  );
}

// Columnas DINÁMICAS: cada fuente trae las suyas, así que el encabezado sale del servidor.
function MuestraFilas({ preview }: { preview: Preview }) {
  if (preview.columnasMuestra.length === 0 || preview.muestra.length === 0) return null;
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>
        Muestra — primeras {num(preview.muestra.length)} filas ya limpias de {num(preview.candidatas)} candidatas
      </p>
      <div style={{ overflow: 'auto', maxHeight: 360, border: '1px solid var(--z-border)', borderRadius: 8 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead style={{ position: 'sticky', top: 0, background: 'var(--z-bg-card)', zIndex: 1 }}>
            <tr>{preview.columnasMuestra.map(c => <th key={c} style={TH}>{c}</th>)}</tr>
          </thead>
          <tbody>
            {preview.muestra.map((fila, i) => (
              <tr key={i}>
                {preview.columnasMuestra.map((c, j) => {
                  const v = fila[j];
                  return (
                    <td key={c} style={v == null ? { ...TD, color: 'var(--z-text-muted)' } : TD}>
                      {v ?? '—'}
                    </td>
                  );
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
