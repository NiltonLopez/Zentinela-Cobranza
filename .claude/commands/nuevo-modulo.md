\# /nuevo-modulo



\## Descripción

Inicia el ciclo completo de desarrollo de un nuevo módulo de Zentinela.

El orquestador coordina todos los agentes desde cero.



\## Uso

/nuevo-modulo \[nombre-modulo] \[descripcion]



\## Pasos automáticos

1\. Orquestador analiza requerimiento y define criterios de aceptación

2\. Orquestador delega diseño de API al agente Backend

3\. Orquestador delega diseño de UI al agente Frontend

4\. Backend y Frontend desarrollan en paralelo en sus worktrees

5\. QA revisa ambos entregables

6\. Docs documenta el módulo completo

7\. Orquestador consolida y hace merge a main

8\. Reporte final a Nilton



\## Criterios de éxito

\- Módulo funcional end-to-end

\- Pruebas unitarias presentes

\- Documentación completa

\- Sin regresiones en módulos existentes

