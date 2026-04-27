# ADR-014 — Virtualidad: Mensajería e IVR con FK a Lider_Cartera

**Estado:** Aceptada
**Fecha:** 2026-04-27

## Contexto

El módulo de Virtualidad expone los canales digitales de impacto a titulares (Mensajería SMS, IVR, Llamadas Perdidas, Llamadas Entrantes). Antes de este cambio:

- `dbo.MensajesEnviados` tenía `CarteraID` nullable, `NombreCartera` denormalizado y ninguna FK formal.
- `dbo.IVREnvio` (singular) era una tabla diferente con su propio modelo (`IVRID identity`, `Resultado`, `Campana`) y un flujo pre-envío / post-envío en la UI.
- El selector de carga ya entregaba `(carteraID, liderID)` desde la UI (combo de calificador), pero solo el `carteraID` se persistía.

Resultado: la integridad referencial dependía solo del front, y los informes no podían diferenciar el desempeño por **líder dentro de la misma cartera**, dato clave porque las metas y los KPIs operan sobre la combinación `(LiderID, CarteraID)` (ver `Lider_Cartera`, `Meta_Lider_Cartera_Mes`).

## Decisión

1. **Modelo común para canales salientes con plantilla Excel**: `MensajesEnviados` e `IVREnviados` comparten exactamente la misma estructura de tabla y la misma UX:
   - Columnas: `NumeroDocumento, NumeroTelefono, LiderID, CarteraID, FechaEnvio, Canal, FechaCargue`.
   - FK compuesta `(LiderID, CarteraID) → Lider_Cartera(LiderID, CarteraID)`, `ON DELETE RESTRICT`.
   - `HasNoKey()` en EF Core — son tablas append-only para agregaciones, sin PK surrogate.
   - `NombreCartera` no se denormaliza: el informe hace `JOIN dbo.Cartera`.
   - `Canal` lo fija el back (`'SMS'` o `'IVR'`), no viene del archivo.

2. **Tab IVR reescrito como espejo del tab Mensajería**:
   - Se eliminan los sub-tabs `Pre-envío` / `Post-envío` / `Informe`.
   - Flujo único: selector cartera+líder → plantilla → subir `.xlsx` → validar → preview → confirmar.
   - Informe filtrable por **líder y/o cartera**, agrupado por `(LiderID, CarteraID)`.
   - Rutas API planas: `/api/virtualidad/ivr/{plantilla, validar, confirmar, informe}` (se borraron `pre-envio/*` y `post-envio/*`).

3. **Informe `por líder y cartera`** (no solo por cartera):
   - Tanto Mensajería como IVR devuelven `PorLiderCartera: { liderID, nombreLider, carteraID, nombreCartera, total }[]`.
   - El backend hace `JOIN _db.Carteras` y `_db.Lideres` y agrupa por la cuádrupla.
   - El front muestra los filtros (Líder + Cartera) sobre el botón **Generar informe**, derivados del dropdown del calificador (etiqueta `"Cartera — Líder"`).
   - El informe NO se dispara automáticamente al cambiar el período: requiere clic explícito en "Generar informe".

4. **Inyección de `LiderID` en el front**: el endpoint de validación recibe solo `carteraID`. El front, al recibir el preview, completa `liderID` en cada `filaValida` antes de enviar `confirmar`. Esto satisface la FK compuesta sin cambiar la firma del endpoint de validación.

## Consecuencias

**Positivas**
- Integridad referencial garantizada por la BD: imposible insertar un `(LiderID, CarteraID)` que no exista en `Lider_Cartera`.
- Los KPIs de Virtualidad ya pueden cruzarse contra `Meta_Lider_Cartera_Mes` y `Cartera_Agente` (mismo eje `(LiderID, CarteraID)`).
- UX consistente entre Mensajería e IVR — un solo patrón mental para el usuario.
- El renombrado de carteras no rompe el histórico de envíos (siempre se resuelve con JOIN).

**Negativas**
- Se pierde el seguimiento `Resultado` (Contestó / No contestó) que tenía `IVREnvio`. Si el negocio lo requiere, se reintroducirá como tabla separada `IVRResultado` ligada a un identificador del envío.
- Se pierden los gráficos de IVR `por semana`, `Pie contestaron/no contestaron` y la sección `vsGestión` que existían en el modelo previo. Quedan pendientes para una fase posterior.
- El backend de validación no verifica `(carteraID, liderID) ∈ Lider_Cartera`; depende del selector del front. La FK lo atrapa en `confirmar`, pero el error sería tardío. Mejora futura: aceptar `liderID` también en `/validar` y validar antes de devolver el preview.

## Migración

Aplicada vía `sqlcmd` contra `10.0.0.89,1433 / DBCallCenter`:

1. `DROP TABLE dbo.MensajesEnviados` y recrear desde `docs/sql/create_MensajesEnviados.sql` (datos previos eran SEED-TEST).
2. `DROP TABLE dbo.IVREnvio` (legacy, sin datos productivos) y crear `dbo.IVREnviados` desde `docs/sql/create_IVREnviados.sql`.
3. Datos de prueba en `docs/sql/seed_MensajesEnviados.sql` con combinaciones reales `(1,13) PELICANO PROPIA / (1,20) LUMEN / (2,21) MERCADEOS PERSONAL ALMACEN`. Limpieza con `DELETE WHERE Canal = 'SEED-TEST'`.

## Pendientes

- Aceptar `liderID` en el endpoint de validación para verificar la combinación contra `Lider_Cartera` antes de mostrar el preview.
- Plantilla y flujo análogos para Llamadas Entrantes / Perdidas (hoy aún viven en su modelo legacy).
- Reintroducir tracking de `Resultado` IVR si el negocio lo requiere (tabla `IVRResultado` con FK al envío).
