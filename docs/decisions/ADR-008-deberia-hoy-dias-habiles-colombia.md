# ADR-008 — "Debería hoy" usa días hábiles del calendario colombiano

**Estado:** Aceptado  
**Fecha:** 2026-04-20  
**Módulos afectados:** Informes de Recaudo (todos los tabs con campo "Debería hoy")

---

## Contexto

El campo **"Debería hoy"** representa la porción de la meta de honorarios que debería estar
recaudada a la fecha actual si el ritmo de recaudo fuera lineal durante el mes. La fórmula
original usaba días calendario:

```
DeberiaHoy = Meta × (diasTranscurridos / diasDelMes)
```

Esto sobreestimaba la exigencia real porque asumía que se cobra igual los sábados, domingos
y festivos, cuando en la práctica la operación de cobranza solo opera en días hábiles.

---

## Decisión

Reemplazar el denominador y el numerador de la fórmula por **días hábiles** del calendario
oficial de Colombia:

```
DeberiaHoy = Meta × (diasHabilesTranscurridos / diasHabilesDelMes)
```

### Qué cuenta como día hábil
- Lunes a viernes
- Que no sea festivo oficial colombiano

### Festivos hardcodeados
Se hardcodearon los festivos 2024–2027 en `_festivosColombia` (HashSet<DateOnly>) dentro de
`InformesRecaudoService`. Incluye:

| Tipo | Ejemplos |
|---|---|
| Fijos | Año Nuevo, Día del Trabajo, Independencia (Jul 20), Boyacá, Inmaculada, Navidad |
| Ley Emiliani (traslado al lunes siguiente) | Reyes Magos, San José, San Pedro y San Pablo, Asunción, Día de la Raza, Todos los Santos, Independencia de Cartagena |
| Semana Santa | Jueves Santo, Viernes Santo |
| Móviles de Pascua (traslado al lunes siguiente) | Ascensión (+39), Corpus Christi (+60), Sagrado Corazón (+68) |

Fechas de Pascua usadas: 2024-03-31 · 2025-04-20 · 2026-04-05 · 2027-03-28

### Qué NO cambió
`ProyeccionCierre` sigue usando días calendario porque es una proyección de la tasa de
recaudo real observada, no una distribución lineal de la meta.

---

## Implementación

`InformesRecaudoService.CalcularDiasHabiles(año, mes)` itera el mes día a día y cuenta
los hábiles, retornando `(DiasHabilesDelMes, DiasHabilesTranscurridos)`. El método
`EsDiaHabil(d)` comprueba día de semana y membresía en `_festivosColombia`.

Ambos métodos del servicio que calculan `deberiaHoy` (`GetInformeAgentesAsync` y
`GetInformeLideresAsync`) llaman a `CalcularDiasHabiles` adicionalmente al existente
`CalcularDias` (que se mantiene para proyección y otros usos).

---

## Alternativas descartadas

| Alternativa | Razón de descarte |
|---|---|
| Calcular festivos dinámicamente (algoritmo de Pascua + Emiliani) | Mayor complejidad para un horizonte de 4 años; el cálculo dinámico puede diferir de la lista oficial publicada por el gobierno |
| Mantener días calendario | Sobreestima la exigencia los fines de semana y festivos; no refleja la realidad operativa |
| Mover lógica al frontend | La fórmula de negocio debe vivir en el backend para que sea consistente en cualquier cliente futuro |
