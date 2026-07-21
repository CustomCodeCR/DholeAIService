# Auditoría de conversaciones IA

Cada ejecución Chat publica eventos hacia AuditLogs con:

- `EntityType`: `AiChat`
- `Action`: `chat`
- eventos: `ai.chat.requested`, `ai.chat.completed`, `ai.chat.failed`
- pregunta/mensajes, variables, respuesta, modelo, proveedor, tokens, costo, duración, usuario y correlación.

Puertos configurados:

- HTTP: `5206`
- gRPC: `5307`

El contrato gRPC acepta `requested_by_name` para conservar el actor original cuando Pricing ejecuta el análisis desde su Worker.
