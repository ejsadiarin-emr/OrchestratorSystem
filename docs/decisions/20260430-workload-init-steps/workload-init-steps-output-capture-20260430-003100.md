# Decision: Init Steps Output Capture (Deferred)

**Date:** 2026-04-30
**Status:** Deferred

## Q22: Stdout/Stderr Capture for Init Steps

**Decision: Retain current pattern (same as existing pipeline steps).** 

No new output capture mechanism for init steps. The `Message` field on `StepStatusPayload` and `WorkloadRunTimelineEntity` remains the only output channel — same as `AcquireArtifact`, `InstallOrUpgrade`, etc.

### Rationale

- A proper log aggregation stack (Prometheus/Loki/Grafana or similar) is planned for the future
- Once a log collector is in place, init step output can be streamed there and a `LogPath` reference stored in the timeline
- Until then, the current pattern (capture for error `Message` on failure, status-only on success) is sufficient
- No new DB columns, no new payload fields, no truncation logic needed at this stage

### Future enhancement

When log infrastructure is added:
- Stream all init step stdout/stderr to log collector
- Store log reference (e.g., Loki query label or file path) in timeline
- Add `LogPath` or `LogRef` column to `WorkloadRunTimelineEntity` if needed