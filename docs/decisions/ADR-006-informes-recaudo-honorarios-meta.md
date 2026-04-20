# ADR-006 — Informes de Recaudo: métricas en Honorarios, MetaHonorarios y estructura de tabs

**Estado:** Aceptado  
**Fecha:** 2026-04-20  
**Módulos afectados:** Informes de Recaudo (frontend + backend)

---

## Contexto

El módulo de Informes de Recaudo se construyó inicialmente mostrando métricas de **Recaudo** como eje principal. Durante el desarrollo se clarificó que el negocio evalúa el desempeño de asesores y líderes principalmente por **Honorarios** (comisiones), no por Recaudo bruto. Esto implicó cambios en la lógica de cálculo y en la presentación visual.

---

## Decisiones tomadas

### 1 — `DeberiaLlevarHoy` basado en `MetaHonorarios`

**Problema:** `DeberiaLlevarHoy` se calculaba como `MetaRecaudo × (díasTranscurridos / díasDelMes)`.  
**Decisión:** Usar `MetaHonorarios × (díasTranscurridos / díasDelMes)`.  
**Razón:** El negocio mide el avance del período contra la meta de comisiones, no contra la meta de recaudo bruto.  
**Impacto:** Se agregó columna `MetaHonorarios` a `Meta_Agente_Mes` y `Meta_Lider_Cartera_Mes`. `FaltaParaMeta` = `Max(0, MetaHonorarios − Honorarios)`.

### 2 — Columnas separadas en tablas: Meta Recaudo y Meta Honorarios

Las tablas muestran ambas metas para que el usuario tenga contexto completo sin perder la referencia de recaudo bruto.

### 3 — Gráficos usan Honorarios como eje principal

Los elementos visuales (barras) muestran: **Honorarios · Debería hoy · Meta Honorarios**. El Recaudo bruto sigue disponible en la tabla pero no domina el gráfico.

### 4 — `TotalAsesores` por combinación Líder-Cartera

Inicialmente era por Líder (todos sus asesores). Se cambió a por (LiderID, CarteraID) para que el tab Carteras pueda mostrar asesores por cartera individual correctamente.

---

## Estructura de tabs — Informes de Recaudo

| Tab | Contenido | Selector de período |
|-----|-----------|---------------------|
| **Agentes** | Métricas por asesor. Filtros: líder, cartera, búsqueda libre. Gráfico horizontal. | Global (esquina superior derecha) |
| **Líderes** | Métricas agregadas por líder (todas sus carteras). Gráfico vertical con orden seleccionable. | Global |
| **Carteras** | Toggle **Líder-Cartera** (fila por combinación) / **Cartera** (agrega líderes de la misma cartera). Filtro por líder. | Global |
| **Comparativo** | Toggle **2 meses / 3 meses**. Dos queries independientes (A↔B y B↔C) fusionados por `carteraID`. Variaciones A→B y B→C. | Internos al tab |

---

## Convenciones de color

| Color | Hex | Uso |
|-------|-----|-----|
| `brand` | `#2563eb` | Recaudo |
| `honorarios` | `#7c3aed` | Honorarios |
| `proyeccion` | `#f59e0b` | Debería hoy / proyecciones |
| `meta` | `#94a3b8` | Metas |
| `mesA` | `#94a3b8` | Período A en Comparativo |
| `mesB` | `#2563eb` | Período B en Comparativo |
| `mesC` | `#10b981` | Período C en Comparativo |

---

## Errores encontrados y lecciones

### React Rules of Hooks
`useState` y `useMemo` deben llamarse **antes** de cualquier `return` condicional. Se encontró este bug repetidamente en `TabLideres`, `TabCarterasDetalle` y `TabComparativo`. La solución: todos los hooks al inicio del componente, antes de `if (isLoading) return`.

### Temporal Dead Zone en constantes de módulo
`const MES_DOS_ANTES` fue declarada en la línea 26 referenciando `MES_ACTUAL` que se declaraba en la línea 58. JavaScript lanza `ReferenceError` silencioso en build pero falla en runtime colapsando toda la app. Solución: declarar las constantes derivadas **después** de sus dependencias.

---

## SQL aplicado en producción

```sql
-- Agregar MetaHonorarios a tablas de metas
ALTER TABLE dbo.Meta_Agente_Mes        ADD MetaHonorarios decimal(18,2) NOT NULL DEFAULT 0;
ALTER TABLE dbo.Meta_Lider_Cartera_Mes ADD MetaHonorarios decimal(18,2) NOT NULL DEFAULT 0;

-- Poblar datos de ejemplo (10% de MetaRecaudo)
UPDATE dbo.Meta_Agente_Mes        SET MetaHonorarios = MetaRecaudo * 0.10 WHERE AgenteID BETWEEN 90000001 AND 90000020;
UPDATE dbo.Meta_Lider_Cartera_Mes SET MetaHonorarios = MetaRecaudo * 0.10 WHERE LiderID  BETWEEN 9001 AND 9003;
```
