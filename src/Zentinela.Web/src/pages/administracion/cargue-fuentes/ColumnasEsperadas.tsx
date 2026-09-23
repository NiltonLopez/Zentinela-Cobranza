// Columnas que el motor reconoce para la fuente elegida. Se muestran ANTES de subir el
// archivo para que el auxiliar sepa qué encabezados debe traer (se reconocen por nombre o
// alias, sin importar mayúsculas, tildes ni el orden).
import type { FuenteCargue } from '../../../types/cargueFuentes';
import { TH, TD } from '../historico-virtualidad/compartidos';

export function ColumnasEsperadas({ fuente }: { fuente: FuenteCargue }) {
  const obligatorias = fuente.columnasEsperadas.filter(c => c.obligatoria).length;
  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 8 }}>
      <div>
        <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
          Columnas esperadas — {fuente.etiqueta}
        </p>
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: '2px 0 0', lineHeight: 1.5 }}>
          {obligatorias} obligatoria{obligatorias !== 1 ? 's' : ''} de {fuente.columnasEsperadas.length}.
          Se identifican por el encabezado o cualquiera de sus alias, sin importar mayúsculas,
          tildes ni el orden; las columnas adicionales se ignoran. Destino: <code>{fuente.tablaDestino}</code>.
        </p>
        {fuente.notas && (
          <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: '4px 0 0', lineHeight: 1.5 }}>{fuente.notas}</p>
        )}
      </div>
      <div style={{ overflowX: 'auto', border: '1px solid var(--z-border)', borderRadius: 8 }}>
        <table style={{ width: '100%', borderCollapse: 'collapse' }}>
          <thead>
            <tr>
              <th style={TH}>Columna</th>
              <th style={TH}>Tipo</th>
              <th style={TH}>Obligatoria</th>
              <th style={TH}>También se reconoce como</th>
            </tr>
          </thead>
          <tbody>
            {fuente.columnasEsperadas.map(c => (
              <tr key={c.nombre}>
                <td style={{ ...TD, whiteSpace: 'normal' }}>
                  <span style={{ fontWeight: 600 }}>{c.nombre}</span>
                  {c.descripcion && (
                    <div style={{ fontSize: 11, color: 'var(--z-text-muted)', marginTop: 2 }}>{c.descripcion}</div>
                  )}
                </td>
                <td style={{ ...TD, color: 'var(--z-text-muted)' }}>{c.tipo}</td>
                <td style={TD}>
                  {c.obligatoria
                    ? <span style={{ color: '#dc2626', fontWeight: 600 }}>Sí</span>
                    : <span style={{ color: 'var(--z-text-muted)' }}>No</span>}
                </td>
                <td style={{ ...TD, whiteSpace: 'normal', fontFamily: 'monospace', fontSize: 12, color: 'var(--z-text-muted)' }}>
                  {c.alias.length > 0 ? c.alias.join(', ') : '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
