# OpenTelemetry as standard observability layer

Distributed orchestration needs correlated traces, metrics, and logs for failure diagnosis and operational trust. Adopt OpenTelemetry conventions for traces, metrics, and logs across both Orchestrator and Agent. This provides a standardized telemetry model and backend portability at the cost of early schema discipline and instrumentation effort.

**Status**: accepted

**Considered Options**: (1) Custom log formats with manual correlation, (2) Vendor-specific APM SDK, (3) OpenTelemetry as cross-vendor standard.

**Consequences**: All spans, metrics, and log entries carry workload-run correlation fields. Backend visualization uses Grafana (Loki for logs, Tempo for traces, Prometheus for metrics). Schema changes after instrumentation are expensive, so the initial schema must be deliberate.

**Amendments**: Ported from ADR-004 (distributed-installer, 2026-04-06). Re-termed: "job" → "workload run" where applicable.