# Hybrid control plane with custom Orchestrator and headless Agent

Enterprise Windows installations, air-gapped operation, legacy installer interop, and strong observability needs don't align with generic automation tools. Use a custom .NET Orchestrator (with embedded React UI) and a headless Agent (Windows service) architecture. The Orchestrator owns all operator visibility and control; the Agent is purely an execution target. Running a workload from the Orchestrator UI is the primary demo goal.

**Status**: accepted

**Considered Options**: (1) Ansible-style agentless push, (2) PowerShell DSC with custom reporting, (3) Custom Orchestrator + headless Agent.

**Consequences**: Strong domain fit and extensibility. Clean separation — Orchestrator handles planning, policy, and UI; Agent handles execution. No Agent UI means reduced attack surface and simpler packaging. Increased ownership responsibility compared to adopting an existing platform.

**Amendments**: Ported from ADR-001 (distributed-installer, 2026-04-06). Agent is now explicitly headless with no local UI (Decision 1). Removed "declarative idempotency concepts from Ansible-like models" — PoC uses version-check idempotency by default, not per-artifact idempotency mode (Decision 5). Added: single update workflow with no major/minor distinction (Decision 3). Running workload from Orchestrator UI is the primary demo goal (Decision 7).