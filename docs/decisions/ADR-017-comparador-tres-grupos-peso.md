# ADR-017 — Comparador de Carteras: clasificación en 3 grupos + regla por peso

**Fecha:** 2026-05-21
**Estado:** Aceptado
**Módulos:** Comparador de Carteras (`Zentinela.Application.Config.PerfilClasificacion`, `ComparadorCarterasService`, `ComparadorCarterasPage`)

---

## Contexto

El Comparador de Carteras detecta titulares que pueden gestionarse mejor cruzando carteras: si en la cartera del líder X un titular figura sin gestión, pero en otra cartera ya tiene contacto positivo o paz y salvo, el líder puede aprovechar esa información.

La implementación anterior tenía dos problemas:

1. **Lista de "códigos positivos" demasiado corta y desalineada con el resto del sistema.** El Comparador clasificaba como positivos sólo 6 perfiles (`Pago Total`, `Paz y Salvo`, `Normalizado`, `Contacto Positivo`, `Promesa de Pago`, `Acuerdo`), mientras los Dashboards (`Tipificaciones.cs`) reconocen 13 códigos positivos vigentes en operación (`Acuerdo de pago abono`, `No hubo acuerdo`, `Renuente`, `Incumplido`, etc.). Resultado: titulares con perfiles operativamente positivos (`No hubo acuerdo`) caían en "Sin Gestión" desde la perspectiva del Comparador.

2. **Regla única "Negativo → algo mejor"**. Solo se mostraban oportunidades donde la cartera seleccionada tenía `Sin Gestión`. No se detectaban *promociones* dentro del rango positivo, p.ej. un titular en `Contacto Positivo` en la cartera seleccionada que en otra cartera ya está en `Paz y Salvo`.

---

## Decisión

### 1. Tres grupos con vocabulario explícito

`PerfilClasificacion` clasifica `UltimoPerfil` en tres categorías ordenadas (de peor a mejor):

| Categoría | Peso | Códigos |
|---|---:|---|
| `ContactoNegativo` | 0 | default — null, vacío, `Mensaje con tercero`, `Mensaje en buzón`, `Fallecido`, `No contacto`, `Mensaje contestadora/buzon`, `Numero equivocado`, `Ilocalizado`, `Datos de localización`, `Sin gestion`, `Volver a llamar`, **`PQR`**, **`Fraude o suplantacion`** |
| `ContactoPositivo` | 1 | `Acuerdo de pago abono`, `Acuerdo de pago total`, `Realizo abono`, `Recordar`, `Incumplido`, `No hubo acuerdo`, `Potencial acuerdo`, `Renuente`, `No gestionar` + legacy (`Contacto Positivo`, `Promesa de Pago`, `Acuerdo`) |
| `PazYSalvo` | 2 | `Paz y salvo`, `Consignación por aplicar` + legacy (`Pago Total`, `Normalizado`) |

Comparación **case-insensitive** (`StringComparer.OrdinalIgnoreCase`).

Strings *legacy* preservados por defensa: existen filas históricas en `UltimoEstadoTitular.UltimoPerfil` con esos valores (anteriores al sistema de tipificaciones actual). Quitarlos los empujaría a `ContactoNegativo` falsamente.

`PQR` y `Fraude o suplantacion` **no son comparables** — corresponden a estados administrativos, no a gestión cobratoria. Caen en `ContactoNegativo` por default (no se listan en ningún HashSet).

### 2. Regla de comparación: peso estricto creciente

Para cada titular en la cartera seleccionada cuyo mejor perfil tiene peso < 2 (es decir, no está ya en `PazYSalvo`), se buscan coincidencias en otras carteras donde el mejor perfil tenga **peso estrictamente mayor**.

Esto cubre dos casos como una sola condición:

| Peso en seleccionada | Coincidencias mostradas |
|---|---|
| 0 (`ContactoNegativo`) | otras carteras en peso 1 o 2 |
| 1 (`ContactoPositivo`) | otras carteras en peso 2 |
| 2 (`PazYSalvo`) | no se compara (ya está al tope) |

