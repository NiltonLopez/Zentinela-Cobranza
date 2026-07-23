# Design System — Zentinela

El sistema de diseño está implementado mediante variables CSS globales (`var(--z-*)`) referenciadas directamente en los estilos inline de los componentes React.

---

## Tokens de color

Los siguientes tokens son los que se referencian en el código fuente de los componentes. Sus valores se definen en la hoja de estilos global del proyecto.

| Token | Uso en el código |
|---|---|
| `var(--z-brand)` | Color principal de marca: botones primarios, tab activa, indicador de sidebar activo, borde inferior de tab activa, color de texto activo |
| `var(--z-brand-light)` | Fondo de ítem de sidebar activo |
| `var(--z-bg-page)` | Fondo del área de contenido principal (`<main>`) y hover de sidebar inactivo |
| `var(--z-bg-card)` | Fondo de la Navbar, Sidebar y `MetricCard` |
| `var(--z-border)` | Bordes de separación (Navbar, Sidebar, MetricCard), indicador de sidebar inactivo, borde del botón ghost |
| `var(--z-text-primary)` | Texto principal: nombre de la app en Navbar, valor numérico de MetricCard (variante default), iniciales del avatar |
| `var(--z-text-muted)` | Texto secundario: tabs inactivas, ítems de sidebar inactivos, label de MetricCard, botón "Salir", botón ghost |

### Colores hardcodeados en componentes

Estos valores aparecen directamente en el código fuente como literales:

| Valor | Dónde se usa |
|---|---|
| `#fff` | Color de texto del botón primario (`ZButton` variante `primary`) |
| `#E2E2DF` | Fondo del avatar de iniciales en la Navbar |
| `#e53e1a` | Color del botón "Salir" en hover |

### Paleta de módulo Informes (Virtualidad / Recaudo)

Los módulos de informes definen una paleta local (`const C`) en cada página. La paleta estándar para Virtualidad es:

| Clave | Hex | Uso |
|---|---|---|
| `brand` | `#F54927` | Naranja principal Zentinela — botones primarios, SelectorPeriodo activo, LoadingRow, tab Pagos vs Impacto |
| `brandLight` | `#FEF0EC` | Fondo de hover en DropZone, fondo de LoadingRow |
| `success` | `#16a34a` | KPI válidos, banners de éxito, barras "contestaron" |
| `warning` | `#d97706` | AlertPanel, alertas de umbral superado |
| `danger` | `#dc2626` | KPI errores, filas inválidas, ErrorPanel |
| `gray` | `#64748b` | Texto secundario, ejes de gráficos, botones secundarios |
| `mensajes` | `#7c3aed` | Tab Mensajería — headers, KPIs, gráficos |
| `ivr` | `#0891b2` | Tab IVR — headers, KPIs, sub-tabs, gráficos |
| `llamadas` | `#059669` | Tab Llamadas Perdidas — headers, KPIs, gráficos |
| `entrantes` | `#ea580c` | Tab Llamadas Entrantes — headers, KPIs, gráficos |
| `impacto` | `#F54927` | Tab Pagos vs Impacto (igual a brand) |

---

## Tipografía

Los tamaños de fuente utilizados en los componentes son:

| Tamaño | Dónde se usa |
|---|---|
| `11px` | Label de `MetricCard`, iniciales del avatar en Navbar |
| `12px` | Botón "Salir" en Navbar |
| `13px` | Tabs de Navbar, ítems de Sidebar, `ZButton`, textos de ítem sidebar |
| `15px` | Nombre "Zentinela" en el logo de la Navbar |
| `22px` | Valor numérico de `MetricCard` |

Los pesos de fuente usados son `400` (normal), `500` (medium) y `600` (semibold).

El formato de números en `MetricCard` usa `toLocaleString('es-CO')` para separadores de miles en formato colombiano.

---

## Componentes base

### ZButton

Ubicación: `src/Zentinela.Web/src/components/ZButton.tsx`

Botón base del sistema. Extiende todos los atributos nativos de `<button>`.

#### Props

| Prop | Tipo | Default | Descripción |
|---|---|---|---|
| `variant` | `'primary' \| 'ghost'` | `'primary'` | Estilo visual del botón |
| `children` | `ReactNode` | — | Contenido del botón |
| `style` | `React.CSSProperties` | — | Estilos adicionales (se mezclan con los del variant) |
| `...props` | `ButtonHTMLAttributes` | — | Todos los atributos HTML nativos de button |

