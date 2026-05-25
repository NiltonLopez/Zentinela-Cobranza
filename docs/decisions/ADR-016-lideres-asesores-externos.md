# ADR-016 — Líderes y asesores externos sin tabla Agencia

**Fecha:** 2026-05-20
**Estado:** Aceptado
**Módulos:** Identity, Gestión de Personal, Gestión de Carteras, Informes, Dashboard Director/Líder, Metas, Cargue de Pagos, sub-módulo nuevo *Agencias Externas*

---

## Contexto

La empresa trabaja con dos tipos de líderes:

- **Líderes internos** (empleados de planta) — usan Zentinela completo: estrategias, informes, metas, dashboards.
- **Líderes externos** (agencias o personal externo) — solo participan operativamente con sus carteras; no deben aparecer en métricas administrativas (recaudo, metas, dashboards de la operación).

Casos reales:
- **Hugo Betancur**, **Adriana Molina** — completamente externos.
- **Manuela** — híbrida: trabaja *Mercadeos Personal* externamente y *Aliarcom Administrativa* internamente.
- Una misma cartera (p. ej. *Mercadeos Personal Propia*) puede tener simultáneamente líderes internos y externos asignados, cada uno trabajándola desde su lado.

Adicionalmente, un nuevo sub-módulo **Informes → Agencias Externas** debía permitir calificar el rendimiento de las relaciones externas (recaudo, honorarios, asignaciones, gráfico discriminador por cartera).

---

## Decisión

El modelo se sostiene en **dos columnas ortogonales** — **sin crear una tabla `Agencia`**:

### 1. Rol del usuario (`AspNetUserRoles`)

Dos roles nuevos en Identity:

- `Líder Externo`
- `Asesor Externo`

Definen lo que el usuario **ve** (sidebar, autorización de endpoints):

| Rol | Módulos visibles |
|---|---|
| `Líder Externo` | Cartera (Calificador, Creador, Comparador) + Consultas (Consulta Integrada, Rastreo de Líneas) |
| `Asesor Externo` | Solo Consulta Integrada |

Ninguno ve Home/Dashboard, Informes, Administración. Redirección post-login se ajusta por rol (`rutaInicialPorRol` en `navigation.ts`).

### 2. Flag de la relación (`Lider_Cartera.EsExterna BIT`)

```sql
ALTER TABLE dbo.Lider_Cartera ADD EsExterna BIT NOT NULL DEFAULT 0;
```

Marca cada relación líder-cartera específica como externa.

**¿Por qué en `Lider_Cartera` y no en `Cartera`?** Porque la misma cartera puede coexistir:
- Operada externamente por un líder externo (relación con `EsExterna = 1`).
- Operada internamente por un líder de planta (relación con `EsExterna = 0`).

Si el flag viviera en `Cartera`, ese caso sería imposible de modelar.

### Las dos dimensiones son independientes

- **Manuela** → `Líder` normal (rol) + sus dos relaciones tienen flags distintos.
- **Felipe / Tatiana** → `Líder Externo` (rol) + todas sus relaciones con `EsExterna = 1`.

El sistema **decide el comportamiento por rol** (vista, autorización) y **filtra los datos por relación** (qué incluir en informes, metas, dashboards).

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|-------------|-------------------|
| Tabla `Agencia` separada con FK desde `Cartera` o `Lider` | Sobre-modelado: "cada externo es persona suelta", no hay atributos extra que justifiquen una entidad. Se descartó por petición explícita del usuario. |
| Flag `Cartera.EsExterna` | Impide modelar una cartera trabajada simultáneamente por líderes internos y externos. Inicialmente implementado y revertido el mismo día (2026-05-20). |
| Derivar "líder externo" de las asignaciones (sin rol) | Frágil: asignar/quitar una cartera interna cambiaría implícitamente el rol del usuario. No deja auditoría clara. |

---

## Filtros aplicados

Todos los filtros administrativos usan la subquery `NOT EXISTS … AND lc.EsExterna = 1` contra `Lider_Cartera`:

