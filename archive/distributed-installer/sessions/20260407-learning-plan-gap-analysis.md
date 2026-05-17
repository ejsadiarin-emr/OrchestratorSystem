# Gap Analysis: Learning Plan vs Current Documentation

Date: 2026-04-07

## Scope

Compare the learning plan (`learning-plan.md`) against current documentation in `docs/distributed-installer/` to identify every requirement, artifact, concept, or detail mentioned in the learning plan that is NOT adequately covered in the current docs.

---

## Day 1 & 2 — C# Fundamentals / React

**Gap: None.** These are skill-building days. No documentation outputs expected.

---

## Day 3 — DeltaV Installer Assessment + Modernization Map

**Severity: Critical**

| Missing Artifact | Learning Plan Reference | Current Coverage |
|---|---|---|
| **Current-state assessment** (install flow, component map, tech stack) | Day 3 EOD output | 01-research-report.md mentions legacy at high level only |
| **Modernization map** (current tech → target tech → migration strategy → risk/effort) | Day 3 Afternoon Part 2 | Not addressed anywhere |
| **Legacy tech interop analysis** (C++/InstallScript/VB6/.NET Framework boundaries) | Day 3 Afternoon Part 1 | ADR-003 mentions MSI+EXE adapters but no tech-specific analysis |
| **Constraints from legacy on new design** | Day 3 EOD | Not documented |

---

## Day 4 — Deployment Options + Automation Tooling

**Severity: Important**

| Missing Artifact | Learning Plan Reference | Current Coverage |
|---|---|---|
| **Configuration persistence strategy** (backup-before-upgrade, schema versioning, config file separation) | Day 4 Afternoon Part 3 | Not addressed anywhere |
| **C#-first automation guardrails** (no scripting, data-driven manifests, what tempts devs toward scripting) | Day 4 Afternoon Part 2 | Mentioned in passing but no explicit guardrails |
| **Azure DevOps boundary clarification** (CI/CD of framework only, not workstation deployments) | Day 4 Afternoon Part 2 | Not documented anywhere |
| **Decision matrix** (installer format, deployment pattern, automation approach, config persistence) | Day 4 EOD | Partially covered in 01-research-report decision matrix but missing config persistence and automation approach |

---

## Day 5 — Requirements Definition

**Severity: Critical**

| Missing Artifact | Learning Plan Reference | Current Coverage |
|---|---|---|
| **Numbered functional requirements list** (install single/batch, upgrade delta vs full, rollback, status tracking, retry, scheduling, audit logging, pre-condition checks, post-install verification, EOL/version awareness) | Day 5 Afternoon Part 1 | 03-architecture-and-design.md has goals/scope but no numbered FRs |
| **Design principles as first-class constraints** (deterministic, testable, modular, distributed) | Day 5 Afternoon Part 2 | Not explicitly documented as constraints |
| **Comprehensive NFRs** (scalability tens-hundreds, self-contained zero-prereq, no internet/cloud, automation-friendliness with CLI+REST, heterogeneous package support, installation timing observability) | Day 5 Afternoon Part 3 | Scattered across docs but not consolidated; missing self-contained constraint, no-cloud constraint, heterogeneous package support |
| **EOL/version awareness per installed component** | Day 5 Afternoon Part 1 | Not addressed anywhere |
| **Config preservation during upgrades** | Day 5 Afternoon Part 1 | Not addressed anywhere |

---

## Day 6 — Security Design and Threat Modeling

**Severity: Critical**

| Missing Artifact | Learning Plan Reference | Current Coverage |
|---|---|---|
| **Data Flow Diagram (DFD)** | Day 6 Morning Part 1 | Architecture diagram exists but not a security-focused DFD with trust boundaries |
| **Threat surface map** (asset → trust boundary → attacker → attack vector) | Day 6 Morning Part 2 | Not documented |
| **Detailed STRIDE threat register** (with likelihood, impact, mitigation per threat) | Day 6 Afternoon Part 1 | 07-security-reliability-observability.md has STRIDE highlights but no likelihood/impact scoring |
| **Secure coding checklist for C#/.NET** | Day 6 Afternoon Part 3 | Not documented anywhere |
| **Security implementation patterns** (AuthenticodeSignatureHelper, cert thumbprint validation, IAuthorizationPolicy, ProcessStartInfo sanitization, hash chaining, DPAPI helper) | Day 6 Afternoon Part 2 | ADR-006 has high-level decisions but no implementation patterns |
| **Security requirements addendum** | Day 6 EOD | Not a separate document |

---

## Day 7 — Architecture Document

**Severity: Important**