#### Variantes

| Variant | Fondo | Texto | Borde |
|---|---|---|---|
| `primary` | `var(--z-brand)` | `#fff` | ninguno |
| `ghost` | `transparent` | `var(--z-text-muted)` | `1px solid var(--z-border)` |

El hover aplica `opacity: 0.85` en ambas variantes.

Dimensiones base: `padding: 8px 16px`, `border-radius: 6px`, `font-size: 13px`, `font-weight: 500`.

#### Ejemplo de uso

```tsx
import { ZButton } from '../components/ZButton';

// Botón primario
<ZButton onClick={handleDescargar}>Descargar Excel</ZButton>

// Botón ghost
<ZButton variant="ghost" onClick={handleCancelar}>Cancelar</ZButton>

// Con ícono (children puede ser cualquier ReactNode)
<ZButton>
  <DownloadIcon />
  Exportar
</ZButton>

// Deshabilitado
<ZButton disabled>Procesando...</ZButton>
```

---

### MetricCard

Ubicación: `src/Zentinela.Web/src/components/MetricCard.tsx`

Tarjeta de indicador numérico. Muestra una etiqueta en mayúsculas y un valor prominente.

#### Props

| Prop | Tipo | Default | Descripción |
|---|---|---|---|
| `label` | `string` | — | Texto de la etiqueta (se muestra en mayúsculas automáticamente) |
| `value` | `string \| number` | — | Valor a mostrar. Si es `number`, se formatea con `toLocaleString('es-CO')` |
| `variant` | `'default' \| 'brand'` | `'default'` | Color del valor: `default` usa `var(--z-text-primary)`, `brand` usa `var(--z-brand)` |

#### Dimensiones

- Fondo: `var(--z-bg-card)`, borde `0.5px solid var(--z-border)`, radio `8px`
- Padding: `16px 20px`
- Label: `11px`, `font-weight: 500`, uppercase, `letter-spacing: 0.04em`
- Valor: `22px`, `font-weight: 600`, `letter-spacing: -0.02em`

#### Ejemplo de uso

```tsx
import { MetricCard } from '../components/MetricCard';

// Variante default
<MetricCard label="Total titulares" value={1520} />

// Variante brand (valor destacado en color de marca)
<MetricCard label="Carteras activas" value={8} variant="brand" />

// Con valor string
<MetricCard label="Último proceso" value="Hoy, 09:32" />
```

---

### TarjetaCartera

No existe como componente independiente en el código fuente actual. Las carteras se renderizan en `CarterasPage` mediante la lista de `CarteraDto` obtenida del endpoint `GET /api/carteras`.

---

## Layout

### AppLayout

Ubicación: `src/Zentinela.Web/src/components/layout/AppLayout.tsx`

Estructura de página para todas las rutas protegidas. Envuelve el contenido en un layout de dos zonas: barra superior (Navbar) y área inferior dividida en sidebar + contenido.

#### Estructura visual

```
┌─────────────────────────────────────────────┐
│                  Navbar (52px)              │
├──────────┬──────────────────────────────────┤
│          │                                  │
│ Sidebar  │        <Outlet />                │
│ (180px)  │   padding: 28px 32px             │
│          │   background: var(--z-bg-page)   │
│          │                                  │
└──────────┴──────────────────────────────────┘
```

- El layout ocupa `100vh` con `overflow: hidden`.
- El área de contenido (`<main>`) tiene scroll vertical propio (`overflowY: auto`).
- La tab activa se sincroniza con la ruta actual mediante `pathToTab()`:

| Ruta | Tab activa |
|---|---|
| `/carteras` o subrutas | `carteras` |
| `/kpis` o subrutas | `reportes` |
| Cualquier otra | `bases` |

---

### Navbar

Ubicación: `src/Zentinela.Web/src/components/layout/Navbar.tsx`

#### Dimensiones

- Altura: `52px`
- Fondo: `var(--z-bg-card)`
- Borde inferior: `0.5px solid var(--z-border)`
- z-index: `10`

#### Elementos (de izquierda a derecha)

