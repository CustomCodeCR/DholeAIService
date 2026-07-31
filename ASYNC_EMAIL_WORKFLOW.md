# Procesamiento AI asíncrono de correos

`AiPricingEmailAnalysisRequestedStreamHandler` persiste un job idempotente y
retorna. `AiEmailAnalysisWorker` obtiene el payload desde la URL interna de
DataExtraction y llama directamente a `IAiExecutionOrchestrator`; no usa el
gRPC del propio servicio.

DataExtraction solo crea la solicitud cuando la normalización determinística
queda por debajo de `AI:AutomaticExtraction:MinimumDeterministicConfidence`
(75% por defecto). El AI Worker procesa como máximo dos etapas pequeñas y en
serie. Para `pricing-email-analysis` se selecciona un único modelo por etapa,
con 120 segundos de timeout. Si el proveedor está caído, sin memoria o agota el
timeout, no se repite el mismo fallo con el segundo fragmento.

Toda entrada y salida del AI Service se registra primero en el Outbox SQL y se
publica en segundo plano a `dhole.audit.events` mediante
`audit.event.registered`. La auditoría incluye solicitud del servicio, prompt
compilado, imágenes/base64, esquema JSON, intento por proveedor, modelo,
respuesta normalizada, respuesta cruda, tokens, costo, duración, errores,
fallbacks y etapas del análisis de correo.

Configuración de despliegue:

- `AI__EmailJobs__Enabled=true`
- `AI__EmailJobs__MaxConcurrentJobs=1`
- `AI__EmailJobs__MaxJobsPerRun=1`
- `AI__EmailJobs__MaxRetryCount=1`
- `AI__EmailJobs__LeaseMinutes=10`
- `AI__EmailJobs__HeartbeatSeconds=20`
- `AI__Execution__Profiles__pricing-email-analysis__MaximumCandidates=1`
- `DataExtraction__InternalBaseUrl`: URL interna de DataExtraction.

La comunicación AI → DataExtraction no requiere API key, token ni encabezado
de autenticación. La migración `AddEmailAnalysisJobs` debe aplicarse antes de
iniciar el Worker. El perfil `pricing-email-analysis` se sincroniza con 768
tokens máximos de salida y 120 segundos de timeout.
