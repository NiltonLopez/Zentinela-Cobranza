// Margen negativo para que las barras desplazables (tickers) del dashboard ocupen
// todo el ancho de <main>, no solo el de .z-page-content (que en AppLayout.tsx tiene
// max-width:1440px + padding 32px y se centra en pantallas anchas, dejando espacio
// a los lados que un -32px fijo no cubre). Cancela el padding local (32px) más el
// espacio de centrado que aparece cuando el ancho de <main> supera 1440px.
export const MARGEN_BARRA_COMPLETA =
  'calc(-32px - max(0px, (100vw - var(--z-sidebar-width) - 1440px) / 2))';