1. **Logo**: icono shield (SVG 16x16) + texto "Zentinela". Ancho mínimo: `160px`.
2. **Tabs de navegación**: centradas absolutamente. Cada tab tiene altura `100%`, padding `0 14px`. La tab activa muestra `border-bottom: 2px solid var(--z-brand)`.
3. **Controles de usuario**: alineados a la derecha. Botón "Salir" + avatar de iniciales (32x32px, fondo `#E2E2DF`, borde circular).

#### Tabs disponibles y visibilidad por rol

| Tab | Key | Roles que la ven |
|---|---|---|
| Bases | `bases` | Admin, Supervisor, Líder, Analista |
| Carteras | `carteras` | Admin, Supervisor, Líder |
| Reportes | `reportes` | Admin, Supervisor, Analista |

Si el rol del usuario no está en el mapa `TABS_POR_ROL`, se muestran todas las tabs (comportamiento por defecto del operador `?? true`).

#### Navegación al hacer clic en tabs

| Tab clicada | Ruta de destino |
|---|---|
| `bases` | `/bases` |
| `carteras` | `/carteras` |
| `reportes` | `/kpis` |

---

### Sidebar

Ubicación: `src/Zentinela.Web/src/components/layout/Sidebar.tsx`

#### Dimensiones

- Ancho: `180px` (fijo, `flex-shrink: 0`)
- Fondo: `var(--z-bg-card)`
- Borde derecho: `0.5px solid var(--z-border)`
- Padding interno: `16px 8px`

#### Ítems por tab

| Tab activa | Ítem | Ruta |
|---|---|---|
| `bases` | Generar base | `/bases` |
| `bases` | Historial | `/historial` |
| `carteras` | Mis carteras | `/carteras` |
| `reportes` | KPIs | `/kpis` |

Cada ítem tiene un indicador visual circular (6x6px). Activo: `var(--z-brand)`. Inactivo: `var(--z-border)`. El fondo activo es `var(--z-brand-light)` con borde-radius `6px`.

---

## Tablas de desglose (dashboards)

Patrón usado por `TablaLideres`, `TablaCarteras` y `TablaAsesores` en `DashboardDirectorPage.tsx` (y equivalente en `DashboardLiderPage.tsx`). Las tres deben mantenerse alineadas — si se agrega una tabla de desglose nueva, seguir exactamente este patrón en vez del más simple/antiguo (fondo `var(--z-bg-page)` en el header, sin acento superior, sin hover), que quedó desactualizado.

### Estructura

```tsx
const [filaHover, setFilaHover] = useState<number | null>(null);

<table style={{ width: '100%', borderCollapse: 'collapse' }}>
  <thead>
    <tr style={{ background: 'var(--z-brand-light)' }}>
      <ThOrdenable label="..." sortKey="..." sort={sort} onSort={toggle} />
      {/* ... */}
      <ThOrdenable label="" align="center" /> {/* columna de acción, si aplica */}
    </tr>
  </thead>
  <tbody>
    {items.map((f, i) => (
      <tr
        key={f.id}
        onMouseEnter={() => setFilaHover(f.id)}
        onMouseLeave={() => setFilaHover(prev => (prev === f.id ? null : prev))}
        style={{
          borderTop: i === 0 ? '2px solid var(--z-brand)' : undefined,
          borderBottom: '0.5px solid var(--z-border)',
          background: filaHover === f.id ? 'var(--z-bg-page)' : 'transparent',
          transition: 'background 0.12s ease',
        }}
      >
        <td style={{ padding: '12px 14px', fontSize: '13px', color: 'var(--z-text-primary)', fontWeight: 500 }}>...</td>
        {/* columnas numéricas: agregar fontVariantNumeric: 'tabular-nums' */}
      </tr>
    ))}
  </tbody>
</table>
```

### Reglas

| Elemento | Valor |
|---|---|
| Header (`thead tr`) | `background: var(--z-brand-light)` |
| Primera fila | `borderTop: '2px solid var(--z-brand)'` (acento, solo `i === 0`) |
| Filas | `borderBottom: '0.5px solid var(--z-border)'`, hover con `background: var(--z-bg-page)` y `transition: 'background 0.12s ease'` |
| Padding de celda | `12px 14px` (no `11px`, que es el valor antiguo) |
| Columnas numéricas | `fontVariantNumeric: 'tabular-nums'` para que los dígitos no salten de ancho al reordenar |
| Botón "Ver detalle" (si aplica) | Mismo hover que la fila: `background` pasa a `var(--z-bg-card)` y agrega `boxShadow: '0 1px 3px rgba(0,0,0,0.08)'` cuando `filaHover === f.id` |
| Estado vacío | `<div style={{ padding: '32px', textAlign: 'center', fontSize: '13px', color: 'var(--z-text-muted)' }}>Sin datos de ... para este período.</div>` |

