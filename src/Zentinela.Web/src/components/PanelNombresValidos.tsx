import { useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { listarRelaciones } from '../api/gestionCarterasApi';

// ── Botón copiable ────────────────────────────────────────────────────────────
//
// Copia el nombre al portapapeles y muestra un aviso "Copiado!" flotante y
// minimalista sobre el botón (se oculta solo). No es texto seleccionable —
// es un botón, el gesto esperado es el clic, no la selección manual.
function BotonCopiable({ texto }: { texto: string }) {
  const [copiado, setCopiado] = useState(false);

  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(texto);
    } catch {
      const ta = document.createElement('textarea');
      ta.value = texto;
      ta.style.position = 'fixed';
      ta.style.opacity = '0';
      document.body.appendChild(ta);
      ta.select();
      document.execCommand('copy');
      document.body.removeChild(ta);
    }
    setCopiado(true);
    window.setTimeout(() => setCopiado(false), 1100);
  };

  return (
    <button
      onClick={copiar}
      style={{
        position: 'relative', userSelect: 'none', cursor: 'pointer',
        padding: '5px 10px', borderRadius: 6, fontSize: 12.5, fontFamily: 'monospace',
        border: `0.5px solid ${copiado ? 'var(--z-brand)' : 'var(--z-border)'}`,
        background: copiado ? 'var(--z-brand)' : 'var(--z-bg-page)',
        color: copiado ? '#fff' : 'var(--z-text-primary)',
        transition: 'background 0.15s, border-color 0.15s, color 0.15s',
      }}
    >
      {texto}
      {copiado && (
        <span style={{
          position: 'absolute', bottom: '100%', left: '50%', transform: 'translateX(-50%)',
          marginBottom: 5, background: '#111827', color: '#fff', fontFamily: 'inherit',
          fontSize: 11, fontWeight: 600, padding: '3px 8px', borderRadius: 5,
          whiteSpace: 'nowrap', pointerEvents: 'none', boxShadow: '0 2px 6px rgba(0,0,0,0.2)',
        }}>
          Copiado!
        </span>
      )}
    </button>
  );
}

// ── Lista independiente (Carteras o Líderes) ─────────────────────────────────
function ListaCopiable({ titulo, nombres }: { titulo: string; nombres: string[] }) {
  const [busqueda, setBusqueda] = useState('');
  const filtrados = useMemo(() => {
    const q = busqueda.trim().toLowerCase();
    return q ? nombres.filter(n => n.toLowerCase().includes(q)) : nombres;
  }, [nombres, busqueda]);

  return (
    <div style={{ flex: '1 1 260px', display: 'flex', flexDirection: 'column', gap: 8, minWidth: 240 }}>
      <div style={{ display: 'flex', alignItems: 'baseline', justifyContent: 'space-between', gap: 8 }}>
        <p style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-primary)', textTransform: 'uppercase', letterSpacing: '0.04em', margin: 0 }}>
          {titulo}
        </p>
        <span style={{ fontSize: 11, color: 'var(--z-text-muted)' }}>{filtrados.length} de {nombres.length}</span>
      </div>
      <input
        type="text" placeholder={`Buscar ${titulo.toLowerCase()}...`} value={busqueda}
        onChange={e => setBusqueda(e.target.value)}
        style={{
          padding: '6px 9px', borderRadius: 6, border: '0.5px solid var(--z-border)',
          background: 'var(--z-bg-card)', color: 'var(--z-text-primary)', fontSize: 12.5,
        }}
      />
      <div style={{
        maxHeight: 220, overflowY: 'auto', border: '0.5px solid var(--z-border)', borderRadius: 6,
        background: 'var(--z-bg-card)', padding: 10,
        display: 'flex', flexWrap: 'wrap', gap: 6, alignContent: 'flex-start',
      }}>
        {filtrados.length === 0 ? (
          <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>Sin coincidencias.</p>
        ) : (
          filtrados.map(n => <BotonCopiable key={n} texto={n} />)
        )}
      </div>
    </div>
  );
}

// ── Panel de referencia: nombres exactos de Cartera + Líder ──────────────────
//
// El mapeo del Excel es por NOMBRE (no por selector), y el par debe existir y
// estar activo en Lider_Cartera o la fila queda marcada como error (validado
// en el backend). Reusa /api/gestion-carteras/relaciones (roles Admin/AuxiliarSistemas).
// Cartera y Líder se muestran como dos listas independientes (no emparejadas por
// fila) — cada nombre es un botón: un clic lo copia al portapapeles.
export function PanelNombresValidos() {
  const { data: relaciones = [], isLoading } = useQuery({ queryKey: ['relaciones-lider-cartera'], queryFn: listarRelaciones });

  const activas = useMemo(() => relaciones.filter(r => r.activo), [relaciones]);
  const carteras = useMemo(
    () => Array.from(new Set(activas.map(r => r.nombreCartera))).sort((a, b) => a.localeCompare(b, 'es-CO')),
    [activas],
  );
  const lideres = useMemo(
    () => Array.from(new Set(activas.map(r => r.nombreLider))).sort((a, b) => a.localeCompare(b, 'es-CO')),
    [activas],
  );

  return (
    <div style={{
      background: 'var(--z-bg-page)', border: '0.5px solid var(--z-border)', borderRadius: 8,
      padding: 16, display: 'flex', flexDirection: 'column', gap: 10,
    }}>
      <p style={{ fontSize: 11, fontWeight: 600, color: 'var(--z-text-primary)', textTransform: 'uppercase', letterSpacing: '0.04em', margin: 0 }}>
        Nombres exactos de Cartera y Líder
      </p>
      <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>
        Haz clic para copiar el nombre <strong>tal cual</strong> y pégalo en la columna correspondiente del Excel.
        El par Cartera-Líder debe existir y estar activo — si no coincide exacto, la fila queda marcada como error.
      </p>
      {isLoading ? (
        <p style={{ fontSize: 12, color: 'var(--z-text-muted)', margin: 0 }}>Cargando...</p>
      ) : (
        <div style={{ display: 'flex', gap: 16, flexWrap: 'wrap' }}>
          <ListaCopiable titulo="Carteras" nombres={carteras} />
          <ListaCopiable titulo="Líderes" nombres={lideres} />
        </div>
      )}
    </div>
  );
}
