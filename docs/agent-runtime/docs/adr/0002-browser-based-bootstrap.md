# Browser-based bootstrap replaces WinRM push script

Agent installation uses a browser-based download flow: the operator generates a single-use enrollment token in the Orchestrator UI, then opens the provided URL on the target node to download a signed agent.exe with the token embedded. This replaces the earlier WinRM/PowerShell push script approach (ADR-010). Browser-based distribution is simpler for operators (no remote PowerShell access required), produces a signed binary that can be verified, and aligns with how commercial installers like Intel Driver & Support Assistant work.

**Status**: accepted — supersedes ADR-010 (WinRM bootstrap)

**Considered Options**: (1) WinRM/PowerShell push script with remote execution, (2) Browser-based download with enrollment token, (3) MSI-based installer with embedded Orchestrator address.

**Consequences**: Bootstrap requires operator to access Orchestrator URL from target node (or transfer the binary manually). No remote PowerShell execution is needed. ADR-010 text in the PRD has been updated to reflect this change.