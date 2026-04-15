# ADR-003 — Cargue de Pagos: resolución automática del LiderID por fila

**Fecha:** 2026-04-15  
**Estado:** Aceptado  
**Módulo:** Cargue de Pagos (CarguePagosService)

---

## Contexto

Al cargar un CSV de pagos, cada fila tiene una cédula de titular. Los pagos deben quedar asociados a un líder, pero una cartera puede tener múltiples líderes y el CSV no incluye esa información. Se evaluaron tres opciones:

1. El usuario selecciona la cartera **y** el líder en la UI → un archivo por líder.
2. El CSV incluye una columna `LiderID` → el usuario la rellena manualmente.
3. El sistema resuelve el líder automáticamente desde `Titular_Cartera`.

---

## Decisión

Se usa la **opción 3: resolución automática**. El servicio hace un lookup por lote:

```
SELECT TitularID, LiderID
FROM Titular_Cartera
WHERE TitularID IN (...cedulas...) AND CarteraID = :carteraID
```

Cada fila del CSV obtiene su `LiderID` según la asignación vigente en BD. Si una cédula no está asignada a esa cartera, la fila se marca como inválida con mensaje descriptivo.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|-------------|-------------------|
| Selector de líder en la UI | Una cartera puede tener decenas de líderes; Valentina tendría que cargar un archivo por líder — operativamente inviable |
| Columna LiderID en el CSV | El CSV viene de ControlNext y no incluye ese campo; requeriría transformación manual |

---

## Consecuencias

- **Positivo:** La UI solo pide seleccionar la cartera — flujo mínimo para el usuario.
- **Positivo:** Un solo archivo cubre todos los líderes de la cartera.
- **Positivo:** El lookup es un solo `IN` por lote, sin N+1.
- **Negativo:** Si un titular está asignado a varios líderes de la misma cartera (caso poco común), se toma el primer resultado. Este escenario no ocurre en el modelo actual de negocio.
- **Nota:** `AgenteControlID` también se resuelve automáticamente desde `Agente.UsuarioControlNext` usando el valor textual de la columna `UsuarioControl` del CSV.
