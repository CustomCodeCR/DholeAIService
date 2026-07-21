# Corrección de timeout de Ollama

El error HTTP 499 a los 100 segundos era causado por `Dhole.ApiGateway`, que tenía
`HttpClient.Timeout = 100 segundos`. El gateway cancelaba la solicitud antes del
timeout configurado en el perfil de IA.

Cambios incluidos en el servicio de IA:

- El timeout interno del proveedor ya no se propaga como excepción no controlada.
- `AI.ProviderTimeout` se devuelve como HTTP 504.
- Los fallos del proveedor se devuelven como HTTP 502.
- La cancelación real del navegador sigue propagándose normalmente.

Configuración recomendada para Ollama local en CPU:

- Timeout de la conexión: 300 segundos.
- Timeout del perfil: 300 segundos.
- Máximo de salida: 512 o 768 tokens durante las pruebas.
- Gateway para `/api/ai`: 600 segundos.