---

## Barra desplazable (ticker)

Patrón compartido por las tres barras de "frases"/"destacados" de los dashboards: `BarraFraseLider` (Líder), `BarraDestacadosLider` (Director) y `BarraFrasesMotivacionales` (Asesor). Utilidad compartida: `MARGEN_BARRA_COMPLETA` en `src/Zentinela.Web/src/utils/layout.ts`.

### Reglas

| Elemento | Valor |
|---|---|
| Fondo | `var(--z-brand)` |
| Texto | `#fff`, `15px`, `font-weight: 600` |
| Separador entre ítems | `1px solid rgba(255,255,255,0.3)` |
| Padding del contenedor | `8px 0` |
| Padding por ítem | `0 24px` |
| Márgenes (ancho completo) | `marginLeft`/`marginRight: MARGEN_BARRA_COMPLETA`, `marginTop: '-22px'`, `marginBottom: '24px'` — **no** usar `-32px` fijo (no cubre el espacio de centrado en pantallas >1440px, ver comentario en `layout.ts`) |
| Animación | `@keyframes` con `translateX(0%)` → `translateX(-50%)`, `animation-timing-function: linear`, `animation-iteration-count: infinite`; la pista se duplica (`[...items, ...items]`) para loop continuo sin salto |
| Pausa en hover | `onMouseEnter`/`onMouseLeave` sobre un state `pausado`, aplicado a `animationPlayState` |
| Iconos (si el ítem lleva uno) | SVG propio `viewBox="0 0 24 24"`, prop `size` (20px en los tickers), `color` default `#fff` — no usar emojis |

---

## Rutas y navegación

> **Nota de vigencia:** esta sección (rutas, tabs de Navbar, ítems de Sidebar y roles) describe una versión anterior del sistema. Los roles actuales son `Admin`, `Líder`, `Director`, `Asesor`, `Líder Externo`, `Asesor Externo` y `Auxiliar de Sistemas` (no `Supervisor`/`Analista`), y las rutas reales viven bajo `/home`, `/cartera/*`, `/informes/*`, `/administracion/*` y `/consultas/*` (ver `App.tsx`). Queda pendiente reescribir esta sección con el mapa de rutas y roles actual.

### Mapa de rutas completo

| Ruta | Componente | Protegida | Descripción |
|---|---|---|---|
| `/login` | `LoginPage` | No | Formulario de autenticación |
| `/sin-permiso` | `SinPermisoPage` | No | Página de acceso denegado |
| `/bases` | `GeneradorPage` | Sí | Generador de bases Excel |
| `/historial` | `HistorialPage` | Sí | Historial de bases generadas |
| `/carteras` | `CarterasPage` | Sí | Listado de carteras del usuario |
| `/kpis` | `KPIsPage` | Sí | Dashboard de KPIs |
| `*` (catch-all) | `Navigate` | — | Redirige a `/bases` |

Las rutas protegidas están envueltas en `<RutaProtegida>` y renderizan bajo `<AppLayout>` mediante el `<Outlet />` de React Router.

### Navegación por rol

| Rol | Tabs visibles | Ítems de sidebar disponibles |
|---|---|---|
| `Admin` | Bases, Carteras, Reportes | Generar base, Historial, Mis carteras, KPIs |
| `Supervisor` | Bases, Carteras, Reportes | Generar base, Historial, Mis carteras, KPIs |
| `Líder` | Bases, Carteras | Generar base, Historial, Mis carteras |
| `Analista` | Bases, Reportes | Generar base, Historial, KPIs |
| `Asesor` | Todas (sin restricción definida) | Todos los ítems |

Nota: La visibilidad de los ítems del sidebar es indirecta — depende de qué tab está activa, y la visibilidad de tabs está controlada en la Navbar por `TABS_POR_ROL`.

---

## Tipos TypeScript del sistema de UI

### types/ui.ts

