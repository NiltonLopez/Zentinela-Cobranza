# ADR-007 — Módulo Metas del Mes: gestión inline en lugar de cargue Excel

**Estado:** Aceptado  
**Fecha:** 2026-04-20  
**Módulos afectados:** Administración → Metas del mes, Informes de Recaudo (Tab Carteras)

---

## Contexto

Se necesitaba un mecanismo para que el administrador cargue las metas mensuales de recaudo y honorarios tanto para combinaciones líder-cartera como para agentes individuales. La primera aproximación fue un flujo de cargue Excel (plantilla → validar → confirmar), siguiendo el patrón de `CargueReportesPage`.

Durante la construcción se identificaron dos problemas con el approach Excel:

1. **Fricción innecesaria**: el administrador tiene que descargar una plantilla, llenarla en Excel y volver a subirla, cuando los datos ya viven en el sistema.
2. **Falta de contexto**: en la plantilla Excel el usuario no ve en tiempo real si esa combinación ya tiene meta, cuántos asesores tiene o cuántas cuentas están asignadas.

---

## Decisión

Reemplazar el flujo Excel por una **tabla inline editable** directamente en la aplicación, con las siguientes características:

### Período bloqueado al mes actual
No existe selector de período. El sistema siempre opera sobre el mes/año actuales. Esto evita errores de cargue en períodos cerrados y simplifica la UX. Si se necesita corregir una meta de un mes anterior, debe hacerse directo en BD.

### Énfasis en "sin meta"
Las filas sin meta para el ciclo actual se muestran con fondo naranja. Los KPIs de cabecera muestran el conteo de "con meta" vs "sin meta". Existe un filtro "Solo sin meta" para enfocarse en los pendientes.

### Guardado por fila (UPSERT individual)
Cada fila tiene su propio botón **Guardar** que hace UPSERT inmediato. No hay botón de "guardar todos". Esto permite al administrador ir fila por fila sin perder trabajo si hay un error de red.

### Columnas informativas
La tabla muestra datos de contexto que no son editables:
- **Líderes-Carteras**: Asesores asignados a esa combinación + Cuentas asignadas (titulares).
- **Agentes**: Cartera y Líder del agente.

Estos datos provienen de `Cartera_Agente` y `Titular_Cartera` en tiempo real.

---

## Arquitectura

### Backend
```
GET  /api/metas/estado-mes-actual   → EstadoMetasMesDto (todas las combinaciones + metas actuales)
POST /api/metas/guardar-lider       → GuardarMetaLiderRequest → ResultadoMetaDto
POST /api/metas/guardar-agente      → GuardarMetaAgenteRequest → ResultadoMetaDto
```

`CargueMetasService` consulta tres fuentes para construir `EstadoMetasMesDto`:
- `CallCenterDbContext`: `LiderCarteras`, `CarteraAgentes`, `TitularCarteras`, `MetaAgentesMes`, `MetaLiderCarteraMes`
- `RecursosHumanosDbContext`: `Empleados` (para nombre completo del agente vía `NumeroDocumento == AgenteID`)

### Frontend
- `CargueMetasPage.tsx`: tabla inline con `useQuery` para carga inicial y `useMutation` por fila para guardado.
- Cada `FilaLider` / `FilaAgente` maneja su propio estado local (inputs) desacoplado del query global.
- `onGuardado` llama `queryClient.invalidateQueries` para refrescar el estado tras guardar.

---

## Columna `Centro de costos` en Tab Carteras (Informes de Recaudo)

En la misma sesión se agregó la columna **Centro de costos** a la tabla del tab Carteras del informe de recaudo:

```
Centro de costos = asesores × $4.100.000
```

La constante `COSTO_ASESOR = 4_100_000` está definida en el frontend (`InformesRecaudoPage.tsx`) y se calcula en tiempo de renderizado — no se persiste en BD. Aplica a ambas vistas (Líder-Cartera y Cartera agrupada).

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Cargue Excel (plantilla → validar → confirmar) | Fricción innecesaria cuando los IDs ya están en el sistema; no muestra contexto en tiempo real |
| Selector de período libre | Permite errores en meses cerrados; el negocio solo debe fijar metas para el mes en curso |
| Guardado global ("Guardar todos") | Obliga a completar todas las filas antes de poder guardar; una fila con error bloquea el resto |
