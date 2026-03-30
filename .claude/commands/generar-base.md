\# /generar-base



\## Descripción

Genera la base de datos Excel para un líder específico.

Invoca al agente Backend para construir la query con filtros

y al agente Frontend para actualizar la previsualización.



\## Uso

/generar-base \[nombre-lider] \[cartera] \[filtros]



\## Pasos automáticos

1\. Verificar que el líder existe y tiene carteras asignadas

2\. Construir query con filtros aplicados

3\. Previsualizar primeras 100 filas

4\. Confirmar con el usuario

5\. Generar y descargar Excel



\## Criterios de éxito

\- Excel generado con estructura fija de columnas

\- Solo datos de las carteras asignadas al líder

\- Filtros aplicados correctamente