| Missing Artifact | Learning Plan Reference | Current Coverage |
|---|---|---|
| **Deployment scenario sequences** (Fresh Install, Upgrade with config preservation, Rollback, Uninstall, Silent/Automated, Batch across nodes) | Day 7 Afternoon Part 1 | install-sequence.mmd covers happy path only |
| **Upgrade + config lifecycle contract** | Day 7 Afternoon Part 2 | Not addressed anywhere |
| **CLI interface design** (install, upgrade, rollback, status, list-nodes commands via System.CommandLine) | Day 7 Afternoon Part 4 | Not documented anywhere |
| **Self-contained deployment architecture** (dotnet publish flags, embedded Kestrel, React as embedded static files) | Day 7 EOD | ADR-005 mentions self-contained but no build/packaging details |
| **SCCM integration via REST callback hooks** | Day 7 Afternoon Part 4 | Not addressed |
| **UNC share staging for large media (SQL Server ISO)** | Day 7 Afternoon Part 4 | Mentioned as "UNC/HTTPS" but no staging strategy |

---

## Day 8 — Detailed Design: Core Components

**Severity: Critical**

| Missing Artifact | Learning Plan Reference | Current Coverage |
|---|---|---|
| **OpenAPI endpoint list** (request/response schemas) | Day 8 Morning | Not documented anywhere |
| **Data models** (Package, InstallJob, AgentNode, UpgradeManifest, ConfigSnapshot, TelemetryEvent) | Day 8 Morning | Not documented |
| **Database/state store schema** | Day 8 Morning | Not documented |
| **IInstallStep interface definitions** (with legacy adapter module pattern) | Day 8 Afternoon Part 1 | Pipeline steps listed in 03-architecture-and-design.md but no interface definitions |
| **OTel Activity wrapping pattern** (every step wrapped, no step completes without timing) | Day 8 Afternoon Part 1 | Mentioned conceptually but no implementation pattern |
| **Agent upgrade flow sequence diagram** | Day 8 Afternoon Part 2 | Only install sequence exists |
| **Test harness strategy** (mock filesystem, mock service control, mock registry) | Day 8 EOD | Not documented |

---

## Day 9 — Detailed Design: Cross-Cutting Concerns

**Severity: Critical**

| Missing Artifact | Learning Plan Reference | Current Coverage |
|---|---|---|
| **OTel SDK setup code pattern** (ActivitySource naming, metric instrument types, Collector pipeline config) | Day 9 Morning | 07-security-reliability-observability.md has metric names but no SDK setup pattern |
| **CLI command spec** (strongly-typed commands, options, exit codes, JSON output mode) | Day 9 Afternoon Part 1 | Not documented anywhere |
| **JSON install manifest schema** | Day 9 Afternoon Part 1 | Manifest contract described in prose in 03-architecture-and-design.md but no schema |
| **Config backup schema** (what captured, where stored, format) | Day 9 Afternoon Part 2 | Not addressed anywhere |
| **Config migration contract** (version-to-version migration functions, rollback of config) | Day 9 Afternoon Part 2 | Not addressed |
| **React dashboard component breakdown** | Day 9 Afternoon Part 3 | mockups/dashboard-wireframes.md exists (high-level wireframes) but no component tree |
| **Security implementation patterns** (per Day 6 decisions integrated into IInstallStep pipeline) | Day 9 Afternoon Part 4 | Not documented |

---

## Day 10 — DevOps Pipeline

**Severity: Important** (implementation-focused, but design should be documented)

| Missing Artifact | Learning Plan Reference | Current Coverage |
|---|---|---|
| **Azure DevOps pipeline YAML** | Day 10 Morning Part 2 | Not documented |
| **Pipeline stage diagram** | Day 10 Morning Part 1 | Not documented |
| **Branch policy documentation** | Day 10 EOD | Not documented |
| **Artifact versioning strategy** (semver tied to pipeline run) | Day 10 EOD | Not documented |

---

## Summary by Severity

### Critical (blocks implementation)

1. Numbered functional requirements + NFRs (Day 5)
2. Data models + database schema + OpenAPI endpoint list (Day 8)
3. IInstallStep interface definitions + OTel wrapping pattern (Day 8)
4. JSON manifest schema (Day 9)
5. Detailed threat register + DFD + secure coding checklist (Day 6)
6. Config persistence design (backup schema + migration contract) (Day 9)
7. Modernization map for legacy technologies (Day 3)
8. Design principles as first-class constraints (Day 5)

### Important (needed before coding)

1. Deployment scenario sequences (upgrade, rollback, uninstall, batch) (Day 7)
2. CLI command spec (Day 9)
3. Self-contained deployment architecture details (Day 7)
4. Azure DevOps pipeline design (Day 10)
5. Configuration persistence strategy (Day 4)
6. Azure DevOps boundary documentation (Day 4)
7. Test harness strategy (Day 8)

### Nice-to-have

1. React dashboard component tree (wireframes exist)
2. SCCM integration details
3. UNC share staging strategy for large media