```typescript
export type NavTab = 'bases' | 'carteras' | 'reportes';
export type SidebarItem = 'generar-base' | 'historial' | 'mis-carteras' | 'kpis';
```

### types/auth.ts

```typescript
export interface LoginRequest { email: string; password: string; }
export interface LoginResponse { token: string; nombreCompleto: string; rol: string; liderID: number | null; expiracion: string; }
export interface Usuario { email: string; nombreCompleto: string; rol: string; liderID: number | null; }
export type Rol = 'Admin' | 'Supervisor' | 'Líder' | 'Analista' | 'Asesor';
```

### types/carteras.ts

```typescript
export interface CarteraDto { id: number; nombreCartera: string; }
```

---

## Componentes de módulos de informes

Los módulos de Informes (Recaudo, Virtualidad) definen componentes internos reutilizables dentro de cada página. No están en `/components/` globales — viven en el archivo de la página. El patrón estándar es:

### KpiCard

```tsx
function KpiCard({ label, value, color = C.brand }: { label: string; value: string | number; color?: string })
```

Tarjeta de métrica con borde superior de 3px del color asignado. `value` puede ser string o número. `label` se muestra en uppercase con letter-spacing.

### DropZone

```tsx
function DropZone({ onFile, accept = '.xlsx', label }: { onFile: (f: File) => void; accept?: string; label?: string })
```

Zona de carga de archivos con hover interactivo (borde naranja + fondo `brandLight`). Usa `useRef<HTMLInputElement>` internamente.

### BtnPrimary / BtnSecondary

```tsx
function BtnPrimary({ children, onClick, disabled, color = C.brand }: ...)
function BtnSecondary({ children, onClick }: ...)
```

Botones locales del módulo. `BtnPrimary` admite un `color` para mantener el color del tab activo. `BtnSecondary` es siempre gris con borde.

### SelectorPeriodo

```tsx
function SelectorPeriodo({ onChange }: { onChange: (desde: string, hasta: string) => void })
```

Selector de período con chips de preset (Hoy, Este mes, Mes pasado, 7d, 30d, Personalizado). El chip activo usa el naranja principal. Incluye inputs de fecha cuando se selecciona "Personalizado".

### SuccessBanner

```tsx
function SuccessBanner({ mensaje, onNuevaCarga, labelBtn }: ...)
```

Banner de confirmación con borde izquierdo verde, ícono ✓ y botón de acción.

### LoadingRow

```tsx
function LoadingRow({ label }: { label: string })
```

Fila de carga con fondo `brandLight` y símbolo ⟳. Reemplaza el `<p>` plano de carga.

### ErrorPanel

```tsx
function ErrorPanel({ mensaje }: { mensaje: string })
```

Panel de error con borde izquierdo rojo, fondo `#fef2f2`.

### AlertPanel

```tsx
function AlertPanel({ title, children }: { title: string; children: React.ReactNode })
```

Panel de alerta con borde izquierdo amarillo (`#d97706`) y fondo ámbar suave. Para alertas de umbral (>4 mensajes, >5 IVR, etc.).

### SectionDivider

```tsx
function SectionDivider({ title, color = C.brand }: { title: string; color?: string })
```

Separador de sección con línea horizontal + barra vertical de color + título bold. Divide la sección de carga de la sección de informe dentro de un mismo tab.

---

## Patrones de hover en botones inline

Cuando un botón **no** se construye con `<ZButton>` (que ya trae su `opacity: 0.85` de hover), la página suele usar estilos inline con `React.CSSProperties`. Para esos casos, este es el conjunto canónico de patrones — referenciado en `CreadorZentinelaPage.tsx` (Card 2 + Card 3). Aplicar literal cuando se agreguen botones nuevos a otras páginas para mantener consistencia.

### Mecánica común

- Estado: `const [botonHover, setBotonHover] = useState<string | null>(null);` — un único state para todos los botones de la página, con clave string única por botón (`'cols-todas'`, `'export-csv'`, etc.).
- Handlers:
  ```tsx
  onMouseEnter={() => setBotonHover('clave-unica')}
  onMouseLeave={() => setBotonHover(prev => prev === 'clave-unica' ? null : prev)}
  ```
  El `prev === clave ? null : prev` evita carreras cuando el cursor cruza dos botones rápido.
