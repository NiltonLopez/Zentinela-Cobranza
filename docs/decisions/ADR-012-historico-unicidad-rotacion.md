# ADR-012 — HistoricoEstadoTitular: unicidad por Rotacion y FechaActualizacion

**Estado:** Aceptado  
**Fecha:** 2026-04-24  
**Módulos afectados:** Cargue de Reportes → HistoricoEstadoTitular

---

## Contexto

`HistoricoEstadoTitular` acumula el estado de gestión de cada titular a lo largo del tiempo. El problema inicial era determinar qué hace única a una fila histórica:

- Usando solo `(TitularID, CarteraID, AgenteID)` → el histórico solo almacenaba la **primera** gestión; cargas posteriores no insertaban nada.
- Sin ninguna restricción → cada cargue del mismo archivo duplicaba todos los registros.

---

## Decisión

La clave semántica del histórico es `(TitularID, CarteraID, AgenteID, Rotacion)`.

**Por qué `Rotacion`:**  
`Rotacion` es el contador de veces que el titular fue gestionado, calculado por el call center. Cuando `Rotacion` no cambia entre dos cargas, el estado es idéntico — es el mismo evento re-reportado. Cuando `Rotacion` sube, hay una gestión nueva que merece un nuevo registro histórico.

**Comportamiento del MERGE:**

```sql
MERGE HistoricoEstadoTitular ON (TitularID, CarteraID, AgenteID, ISNULL(Rotacion,-1))
  WHEN MATCHED     → UPDATE SET FechaActualizacion = DateTime.Now
  WHEN NOT MATCHED → INSERT (estado completo + FechaActualizacion)
```

| Escenario | Rotacion | Resultado |
|---|---|---|
| Re-carga del mismo archivo | igual | UPDATE FechaActualizacion — sin duplicado |
| Nuevo ciclo de gestión | mayor | INSERT — estado nuevo en el tiempo |
| Carga de archivo viejo | ya existe | UPDATE FechaActualizacion — no contamina |

**`FechaActualizacion` en el histórico:**  
Indica cuándo Zentinela procesó por última vez ese estado específico. Es útil para auditoría y para saber si un estado fue actualizado recientemente aunque la gestión haya ocurrido hace días.

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Solo `(TitularID, CarteraID, AgenteID)` | El histórico jamás crece después del primer cargue |
| `(TitularID, CarteraID, AgenteID, FechaUltimaGestion)` | Frágil: re-reportes del mismo ciclo con distinta fecha generan duplicados |
| Sin restricción (append siempre) | Duplicados masivos en cada re-carga |
| `(TitularID, CarteraID, AgenteID, FechaUltimaGestion OR Rotacion)` | Implementado y luego descartado: la condición OR es más restrictiva de lo necesario — una doble gestión errónea el mismo día con Rotacion diferente es una gestión real accidental y debe registrarse |
