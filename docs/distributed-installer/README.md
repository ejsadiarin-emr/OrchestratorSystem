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

## Diagram files

- `diagrams/architecture.mmd` / `.ascii.md`
- `diagrams/install-sequence.mmd` / `.ascii.md`
- `diagrams/job-state-machine.mmd` / `.ascii.md`

## Mockups

- `mockups/dashboard-wireframes.md`

## Session notes

- `sessions/20260407-gap-analysis-meeting-notes.md`

## Scope assumptions

- PoC environment: developer machine + 1 VM
- PoC platform focus: Windows-first agents
- Dependency policy for PoC: OSS allowed with vetting
- Architecture direction: hybrid control plane (custom Orchestrator + Agent, Ansible-inspired idempotent manifests)
- End-state vision remains distributed and cross-platform; Linux support is phase 2+
