# Changelog

## [1.4.2] - 2026-11-08 (candidato F9, sin publicar)

- Sincroniza release global `rt-20261107-01` a `1.13.11` tras resolver los hallazgos de CI de Config, Reports y Gateway. Bump PATCH de DholeAIService `1.4.1` a `1.4.2`, sin modificar REST, eventos, precios ni datos; flags realtime OFF y PR F9 aún DRAFT.


## [1.4.1] - 2026-11-08 (candidato F9, sin publicación)

- Sincroniza metadatos de versión global del batch Dhole Realtime `rt-20261107-01` a `1.13.10`; actualización PATCH independiente de DholeAIService `1.4.0` → `1.4.1`. No cambia contratos REST, datos, lógica de negocio ni flags; requiere gates F0–F8 y CI de SHA final antes de merge.


- F6.11 (candidato): GET autorizado `realtime-access/emailjob/{id}` devuelve snapshot mínimo de estado, intentos y fechas, sin payload, correo, resultado del modelo ni credenciales; ACL de `ExecutionView` preservada.

## [1.4.0] - 2026-11-08 (candidato F6.11, sin publicar)

- F6.11: productores realtime transaccionales para ejecuciones y trabajos de análisis IA, reutilizando Outbox existente del servicio y un Redis Stream independiente.
- Los mensajes incluyen únicamente identificador del recurso, tipo de cambio, versión-hint y trazabilidad mínima, nunca correos, documentos, prompts, respuestas, URLs privadas ni credenciales.
- El consumidor debe reconciliar mediante REST autorizado, especialmente al comparar eventos originados por réplicas distintas. Cuatro flags de batch OFF por defecto.
- Componente 1.3.9 → 1.4.0; ecosistema propuesto 1.13.0. No se ha promovido develop/master ni desplegado.

## [1.3.9] - 2026-11-06

- Cancela automáticamente trabajos Pending, RetryScheduled y Processing vinculados a correos recibidos originalmente en octubre de 2026, incluso si el trabajo AI se creó en noviembre.
- Consulta la fecha original en DataExtraction y usa la zona horaria de Costa Rica; conserva correos de noviembre, resultados completados y auditoría.
- Aborta ejecuciones activas cuando pierden el lease y bloquea la publicación tardía de resultados para trabajos cancelados.
- Pruebas de límites de octubre, retención de noviembre y cancelación de trabajos en proceso.
- Ecosistema Dhole 1.3.12, release DH-20261106-OCTOBER-EMAIL-AI-QUEUE-014.

## [1.3.8] - 2026-11-06

- Distingue ejecución de modelo completada con revisión posterior de un fallo real de inferencia en el estado operativo.
- Expone DisplayStatus=CompletedWithReview en trabajos afectados, manteniendo intactos Status/ErrorCode para recuperación y auditoría.
- Separa los contadores globales de Requiere revisión y Fallidos, con pruebas unitarias de clasificación.
- Ecosistema Dhole 1.3.11, release DH-20261106-AI-COMPLETED-REVIEW-013.

## [1.3.7] - 2026-11-06

- Conciliación en lotes de hasta 250 solicitudes AI con el estado real del correo en DataExtraction; cancela trabajos eliminados, archivados o ya cerrados antes de consumir Ollama.
- Conserva ejecuciones exitosas, auditoría, recuperación de leases y reintentos válidos; no clasifica los cierres esperados como fallos del modelo.
- Pruebas unitarias para cancelación idempotente y protección de resultados completados.
- La acción manual Retirar ahora deja el trabajo como Cancelled en vez de inflar el contador de Failed.
- Ecosistema Dhole 1.3.10, release DH-20261106-AI-SOURCE-RECONCILE-012.


## [1.3.6] - 2026-11-06

- Cierra solicitudes AI antiguas en estado Pending/RetryScheduled sin invocar Ollama ni borrar filas históricas.
- Reconoce HTTP 410 de DataExtraction en correos anteriores al mes actual y usa el estado Cancelled en vez de Failed.
- Evita reintentos y eventos fallidos para extracciones archivadas, conservando jobs completados.
- Ecosistema Dhole 1.3.9, release DH-20261106-HISTORICAL-CLOSE-011.


## [1.3.5] - 2026-11-06

- Reduce la salida de Qwen: filas tarifarias JSON omiten columnas sin dato en lugar de repetir 22 campos null por tarifa.
- Envía las 22 claves válidas y el contrato de salida directamente en el prompt, imprescindible al usar Ollama format=json sin gramática completa.
- Mantiene normalización y validaciones de importes, equipos y rutas; no trunca filas ni cambia el número máximo de fragmentos.
- Release del ecosistema Dhole 1.3.8 DH-20261106-AI-THROUGHPUT-010.


## [1.3.4] - 2026-11-06

- Mantiene Qwen 3.5 residente 15 minutos en trabajos estructurados para evitar recargas por cada correo.
- Utiliza modo JSON ligero para Qwen 3.5 y conserva validación de contenido y extracción normalizada en Dhole.
- Evita el coste repetido de gramáticas JSON complejas que causa timeouts y respuestas inválidas, sin eliminar control de concurrencia.
- Ecosistema Dhole 1.3.7, release DH-20261106-AI-RELIABILITY-009.


## [1.3.3] - 2026-11-06

- Recupera solicitudes AI en cola con resultados anteriores del mismo correo, adjunto y hash; republica el evento de finalización de forma transaccional sin reenviar al modelo.
- Limita la simultaneidad de Ollama a 2 y el batch a 16 para reducir saturación; espacía reintentos y mantiene recuperación de leases.
- Ecosistema Dhole 1.3.4, release DH-20261106-EMAIL-QUEUE-001.


## [1.3.2] - 2026-11-05

- Evita borrar ramas remotas desde el despliegue AI; las ramas de trabajo y release permanecen intactas.
- Añade reintentos de clonación frente a errores transitorios de DNS en staging y producción.

## [1.3.1] - 2026-11-05

- Habilita despliegue automático del servicio de IA en cada push a master; elimina el job skipped por autorización manual.
- Conserva staging en develop y verifica versión SemVer del release.

## [1.3.0] - 2026-11-05

- Procesamiento de hasta 32 análisis de correo por lote, cuatro slots aislados.
- Detección de trabajos antiguos por edad y métricas de rendimiento horario.
- Timeout acotado de Ollama y recuperación de ejecuciones huérfanas.
- Pruebas de presupuesto y reparto de lotes.

## [1.2.0] - 2026-11-05

- Incorporates universal Dhole SemVer policy with GitHub Actions checks.
- Synchronizes service assembly metadata with the ecosystem version.
- Adds permanent Codex instructions and coordinated release requirements.
