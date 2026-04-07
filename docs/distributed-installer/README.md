# Distributed Installer PoC - Research Artifacts

This folder contains the deep research, market research, design artifacts, diagrams, and mockups for the Emerson Distributed Installer Framework PoC.

## Recommended reading order

1. `docs/distributed-installer/01-research-report.md`
2. `docs/distributed-installer/02-market-and-ansible-comparison.md`
3. `docs/distributed-installer/03-poc-design-spec.md`
4. `docs/distributed-installer/04-testing-strategy.md`
5. `docs/distributed-installer/05-security-reliability-observability.md`
6. `docs/distributed-installer/adr/`

## Diagram files

- `docs/distributed-installer/diagrams/architecture.mmd`
- `docs/distributed-installer/diagrams/install-sequence.mmd`
- `docs/distributed-installer/diagrams/job-state-machine.mmd`

## ASCII diagram files (for Eraser replication)

- `docs/distributed-installer/diagrams/architecture.ascii.md`
- `docs/distributed-installer/diagrams/install-sequence.ascii.md`
- `docs/distributed-installer/diagrams/job-state-machine.ascii.md`

## Mockups and comparison artifacts

- `docs/distributed-installer/mockups/dashboard-wireframes.md`
- `docs/distributed-installer/02-market-and-ansible-comparison.md`

## Scope assumptions used in these artifacts

- PoC environment: developer machine + 1 VM.
- PoC platform focus: Windows-first agents.
- Dependency policy for PoC: OSS allowed with vetting.
- Architecture direction: hybrid control plane (custom Orchestrator + Agent, Ansible-inspired idempotent manifests).
- End-state vision remains distributed and cross-platform, but Linux support is phase 2+.

## Notes

- These documents are intentionally explicit about tradeoffs, not just happy-path recommendations.
- Any uncertain or source-conflicting areas are called out directly.
