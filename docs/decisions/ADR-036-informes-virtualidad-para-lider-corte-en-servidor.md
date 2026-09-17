# ADR-036 — Informes de Virtualidad para el rol Líder: el corte de alcance se hace en el servidor

**Estado:** Aceptado
**Fecha:** 2026-09-17
**Módulos afectados:** Informes → Informes de Virtualidad (Mensajería, IVR, Pagos vs Impacto)

---

## Contexto

El sub-módulo *Informes de Virtualidad* era exclusivo de Admin/Auxiliar de Sistemas. Se pide que el
rol **Líder** lo vea "tal cual el administrador, pero solamente con los resultados pertinentes
exactamente a ese líder y no más".

Los tres endpoints de informe ya aceptaban un `liderID` **opcional** por query string como filtro.
Eso alcanza para el admin, que elige a quién mirar, pero no para un líder: si la restricción
viviera solo en la UI (ocultar el selector), cualquier líder podría editar la URL y pedir
`?liderID=<otro>`. En este proyecto ya existe ese agujero en el Calificador: el dropdown de opciones
sí se filtra por contexto, pero `GET /calificador/{cartera}/{lider}` acepta cualquier par.

## Decisión

1. **El alcance lo decide el servidor, por rol.** Extensión `AlcanceLiderExtensions.TryResolverLiderID`
   en `Zentinela.API`: para el rol `Líder` el `liderID` de la petición **se ignora por completo** y se
   usa el del claim `liderID` del token. Si un Líder no trae ese claim, el endpoint responde
   **403** en lugar de dejarlo ver todo por accidente. Admin/Director/Auxiliar conservan el filtro
   opcional tal cual lo pidieron.

2. **Los tres controllers de informe** (`VirtualidadIVRController`, `VirtualidadMensajeriaController`,
   `VirtualidadPagosImpactoController`) pasan a `[Authorize(Roles = Admin, AuxiliarSistemas, Lider)]`
   **en la clase**, no por método: en ASP.NET Core varios `[Authorize]` se combinan con AND, así que
   un atributo por método nunca puede *ampliar* lo que la clase restringe.

3. **Pagos vs Impacto gana filtro por líder** en las tres capas (interfaz, servicio, controller).
   El filtro acota solo el universo de **pagos** (`Pago.LiderID`); los cruces contra mensajería, IVR
   y llamadas siguen siendo por cédula sin líder, porque lo que se mide es si el titular que le pagó
   a este líder fue impactado por *algún* canal, sea quien sea quien lo impactó.

4. **Las tabs Llamadas Perdidas y Llamadas Entrantes no se muestran al Líder.** Son cargas de archivo
   (operación administrativa) y sus registros no traen `LiderID` — solo titular y cartera. Acotarlas a
   un líder exigiría inferir por `Titular_Cartera`, que cambia con cada reasignación; eso no es
   "exactamente lo suyo", es "aproximadamente lo suyo". Se dejan fuera antes que mostrar cifras que
   el líder no podría cuadrar.

5. **UI:** `useAlcanceLider()` en la página fija el filtro al `liderID` del usuario y oculta el
   selector de líder. El selector de cartera se mantiene y ya viene acotado por contexto desde
   `obtenerOpcionesCalificador`. Es presentación; la garantía es el punto 1.

## Verificación

- `TryResolverLiderID`, seis casos con `ClaimsPrincipal` fabricados: Líder que pide otro líder recibe
  el suyo; Líder sin pedir nada recibe el suyo; Líder sin claim → denegado; Admin y Director reciben
  lo que pidieron o sin filtro. **Los seis pasan.**
- Predicado de pagos contra datos reales (últimos 60 días): 2.560 pagos, 5 líderes; la suma por
  líder reconstruye el total exacto.
- `dotnet build`, `tsc` y `npm run build` limpios. La ruta `/informes/virtualidad` ya admitía
  `Líder` en `RutaProtegida`; solo faltaba el sub-módulo en `navigation.ts`.

## Hallazgo colateral — tres tabs rotas en producción desde su origen

Al intentar ejecutar `VirtualidadPagosImpactoService` completo contra la BD:

```
El nombre de objeto 'LlamadaPerdida' no es válido.
```

Las tablas `LlamadaPerdida` y `LlamadaEntrante`, que mapean `LlamadaPerdidaMapping` y
`LlamadaEntranteMapping`, **no existen en ninguna base de datos del servidor** (verificado con
`sp_MSforeachdb`) y no hay DDL en `docs/sql/`. Por tanto **Llamadas Perdidas, Llamadas Entrantes y
Pagos vs Impacto fallan hoy para el admin también**, desde el commit `6a063a7` que creó el módulo.
No se tocó: es una decisión del negocio si se crean las tablas o se desacopla Pagos vs Impacto de
las llamadas. El filtro por líder de este ADR queda listo para cuando esa tab funcione.

## Deuda que este ADR deja visible

- El Calificador tiene el mismo agujero que aquí se cierra: el corte por líder vive solo en el
  dropdown. Aplicar `TryResolverLiderID` allí es el mismo patrón.
- `navigation.ts` lista a `Director` en *Informes de Virtualidad*, pero los controllers no lo
  incluyen: un Director ve el sub-módulo y recibe 403. Preexistente; no se cambió porque no está
  claro cuál de los dos lados es el correcto.
- `Líder Externo` también tiene claim `liderID` y no se incluyó: el pedido fue "perfil Líder".
