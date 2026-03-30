\# /documentar-modulo



\## Descripción

Activa al agente Docs para documentar un módulo recién aprobado por QA.



\## Uso

/documentar-modulo \[nombre-modulo]



\## Pasos automáticos

1\. Leer código aprobado del módulo

2\. Generar comentarios XML en C# para lógica compleja

3\. Actualizar README del módulo en docs/

4\. Registrar decisiones arquitectónicas en docs/decisions/

5\. Actualizar schema.md si hubo cambios en BD



\## Criterios de éxito

\- Documentación en español

\- ADR registrado si hubo decisión arquitectónica

\- schema.md actualizado si aplica