- Transición: siempre `transition: '<props> 0.15s ease'`. Animar solo las propiedades que cambian (`background`, `box-shadow`, `filter`) — no `all`, que dispara reflows innecesarios.
- Disabled: el hover **no se aplica** cuando el botón está disabled o en un estado de `isPending`. Calcular un boolean `<accion>Activo` arriba y gateaer ambos el `disabled` y el style condicional con él.

### Variante 1 — Botón sólido de acción primaria (ej: "Ver preview")

Fondo `var(--z-brand)`, texto blanco. Hover oscurece con `filter: brightness(0.92)` + sombra sutil. Usa `filter` en vez de cambiar el color para que sirva con cualquier brand color sin tener que conocer su tono exacto.

```tsx
<button
  onClick={handler}
  disabled={!activo}
  onMouseEnter={() => setBotonHover('accion')}
  onMouseLeave={() => setBotonHover(prev => prev === 'accion' ? null : prev)}
  style={{
    padding: '8px 20px', borderRadius: '6px', border: 'none',
    background: activo ? 'var(--z-brand)' : 'var(--z-border)',
    color: 'white', fontSize: '13px', fontWeight: 600,
    cursor: activo ? 'pointer' : 'not-allowed',
    filter:    activo && botonHover === 'accion' ? 'brightness(0.92)' : 'none',
    boxShadow: activo && botonHover === 'accion' ? '0 2px 6px rgba(0,0,0,0.12)' : 'none',
    transition: 'filter 0.15s ease, box-shadow 0.15s ease',
  }}
>
  Acción
</button>
```

### Variante 2 — Botón sólido verde (descarga / éxito)

Fondo `#16a34a` (verde Tailwind 600). Hover baja un escalón a `#15803d` (Tailwind 700) + sombra verde tintada. Más fuerte que la variante 1 porque la acción es decisiva (descargar archivo).

```tsx
style={{
  background: activo ? (hover ? '#15803d' : '#16a34a') : 'var(--z-border)',
  boxShadow: hover ? '0 2px 6px rgba(22,163,74,0.30)' : 'none',
  transition: 'background 0.15s ease, box-shadow 0.15s ease',
  // ...resto igual a variante 1, sin border
}}
```

### Variante 3 — Botón outline verde (acción secundaria con relación a una primaria)

Borde `#16a34a` con fondo transparente. Hover **rellena** con verde claro al 10% (`rgba(22,163,74,0.10)`) + sombra verde más suave. Patrón pensado para parejas "secundaria + primaria" donde la primaria es la variante 2 (ej: "Descargar CSV" + "Descargar Excel").

```tsx
style={{
  border: '1px solid #16a34a',
  background: hover ? 'rgba(22, 163, 74, 0.10)' : 'transparent',
  color: activo ? '#15803d' : 'var(--z-text-muted)',
  boxShadow: hover ? '0 2px 6px rgba(22,163,74,0.18)' : 'none',
  transition: 'background 0.15s ease, box-shadow 0.15s ease',
}}
```

### Variante 4 — Chips de preset / utilidad (outline gris)

Borde `var(--z-border)`, fondo transparente, texto `var(--z-text-muted)`. Hover oscurece el borde a `var(--z-text-muted)`, fondo a `var(--z-bg-surface)`, texto a `var(--z-text-primary)`. Para los presets de "Todas / Solo X / Solo Y / Ninguna" del Creador o cualquier tira similar.

```tsx
const presetStyle = (key: string): React.CSSProperties => ({
  padding: '4px 10px', fontSize: '11px', borderRadius: '4px',
  border: `1px solid ${botonHover === key ? 'var(--z-text-muted)' : 'var(--z-border)'}`,
  background: botonHover === key ? 'var(--z-bg-surface)' : 'transparent',
  color:      botonHover === key ? 'var(--z-text-primary)' : 'var(--z-text-muted)',
  cursor: 'pointer', transition: 'all 0.15s ease',
});
```

(En este caso `transition: 'all'` es aceptable porque cambia 3 propiedades simultáneas.)

### Reglas

- Una sola variable de estado por página (`botonHover: string | null`), no un boolean por botón.
- Las claves deben ser globales-de-la-página, prefijo por sección si hace falta (`'cols-todas'`, `'export-csv'`).
- Nunca aplicar hover a un botón disabled / loading — el cursor `not-allowed` ya comunica el estado.
- Sombras siempre como `box-shadow: 0 2px 6px ...` con tinte coherente con el color del botón. No usar `0 4px 12px` o sombras más fuertes (rompen la jerarquía visual con cards y modales).
- `transition` siempre `0.15s ease`. 0.1s se siente abrupto, 0.2s+ ya empieza a sentirse lento en hovers.

