# Changelog

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
