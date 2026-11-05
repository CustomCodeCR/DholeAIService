# Changelog — DholeAIService

## [1.2.0] — 2026-11-05

### Added
- Bounded batch processing of email tariff extractions with concurrent worker slots.

### Changed
- AI email dispatcher distributes 32 jobs per cycle across isolated workers.
- Model execution timeouts are bounded to avoid indefinite waiting; stale executions are recovered sooner.

### Safeguards
- Individual email-job state, PostgreSQL claim leases and Redis outbox/inbox idempotency are retained.
- No automated production deployment is authorized by this change.
