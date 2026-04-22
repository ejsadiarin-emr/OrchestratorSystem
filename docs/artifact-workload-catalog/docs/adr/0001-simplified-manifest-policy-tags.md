# Simplified manifest policy tags for PoC Phase 1

Removed `retryabilityClass` and `idempotencyMode` from the artifact manifest schema. Only `riskLevel` (`low | medium | high`) remains as manifest-level policy metadata. This simplification focuses PoC Phase 1 on core delivery features — artifact distribution, risk detection, status display — and defers retry/idempotency policy complexity to Hardening Phase 2.

**Status**: accepted

**Consequences**: The manifest `policyTags` block now contains only `riskLevel`. Retry and idempotency behavior will use sensible defaults in Phase 1 (at-least-once delivery, version-check idempotency) without per-artifact configuration. The Policy Engine (W2-04) still evaluates risk and displays status, but does not consume per-artifact retry/idempotency settings.