El "mejor perfil" se define por titular: un mismo titular puede aparecer con varios agentes (una fila por agente). Tomamos el de mayor peso.

### 3. UI sin secciones

El listado del Comparador no se divide. Todos los pares "perfil en seleccionada vs perfil en otra cartera" caen en una sola tabla; las chips de color comunican la categoría:

- Verde → `PazYSalvo`
- Azul → `ContactoPositivo`
- Rojo → `ContactoNegativo`

El visitante distingue Negativo→Positivo de Positivo→PazYSalvo leyendo las dos chips por fila.

---

## Por qué este diseño

**Por qué no unificar con `Tipificaciones.cs`:** los dos sistemas tienen responsabilidades distintas. `Tipificaciones` mide *efectividad operativa* (contactabilidad, acuerdo/abono); `PerfilClasificacion` mide *avance en el ciclo cobratorio* (negativo → positivo → cancelado). Algunos códigos pertenecen a ambos pero con semánticas diferentes — p.ej. `Paz y salvo` es positivo en operación y también es el techo del ciclo cobratorio. Unificar acoplaría dos métricas que pueden evolucionar por separado.

**Por qué peso estricto (`>`), no `≥`:** un titular con el mismo perfil en ambas carteras (`Recordar` vs `Recordar`) no aporta oportunidad — no hay nada nuevo que mostrar.

**Por qué incluir `Contacto Positivo` en la base del comparativo (cambio de nombre del campo):** `TotalSinGestion` ya no cuenta solo titulares en `Sin Gestión` — cuenta toda la base posible de promoción (peso < 2). Conservamos el nombre del campo en el DTO/JSON para no romper el frontend; el label visible cambia a "Base del comparativo".

---

## Cómo agregar/quitar códigos en el futuro

1. Editar `src/Zentinela.Application/Config/PerfilClasificacion.cs` — mover el string entre HashSets o agregarlo nuevo. Comparación case-insensitive — no importa la capitalización.
2. Si el código nuevo es `PazYSalvo` o `ContactoPositivo`, **agregarlo también** al `Set` espejo en `src/Zentinela.Web/src/pages/cartera/ComparadorCarterasPage.tsx` (variables `PERFILES_PAZ_Y_SALVO` y `PERFILES_CONTACTO_POSITIVO`) — si no, la chip aparece en rojo aunque el backend cuente la oportunidad.
3. Sin migraciones de BD, sin reinicio de servicios externos. Recompilar y listo.

---

## Consecuencias

### Inmediatas

- El conteo "Base del comparativo" sube respecto a la versión anterior (incluye titulares en `Contacto Positivo` que antes se ignoraban).
- Aparecen oportunidades nuevas tipo "Positivo → Paz y Salvo".
- Los códigos `PQR` y `Fraude o suplantacion` ya no generan oportunidades (cae en negativo por default — pueden aparecer como origen pero ya no como "destino positivo").

### A futuro

- Si se agrega un nuevo código de tipificación operativa, hay que decidir si entra al Comparador. Default: queda en `ContactoNegativo` hasta que un humano lo mueva.
- La lista espejo en el frontend es deuda de sincronización aceptada (más simple que mandar la clasificación por DTO). Mitigada por test manual + comentario explícito.

### Cómo revertir

`git revert <commit>` restaura clasificación vieja (`YaPago`/`SinGestion` y los 6 códigos originales) y la regla única Negativo→algo. No requiere migración de BD.

---

## Referencias

- `src/Zentinela.Application/Config/PerfilClasificacion.cs` — clasificación
- `src/Infrastructure/Zentinela.Infrastructure.SQLServer/Services/ComparadorCarterasService.cs` — lógica del Comparador
- `src/Zentinela.Application/Constants/Tipificaciones.cs` — clasificación paralela usada por Dashboards
- `src/Zentinela.Web/src/pages/cartera/ComparadorCarterasPage.tsx` — render + listas espejo
