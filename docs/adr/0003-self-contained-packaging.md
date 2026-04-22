# Self-contained packaging for Orchestrator and Agent

Target environments may be air-gapped and should minimize prerequisite dependencies. Publish Orchestrator and Agent as self-contained .NET binaries — single-file where practical. The Agent is a single .exe with no embedded UI framework. Both are distributed as signed artifacts with a single update workflow (no major/minor version distinction). Bootstrap uses browser-based download of a signed agent.exe with enrollment token — no WinRM or remote PowerShell required.

**Status**: accepted

**Considered Options**: (1) Framework-dependent deployment requiring .NET runtime pre-installed, (2) Self-contained single-file publish, (3) Container-based distribution.

**Consequences**: Reduced runtime dependency friction and better portability in constrained environments. Larger binaries. Runtime and security patch cadence responsibility shifts to product team. Agent packaging is simpler since it includes no frontend bundle.

**Amendments**: Ported from ADR-005 (distributed-installer, 2026-04-06). Agent has no embedded UI framework (Decision 1). Single update workflow replaces "controlled update workflow" — no major/minor distinction (Decision 3). Browser-based bootstrap replaces WinRM push (Decision 2).