| Servicio | Comportamiento |
|---|---|
| `InformesRecaudoService` (3 sub-informes) | Pagos/Metas/Líderes-Cartera excluyen relaciones externas |
| `InformesRecaudoService.GetInformeAgenciasExternasAsync` | Inverso: **solo** relaciones externas |
| `DashboardDirectorService` | Vista global excluye externas; con `liderID` filtra por agentes del líder en cualquier cartera (ver bug #2 abajo) |
| `AsignacionMetasService` | Combinaciones líder-cartera filtradas a internas |
| `GestionCarterasService.ListarRelacionesAsync(incluirExternas)` | Default `true`; `PagosController.ListarCarteras` pasa `false` |

Frontend: el sidebar dinámico oculta entradas según rol (`navigation.ts`), las rutas en `App.tsx` aplican `RutaProtegida` con las listas correspondientes, y endpoints backend hacen `[Authorize(Roles=…)]` con los nuevos roles.

---

## Sub-módulo nuevo: `Informes → Agencias Externas`

Lista por **(líder × cartera externa)** con KPIs (recaudo, honorarios, asignaciones, agencias activas) + gráfico de **barras apiladas horizontales** (una barra por líder, segmento por cartera externa, recharts). Accesible para `Admin / Líder / Director`. Una misma cartera puede aparecer dos veces si tiene una relación externa y otra interna con líderes distintos.

---

## Bugs colaterales resueltos en el mismo lote

### 1. `UltimoEstadoTitular.AgenteID` mapeado como `int` no-nullable

La columna real en BD es **nullable** (~695k filas con `AgenteID NULL`, titulares cargados sin asesor). La entidad C# lo declaraba como `int`, lo que causaba `SqlNullValueException` en el Dashboard del Director cuando el filtro de fechas dejaba pasar esas filas. Adicionalmente la PK del mapping incluía `AgenteID`, cuando la PK real en SQL Server es `(TitularID, CarteraID)`.

**Fix**: `AgenteID → int?`, mapping `HasKey(u => new { u.TitularID, u.CarteraID })`. El servicio del Director excluye las filas sin agente del desglose por asesor y las conserva en el agregado por líder.

### 2. Dashboard de Líder mezclaba métricas con otros líderes

`DashboardDirectorService.ObtenerDashboardAsync(liderID, …)` filtraba `UltimoEstadoTitular` solo por **las carteras del líder**. Como esa tabla no expone `LiderID` y varias carteras se comparten (p. ej. *Mercadeos Personal Propia* la trabajan 14 líderes), los estados gestionados por agentes de otros líderes aparecían en el dashboard del líder consultante.

**Regla nueva**: cuando hay `liderID`, filtrar por agentes del líder (`Cartera_Agente` con `LiderID = @lid`), **sin restringir cartera** — los asesores del líder pueden apoyar en carteras de otros líderes y esas gestiones cuentan para su rendimiento. Si además hay `carteraID`, lo limita a esa cartera específica.

### 3. `CambiarUsuarioAsync` no sincronizaba `Agente.UsuarioControlNext`

Al editar el username de un usuario en Identity, el campo `Agente.UsuarioControlNext` (en `DBCallCenter`) quedaba desincronizado, rompiendo el lookup que hace `CarguePagosService` para resolver `AgenteControlID` desde el CSV. **Fix**: tras `SetUserNameAsync`, buscar el `Agente` por cédula y propagar el nuevo username a `UsuarioControlNext`.

### 4. Filtro por supervisor en Gestión de Personal no funcionaba

El DTO backend serializa `nombreSupervisor` pero el tipo TypeScript declaraba `supervisorNombre`. La comparación nunca matcheaba. **Fix**: alinear el tipo `UsuarioAdmin` al JSON real.

### 5. Whitelist de roles en `CambiarRolAsync`

`AdminService.CambiarRolAsync` validaba contra `{Admin, Líder, Director, Asesor}` y rechazaba los roles nuevos. **Fix**: agregar `LiderExterno` y `AsesorExterno` a la whitelist. Adicionalmente, la resolución de `LiderID` al cambiar el rol ahora cubre también `LiderExterno` (antes solo `Lider`).

---

## Consecuencias

**Positivas**

- Modelo mínimo (una columna nueva en `Lider_Cartera`, dos roles, sin tabla nueva).
- Cubre el caso híbrido (Manuela) y el caso de cartera compartida interna/externa sin compromisos.
- Filtros administrativos resuelven con una sola subquery contra `Lider_Cartera`.
- El usuario externo tiene autorización backend real (no solo UI escondida): `[Authorize(Roles=…)]` rechaza accesos no permitidos.

**Negativas / aceptadas**

- `Cartera_Agente` no tiene flag de externo: la pertenencia "externa" se deriva del par `(LiderID, CarteraID)` contra `Lider_Cartera.EsExterna`. Aceptable porque `Cartera_Agente` ya tiene `LiderID`.
- `UltimoEstadoTitular` no tiene `LiderID`. En el Dashboard del Director sin líder seleccionado, la vista global se filtra a carteras "con al menos una relación interna" — una cartera mixta aporta todos sus titulares al agregado. Esto es una aproximación; mejorarla requeriría un join adicional con `Cartera_Agente` por agente y se decidió no hacerlo hasta tener un caso de uso concreto.

**Operativas**

- Asignar el rol `Líder Externo` / `Asesor Externo` a usuarios existentes se hace desde **Gestión de Personal → Cambiar rol**.
- Marcar una relación como externa se hace desde **Gestión de Líderes y Carteras → + Crear Cartera** (checkbox "Cartera externa"); aplica también al asociar una cartera existente. Cambiar el flag de una relación ya creada se hace por SQL directo (decisión administrativa fuera de la UI).
- Migración aplicada el 2026-05-20 en `DBCallCenter`. Backup previo: `Lider_Cartera_BAK_20260520`.

---

## Addendum 2026-05-25 — Fuga de visibilidad en dropdowns del módulo Cartera

**Síntoma**: una `Líder Externo` (Evely) veía en el selector del Calificador, Comparador y Creador **todas las carteras de la empresa**, no solo las suyas.

**Causa**: los tres servicios filtran la lista de combinaciones cuando el rol es `Roles.Lider` y caen al branch Admin/Director en caso contrario. La condición se escribió antes de existir el rol `LiderExterno` y nunca se amplió. El branch Admin/Director devuelve cada `(Cartera × Líder)` más una entrada `"Cartera Completo"` por cartera.

**Puntos corregidos**

| Archivo | Línea | Cambio |
|---|---|---|
| `CalificadorCarteraService.ObtenerOpcionesAsync` | servicio SQL | `Roles.Lider` → `Roles.Lider or Roles.LiderExterno` |
| `ComparadorCarterasService.ObtenerOpcionesAsync` | servicio SQL | idem |
| `CreadorZentinelaService.ObtenerOpcionesAsync` | servicio Application | idem |
| `CreadorZentinelaService.ResolverLiderID` | guard de seguridad | idem — sin esto un externo podía mandar `liderID` ajeno en el body de `/preview`, `/exportar`, `/exportar-csv` y descargar Base Madre de otra relación |

**Por qué no se detectó antes**: el lote original (commit `e3e75d0`) revisó autorización de endpoints (`[Authorize(Roles=…)]`) y filtros administrativos contra `Lider_Cartera.EsExterna`, pero no auditó los checks `Rol == Roles.Lider` que ya existían en los servicios self-service del módulo Cartera. Estos son ortogonales al flag `EsExterna`: filtran por **identidad del usuario**, no por marca de la relación.

**Regla a futuro**: cualquier rama de código que actualmente verifique `contexto.Rol == Roles.Lider` para filtrar a "sus propias carteras" debe incluir también `Roles.LiderExterno`. Idealmente expresarlo como `contexto.Rol is Roles.Lider or Roles.LiderExterno` (pattern matching) para que el lector vea ambos casos juntos.