---

## Convenciones de código frontend

1. **Estilos**: se usan estilos inline con objetos `React.CSSProperties` y variables CSS `var(--z-*)`. No se usa Tailwind ni CSS Modules.
2. **Componentes**: funciones con export nombrado (`export function NombreComponente`). No se usa `export default` en componentes.
3. **Tipos**: definidos en `/src/types/`. Se usan `interface` para objetos y `type` para uniones discriminadas.
4. **Imports de tipos**: se usa `import type` cuando solo se importa una interfaz o tipo, siguiendo las convenciones de TypeScript strict.
5. **Nombrado**: componentes en PascalCase, funciones/variables en camelCase, rutas en kebab-case.
6. **Estado del servidor**: TanStack Query para todas las llamadas al API. No se usa `useState` + `fetch` directo para datos del servidor.
7. **Autenticación**: gestionada mediante `AuthContext` y el hook `useAuth()`. El token JWT se almacena en contexto de React.

---

## Estándar de cabecera de sub-módulo

**Referencia canónica:** `InformesRecaudoPage.tsx`

Todos los sub-módulos del aplicativo deben usar exactamente este patrón para su cabecera de página. No se usan `<h2>`, `<h3>` ni otros niveles — solo `<h1>`.

### Estructura JSX

```tsx
{/* Encabezado */}
<div style={{
  display: 'flex',
  justifyContent: 'space-between',
  alignItems: 'flex-start',
  flexWrap: 'wrap',
  gap: 12,
}}>
  <div>
    <h1 style={{ fontSize: 20, fontWeight: 600, color: 'var(--z-text-primary)', margin: 0 }}>
      Nombre del Sub-módulo
    </h1>
    <p style={{ fontSize: 13, color: 'var(--z-text-muted)', margin: '4px 0 0' }}>
      Descripción breve del propósito del sub-módulo.
    </p>
  </div>
  {/* Opcional: control a la derecha (selector de período, badge, botón de acción) */}
</div>
```

### Reglas

| Elemento | Valor |
|---|---|
| Título (`h1`) | `fontSize: 20`, `fontWeight: 600`, `color: var(--z-text-primary)`, `margin: 0` |
| Descripción (`p`) | `fontSize: 13`, `color: var(--z-text-muted)`, `margin: '4px 0 0'` |
| Wrapper externo | `display: flex`, `justifyContent: space-between`, `alignItems: flex-start`, `flexWrap: wrap`, `gap: 12` |
| Inner wrapper | `<div>` sin estilos — contiene h1 + p |
| Padding propio | **Prohibido** — el padding viene de `AppLayout` (`padding: 28px 32px`) |
| Elemento derecho | Opcional: controles de filtro global, badge de estado, botón CTA |

### Estándar de tabs (navegación interna)

Cuando un sub-módulo tiene pestañas internas, usar este patrón (igual que `InformesRecaudoPage`):

```tsx
{/* Tabs */}
<div style={{ display: 'flex', gap: 2, borderBottom: '1px solid var(--z-border)' }}>
  {TABS.map(t => (
    <button
      key={t.id}
      onClick={() => setTabActiva(t.id)}
      style={{
        padding: '9px 20px',
        fontSize: 13,
        fontWeight: tabActiva === t.id ? 600 : 400,
        color: tabActiva === t.id ? 'var(--z-brand)' : 'var(--z-text-muted)',
        background: 'none',
        border: 'none',
        borderBottom: `2px solid ${tabActiva === t.id ? 'var(--z-brand)' : 'transparent'}`,
        cursor: 'pointer',
        marginBottom: -1,
        transition: 'color 0.15s',
      }}
    >
      {t.label}
    </button>
  ))}
</div>
```

### Lo que NO se permite

- `<h2>` o `<h3>` como título principal del sub-módulo
- `fontSize` distinto de `20` en el título principal
- `fontWeight` distinto de `600` en el título principal
- `padding` propio en el div raíz del sub-módulo
- Tabs con `borderRadius`, colores por tab individuales o estilo de "pestaña levantada"
