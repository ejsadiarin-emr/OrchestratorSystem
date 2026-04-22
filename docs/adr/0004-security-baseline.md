# Security baseline: signed artifacts, RBAC, least privilege

Installer frameworks are privileged systems with high blast radius if compromised. The PoC must enforce: artifact signature/hash validation, role-based authorization, least-privilege execution posture, and an auditable append-only event model. The headless Agent further reduces attack surface — no local HTTP endpoint, no UI server, no remote PowerShell listener. Browser-based bootstrap (signed agent.exe + enrollment token) replaces WinRM push, removing an entire remote-execution attack class.

**Status**: accepted

**Considered Options**: (1) Trust-on-first-use with no signing, (2) Signed artifacts with RBAC and least privilege, (3) Full zero-trust mTLS mesh with certificate pinning.

**Consequences**: Substantially reduced spoofing, tampering, and misuse risk. Better auditability and stakeholder confidence. Increased implementation complexity versus unsecured prototypes.

**Amendments**: Ported from ADR-006 (distributed-installer, 2026-04-06). Added: Agent headless posture reduces attack surface — no local UI endpoint (Decision 1). Browser-based bootstrap removes remote-execution attack class (Decision 2).