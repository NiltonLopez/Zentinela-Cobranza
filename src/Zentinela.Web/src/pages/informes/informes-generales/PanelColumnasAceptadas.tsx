import { BotonCopiable } from '../../../components/PanelNombresValidos';

// Encabezados más comunes que el backend reconoce (AliasColumnaTelefono en
// UsoBaseDatosService). No es la lista completa: se muestran los habituales para
// que el usuario sepa cómo nombrar la columna sin tener que adivinar.
const ALIAS_DESTACADOS = [
  'NumeroTelefono',
  'Telefono',
  'Celular',
  'NumeroCelular',
  'Movil',
  'NumeroMarcado',
  'Destino',
  'MSISDN',
];

export function PanelColumnasAceptadas() {
  return (
    <div style={{
      background: 'var(--z-bg-card)', border: '1px solid var(--z-border)',
      borderRadius: 10, padding: '14px 18px',
    }}>
      <p style={{ fontSize: 13, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
        Columna de teléfono
      </p>
      <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: '4px 0 10px' }}>
        La columna se identifica por el nombre del encabezado, no por su posición. Se aceptan
        columnas adicionales y mayúsculas, tildes o espacios indistintamente.
      </p>
      <div style={{ display: 'flex', flexWrap: 'wrap', gap: 6 }}>
        {ALIAS_DESTACADOS.map((alias) => (
          <BotonCopiable key={alias} texto={alias} />
        ))}
      </div>
    </div>
  );
}
