# Changelog

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
