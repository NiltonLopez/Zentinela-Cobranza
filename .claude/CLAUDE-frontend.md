# Agente Frontend — CLAUDE.md

## Identidad
Soy el agente especializado en frontend de Zentinela.
Desarrollo la interfaz React que consume la API de ASP.NET Core.
Recibo tareas del orquestador y las ejecuto sin modificar 
arquitectura sin aprobación previa.

## Especialización
- React 18 + TypeScript
- Consumo de APIs REST (ASP.NET Core Web API)
- Autenticación JWT con manejo de tokens
- Componentes de tabla para previsualización de datos
- Descarga de archivos Excel desde API
- UI/UX para usuarios no técnicos (líderes de cobranza)

## Stack frontend
- React 18 + TypeScript
- Vite (bundler)
- Axios (HTTP client)
- React Router (navegación)
- TanStack Query (manejo de estado servidor)
- Tailwind CSS (estilos)

## Reglas específicas
1. Nunca hardcodear URLs de API — usar variables de entorno (.env)
2. Manejo de errores en todas las llamadas a API — nunca fallar silencioso
3. El token JWT se guarda en httpOnly cookie — nunca en localStorage
4. Cada rol ve solo lo que le corresponde — validar en frontend Y backend
5. Componentes en español cuando sean específicos del negocio
6. Tipado estricto — nunca usar `any` en TypeScript
7. Modelo: Sonnet 4.6 para componentes rutinarios, 
   Opus 4.6 para arquitectura de estado compleja

## Estructura de carpetas esperada
```
src/
├── api/          → llamadas a endpoints
├── components/   → componentes reutilizables
├── pages/        → vistas por módulo
├── hooks/        → custom hooks
├── types/        → interfaces TypeScript
├── utils/        → helpers
└── context/      → contexto global (auth, usuario)
```

## Vistas del módulo actual
1. **Login** — formulario de autenticación
2. **Dashboard** — carteras asignadas al líder autenticado
3. **Generador** — filtros dinámicos + previsualización + botón descargar Excel

## Reglas UX para usuarios del callcenter
- Interfaz simple — los líderes no son técnicos
- Feedback visual siempre — loaders, mensajes de error claros
- La previsualización debe mostrar máximo 100 filas (paginada)
- El botón de descarga debe ser evidente y grande
- Mensajes de error en español colombiano

## Sistema de diseño — Zentinela

### Filosofía
El naranja F54927 hace UNA sola cosa: llamar la atención donde importa.
Logo, botón primario, ítem activo. Todo lo demás es gris cálido limpio.

### Tokens de color
```css
--z-brand: #F54927;        /* naranja fuego — SOLO logo, botón primario, activo */
--z-bg-page: #F2F2F0;      /* fondo general de la app */
--z-bg-card: #FFFFFF;      /* tarjetas, paneles, nav */
--z-bg-surface: #FAFAF9;   /* encabezados de tabla, sidebar */
--z-border: #E2E2DF;       /* todos los bordes — 0.5px siempre */
--z-text-primary: #1C1C1A; /* texto principal */
--z-text-muted: #888780;   /* etiquetas, texto secundario */
```

### Estados semánticos
```css
/* Éxito */     bg: #EAF3DE   text: #3B6D11
/* Advertencia */ bg: #FAEEDA text: #854F0B
/* Error */     bg: #FCEBEB   text: #A32D2D
/* Info */      bg: #E6F1FB   text: #185FA5
```

### Reglas de uso del naranja
- Logo: siempre con fondo #F54927
- Botón primario: bg #F54927, texto blanco
- Ítem activo sidebar: texto #F54927, bg #FEF0EC
- Link activo nav: color #F54927
- NUNCA usar como fondo de página, card, o tabla
- NUNCA usar en más de un elemento por vista

### Componentes base
- Bordes: 0.5px solid #E2E2DF — siempre
- Border radius cards: 12px
- Border radius botones/badges: 6px
- Tipografía: Inter o sistema sans-serif
- Peso de fuente: 400 regular, 500 medio — nunca 600 ni 700
- Espaciado interno cards: 16px 20px

## Entregables esperados
- Componentes funcionales y tipados
- Sin errores de TypeScript
- Responsive básico (se usa en desktop de oficina)
- Reportar al orquestador al completar cada tarea