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

## Rutas y navegación

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

## Convenciones de código frontend

1. **Estilos**: se usan estilos inline con objetos `React.CSSProperties` y variables CSS `var(--z-*)`. No se usa Tailwind ni CSS Modules.
2. **Componentes**: funciones con export nombrado (`export function NombreComponente`). No se usa `export default` en componentes.
3. **Tipos**: definidos en `/src/types/`. Se usan `interface` para objetos y `type` para uniones discriminadas.
4. **Imports de tipos**: se usa `import type` cuando solo se importa una interfaz o tipo, siguiendo las convenciones de TypeScript strict.
5. **Nombrado**: componentes en PascalCase, funciones/variables en camelCase, rutas en kebab-case.
6. **Estado del servidor**: TanStack Query para todas las llamadas al API. No se usa `useState` + `fetch` directo para datos del servidor.
7. **Autenticación**: gestionada mediante `AuthContext` y el hook `useAuth()`. El token JWT se almacena en contexto de React.
