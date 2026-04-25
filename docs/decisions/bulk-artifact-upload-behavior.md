# Decision: Bulk Artifact Upload Partial Ingestion

**Date:** 2026-04-25

**Context:** When uploading a bulk zip containing multiple artifacts, some may pass validation while others fail. What should happen?

**Decision:** Partial ingestion - ingest successful artifacts, report failures separately.

## Behavior

| Scenario | Action |
|----------|--------|
| All pass | All ingested |
| Some pass, some fail | Pass ingested; failures reported separately |

## Rationale

- Allows work to continue even if one artifact has issues
- Avoids blocking valid artifacts due to a single bad actor
- Failures can be addressed independently

## Status

Accepted