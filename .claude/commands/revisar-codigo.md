\# /revisar-codigo



\## Descripción

Activa al agente QA para revisar código entregado por Backend o Frontend

antes de hacer merge a main.



\## Uso

/revisar-codigo \[rama] \[modulo]



\## Pasos automáticos

1\. Leer código de la rama indicada

2\. Ejecutar checklist de QA completo

3\. Verificar aislamiento de datos por líder

4\. Verificar pruebas unitarias

5\. Generar reporte APROBADO / RECHAZADO con issues



\## Criterios de éxito

\- Reporte claro con severidad de issues

\- Sin hardcode de credenciales

\- Filtros por líder aplicados correctamente

\- Cobertura mínima 80% en lógica crítica

