# Single update workflow with no major/minor distinction

PoC Phase 1 uses a single update workflow: pre-check → detect risk → display status in Orchestrator UI → proceed via pre-defined upgrade paths. There is no separate "major version" vs "minor patch" workflow. Both follow the same pipeline. This avoids semantic versioning complexity and keeps the implementation timeline within the April 28 demo deadline.

**Status**: accepted

**Consequences**: Workload revisions use a simple integer version (e.g., "1", "2"). Risk detection covers all version transitions uniformly. Minor version bumps and hotfixes can be explored in Phase 2 if needed.