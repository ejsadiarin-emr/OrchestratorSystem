# PoC Phase 1 PRD v2 Capability Addendum

Date: 2026-04-14  
Status: Decision closure complete for Phase 1 baseline  
Companion docs:
- `docs/distributed-installer/poc-phase1-prd-final.md`
- `docs/distributed-installer/18-installation-and-operational-storyboards-canonical.md`
- `docs/distributed-installer/sessions/20260413-storyboard-review-output.md`
- `docs/distributed-installer/sessions/20260413-raw-notes-shared-understanding.md`

## Purpose

Close unresolved/partial Phase 1 decisions so implementation can proceed without policy ambiguity.

## Decision closure matrix

| ID | Topic | Current state | Decision to lock | Owner | Due | Status |
|---|---|---|---|---|---|---|
| Q11 | mTLS lifecycle detail | Partial | Enrollment token is one-time, then steady-state reconnect requires bound mTLS identity; cert rotation/revocation operations are deferred to Phase 2 hardening runbooks | Security | 2026-04-14 | Closed |
| Q13 | Package ingestion UX baseline | Partial | Runtime installation source is orchestrator artifact store only; package ingestion is API-first (UI upload supported but not required for signoff) | Product + Backend | 2026-04-14 | Closed |
| Q15 | Package channel taxonomy | Partial | Minimum channel taxonomy locked (`stable`, `canary`, `test`) with immutable version identity and hash-bound manifest metadata | Product + Backend | 2026-04-14 | Closed |
| Q16 | Serial vs parallel execution | Open | Execution is serial per node (step-ordered pipeline) and parallel across nodes with bounded concurrency for PoC | Backend + Agent | 2026-04-14 | Closed |
| Q21 | OTel storage default | Partial | Default is file-based OTel export with rotation/retention; collector/backend stack is Phase 2 option | Platform + Security | 2026-04-14 | Closed |
| Q22 | OTel data exposure controls | Open | Redaction/denylist and least-privilege access are required; no plaintext secrets/tokens/credentials in logs | Security | 2026-04-14 | Closed |
| Q23 | Ping vs LeaseHeartbeat semantics | Partial | `Ping` is liveness/connectivity signal; `LeaseHeartbeat` is assignment ownership/lease freshness signal | Backend | 2026-04-14 | Closed |
| Q24 | Windows-first scope line | Partial | Linux remains optional/non-blocking and is not required for Phase 1 acceptance | Product | 2026-04-14 | Closed |
| Q26 | Scale assumptions | Partial | Phase 1 assumes single orchestrator; multi-orchestrator/HA guarantees are deferred to Phase 2 | Product + Architecture | 2026-04-14 | Closed |

## Mandatory rewrites from storyboard review

1. Replace in-place orchestrator self-update primary flow with staged swap + supervisor/wrapper pattern.
2. Remove retry contradiction (`exit code 1` handling) and make retry decisions reason-code/policy-driven.
3. Replace global `rollbackGuaranteed` language with package-specific tested rollback contracts.
4. Define signing authority + key custody minimum process for PoC.
5. Keep SignalR for control/status only; artifact transfer uses HTTP endpoints with optional range/chunk.

## Exit criteria for this addendum

- [x] All rows in decision closure matrix are marked `Closed` with documented values.
- [x] Canonical storyboard and final PRD updated to reflect closed decisions.
- [x] Implementation tracker references final PRD + canonical storyboard and removes unresolved policy ambiguity.
