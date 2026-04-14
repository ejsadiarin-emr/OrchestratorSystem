# Distributed Installer PoC - Design Documentation

Emerson internship PoC: Windows-first distributed installer framework in .NET + React.

## Recommended reading order

1. `00-meeting-summary-notes.md` — meeting context and stakeholder validation
2. `01-research-report.md` — deep research on architecture alternatives
3. `02-market-and-ansible-comparison.md` — market landscape and competitive analysis
4. `03-architecture-and-design.md` — **core architecture spec** (start here for implementation)
5. `04-agent-bootstrap-and-communication.md` — agent onboarding and wire protocol
6. `05-orchestration-and-validation.md` — job queue, orchestration, dry-run validation
7. `06-testing-strategy.md` — test layers and quality gates
8. `07-security-reliability-observability.md` — security baseline, reliability, OTel
9. `adr/` — architectural decision records
10. `08-requirements-contract.md` — requirements ID contract scaffold (`FR/NFR/AC`)
11. `09-security-pack.md` — DFD + STRIDE + secure coding checklist scaffold
12. `10-core-contracts-pack.md` — API/data/message/interface contract scaffold
13. `11-config-persistence-contract.md` — backup/migration/restore contract scaffold
14. `12-devops-pipeline-design-pack.md` — CI/CD stage/gate/versioning scaffold
15. `13-poc-phase1-definition-of-done.md` — PoC phase-1 signoff checklist and evidence tracker
14. `17-poc-phase1-prd-v2-capability-addendum.md` — decision-closure addendum for unresolved/partial Phase 1 points
15. `18-installation-and-operational-storyboards-canonical.md` — canonical merged storyboard flows for Phase 1
16. `poc-phase1-prd-final.md` — canonical product source of truth for PoC Phase 1
17. `poc-phase1-prd-and-implementation-tracker.md` — dependency-ordered engineering execution tracker aligned to final PRD

## Superseded / historical references

- `15-installation-and-operational-storyboards.md` — superseded by `18-installation-and-operational-storyboards-canonical.md`
- `16-installation-and-operational-storyboards-independent.md` — superseded by `18-installation-and-operational-storyboards-canonical.md`

## Diagram files

- `diagrams/architecture.mmd` / `.ascii.md`
- `diagrams/install-sequence.mmd` / `.ascii.md`
- `diagrams/job-state-machine.mmd` / `.ascii.md`

## Mockups

- `mockups/dashboard-wireframes.md`

## Session notes

- `sessions/20260407-gap-analysis-meeting-notes.md`
- `sessions/20260411-poc-phase1-spec-plan-tasklist.md`

## Scope assumptions

- PoC environment: developer machine + 1 VM
- PoC platform focus: Windows-first agents
- Dependency policy for PoC: OSS allowed with vetting
- Architecture direction: hybrid control plane (custom Orchestrator + Agent, Ansible-inspired idempotent manifests)
- End-state vision remains distributed and cross-platform; Linux support is phase 2+
