# EJ Sadiarin - Learning Plan: On-Prem Distributed Installer Framework PoC (React + C#)

> **Context**: On-premises solution for installing and upgrading various software (e.g., SQL Server, DeltaV components, workstation tools) across multiple Windows workstations in an air-gapped or enterprise LAN environment. No cloud dependencies.

---

## Day 1 — C# and .NET Fundamentals

**Goal**: Get comfortable writing C# and building a testable REST API with proper structure

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning | **C# crash course** — syntax, types, classes, interfaces, async/await, LINQ basics | [C# for Java devs cheat sheet](https://docs.microsoft.com/en-us/dotnet/csharp/), map concepts from TypeScript/Java |
| Afternoon (Part 1) | **Advanced C# concepts** — **Generics**: generic classes, methods, constraints (`where T : IInstallStep`), why generics enable type-safe, reusable pipeline and repository patterns without sacrificing compile-time safety; **Dependency Injection**: the DI container in .NET (`IServiceCollection`, `IServiceProvider`), constructor injection, interface-based design, registering services (transient/scoped/singleton), why DI is essential for testability (swap real implementations with mocks); **other concepts as needed**: extension methods, records, pattern matching, `IOptions<T>` for typed configuration | Write a generic pipeline runner `Pipeline<TContext>` and wire it up via DI as a hands-on exercise |
| Afternoon (Part 2) | **MVC Pattern + ASP.NET Core Web API** — understand Model-View-Controller separation; how ASP.NET Core implements MVC (Models for data shape, Controllers for logic/routing, React as the View layer); create a basic API project applying the DI and generics patterns from the earlier session | `dotnet new webapi`, organize into Models/Controllers/Services folders, inject services via constructor, build a simple endpoint |
| EOD | **Testing in C#** — xUnit or NUnit basics, writing unit tests for services, mocking with Moq (leveraging the DI-friendly interfaces defined earlier), running tests via `dotnet test` | Write tests for the endpoints built earlier; verify that DI and interface boundaries make mocking straightforward |

**Key mindset**: C# = Java + TypeScript mixed. Focus on `async Task`, `IServiceCollection`, `IConfiguration`, and designing around interfaces from the start — generics and DI are foundational to how the installer pipeline will be structured.

---

## Day 2 — React Standalone

**Goal**: Understand React outside of Next.js and establish UI testing practices

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning | **React without Next.js** — Vite + React setup, `useState`, `useEffect`, `fetch`/`axios` for API calls, component structure | Scaffold with `npm create vite@latest`, call his Day 1 C# API |
| Afternoon | **UI Testing in React** — React Testing Library basics (`render`, `screen`, `userEvent`), writing component and integration tests | Write tests for the components built in the morning |
| EOD | **E2E UI testing** — overview of Playwright for React; write one simple end-to-end test | Validates full flow from UI perspective |

---

## Day 3 — Understanding Current DeltaV Installer + Modernization Context

**Goal**: Build contextual understanding of the existing DeltaV installer and what modernization means given the current technology mix

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning | **Explore the DeltaV Installer and existing installer frameworks** — walk through the current installer codebase (guided by architect/team); understand the install flow end-to-end: what gets installed, how components are structured, how upgrades work today, where failures typically occur, known pain points and fragile areas | Code walkthrough with a guide; take notes on structure, key files, install sequence, and what is hard to change today |
| Afternoon (Part 1) | **Understanding the legacy technology mix** — orient to each technology in the current codebase: **C++** (native installer logic, COM components), **InstallScript** (InstallShield scripting language — how it controls MSI/ISM projects, custom actions), **VB6** (legacy UI or business logic), **.NET Framework C#** (managed code in the existing installer, contrast with modern .NET); understand why each exists and what role it plays | Research each tech briefly; map which parts of the installer use which technology; note interop boundaries |
| Afternoon (Part 2) | **Modernization paths research** — for each technology, identify the realistic migration path: C++ → modern C++ or managed C#/P-Invoke; InstallScript/InstallShield → WiX Toolset, MSIX, or custom pipeline; VB6 → C# (COM interop as bridge, phased rewrite); .NET Framework C# → modern .NET (dotnet upgrade-assistant, breaking change review); identify what can be incrementally modernized vs. what requires full rewrite | Document a **modernization map**: current tech → target tech → migration strategy → risk/effort level |
| EOD | **Integration with PoC scope** — given what was learned, identify which parts of the current installer the new framework would replace, wrap, or complement; note any constraints legacy components impose on the new design (e.g., must coexist with existing MSI, must call legacy custom actions) | Feed findings directly into Day 5 requirements and Day 7 architecture |

**Output**: A current-state assessment (install flow, component map, tech stack) and a modernization map covering each legacy technology with migration strategy and risk level.

---

## Day 4 — Deployment Options + Automation Tooling Research

**Goal**: Understand the full landscape of deployment options and the tooling ecosystem around installer automation

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning | **Windows Installer formats** — MSI, MSIX, EXE (NSIS/Inno Setup), ClickOnce, WiX Toolset; compare use cases, pros/cons, Windows compatibility, and support for silent/automated execution | Research and take notes; focus on what fits a self-contained React + C# app |
| Afternoon (Part 1) | **On-prem deployment patterns** — self-contained .NET executables (`dotnet publish --self-contained`), xcopy/zip via network share (UNC paths), Kestrel-hosted API (no IIS), CI-driven deployment via Azure DevOps (on-prem agent pools); understand how packages are staged and distributed on a local network without internet access | Compare tradeoffs: push vs. pull, attended vs. silent, network share vs. embedded package repo |
| Afternoon (Part 2) | **C#-first automation approach** — the framework exposes a strongly-typed REST API and CLI as its automation surface; all orchestration logic is C# code, not scripts; understand the role of Azure DevOps in this system: **Azure DevOps is used for CI/CD of the framework itself** (building, testing, and publishing the Orchestrator and Agent binaries to the deployment server) — it does **not** push installs directly to workstations; actual workstation deployments are triggered through the Orchestrator's REST API or CLI by an operator or automated caller; explore WMI and Windows Service Control Manager (SCM) APIs callable from C# for remote service management; note: **scripting languages (PowerShell, batch, VBScript) are highly discouraged** — data-driven configuration (JSON/XML manifests) is preferred over any scripting; embedded scripting engines (e.g., Lua) are only acceptable if they are an explicit, controlled part of the framework design | Identify what can be fully expressed in C# typed code vs. what tempts developers toward scripting, and define guardrails |
| Afternoon (Part 3) | **Configuration persistence strategies** — how do upgrades preserve existing configurations? Research patterns: backup-before-upgrade, config schema versioning and migration, separation of install artifacts from user config files, registry vs. file-based config | Take notes; will feed directly into requirements |
| EOD | **Decision matrix** — summarize all findings: installer format, deployment pattern, automation approach, config persistence strategy | 1-pager to guide requirements definition |

---

## Day 5 — Requirements Definition: Distributed Installer Framework

**Goal**: Produce a complete requirements document covering all dimensions of the framework

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning | **Scope and problem framing** — define what "distributed" means in this on-prem context: multiple Windows workstations on a LAN, centralized orchestration server on the same network, remote trigger and status visibility with no internet dependency; define the range of software that must be installable (e.g., SQL Server, DeltaV components, workstation utilities); identify stakeholders and their needs; define success criteria for the PoC; incorporate constraints from the Day 3 legacy codebase review | Draft a problem statement, assumptions list, and PoC scope boundary; note on-prem-specific constraints (no cloud, possible air-gap, domain or workgroup auth) |
| Afternoon (Part 1) | **Functional requirements** — installation (single/batch), upgrade (delta vs. full, with config preservation), rollback, status tracking per node, retry on failure, install scheduling/queuing, audit logging, pre-condition checks, post-install verification, EOL/version awareness per installed component | Create a numbered functional requirements list |
| Afternoon (Part 2) | **Design principles as requirements** — the installer framework must be: **deterministic** (same inputs always produce same outcome), **testable** (each module independently testable with no side effects on real machines), **modular** (installer logic separated into discrete pluggable units — e.g., pre-check, copy files, configure, verify), **distributed** (Orchestrator coordinates agents across many nodes without central bottleneck) | Document these as first-class architectural constraints |
| Afternoon (Part 3) | **Non-functional requirements** — scalability (tens to hundreds of workstations on LAN), reliability, security (signed packages, agent auth via Windows credentials or API key — no cloud identity provider), **self-contained** (zero prerequisites on any machine — no .NET runtime, no IIS, no dependencies), **no internet/cloud dependency** (all operations resolvable on a closed network), observability/telemetry (log and metric store must be on-prem, **OpenTelemetry-based**), offline/degraded node handling, idempotency, **automation-friendliness** (CLI-operable, REST-drivable — **no scripting languages**; all automation expressed in strongly-typed C# or data-driven manifests; Azure DevOps is used only for CI/CD of the framework binaries, not for direct workstation deployments), **legacy coexistence** (must be able to invoke or wrap existing legacy installer components during transition period), **heterogeneous package support** (must handle diverse installers: SQL Server, MSI-based components, ZIP-deployed services, custom installers), **installation timing and duration observability** (every pipeline step must emit start/end timing via OpenTelemetry spans) | Add NFRs to the document |
| EOD | **Requirements review** — walk through the full draft, identify gaps, prioritize must-have vs. nice-to-have for PoC scope | Finalized requirements doc |

**Output**: Requirements document covering functional requirements, design principles (deterministic/testable/modular/distributed), NFRs including legacy coexistence and self-contained constraints.

---

## Day 6 — Security Design and Threat Modeling

**Goal**: Identify the attack surface of the distributed installer framework, enumerate threats, and produce a security design that directly informs the Day 7 architecture

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning (Part 1) | **Threat modeling methodology** — learn STRIDE (Spoofing, Tampering, Repudiation, Information Disclosure, Denial of Service, Elevation of Privilege) as the structured approach; understand how to define a system boundary, enumerate trust boundaries, and draw a Data Flow Diagram (DFD) for a distributed system | Draw the DFD for the installer framework: Orchestrator, Agent, Package Repository, Operator UI, CI pipeline artifact drop, LAN communication channels |
| Morning (Part 2) | **Attack surface analysis** — enumerate every trust boundary and entry point: Orchestrator REST API (who can call it?), Agent command receiver (how does it know the Orchestrator is legitimate?), Package Repository (can a package be tampered with in transit or at rest?), installer pipeline execution (what privilege does the agent run installs under?), audit logs (can they be tampered with?), UNC share for large media (can a malicious file be substituted?) | Produce a threat surface map: asset → trust boundary → potential attacker → attack vector |
| Afternoon (Part 1) | **STRIDE threat enumeration** — apply STRIDE to each component; key threats to explore: (1) **Spoofing** — rogue agent impersonating a legitimate agent; operator UI faking install triggers; (2) **Tampering** — package contents modified after signing; config backup files altered before restore; (3) **Repudiation** — install actions with no verifiable audit trail; (4) **Information Disclosure** — agent leaking credentials or config contents over LAN; (5) **Denial of Service** — flooding the Orchestrator with fake agent registrations; (6) **Elevation of Privilege** — installer pipeline step executing arbitrary code at SYSTEM level via legacy adapter shell-out | Document each threat with: description, affected component, likelihood, impact, and proposed mitigation |
| Afternoon (Part 2) | **Security design decisions** — define mitigations that will feed directly into architecture: (1) **Package signing** — Authenticode code signing for all binaries; package manifest hash verification before execution; (2) **Orchestrator–Agent mutual authentication** — certificate-based (self-signed PKI on LAN) or pre-shared API key with HMAC request signing; (3) **REST API authorization** — RBAC model: define roles (Admin, Operator, ReadOnly) and what each can do; (4) **Least privilege execution** — installer pipeline steps run at the minimum required privilege; UAC elevation handled explicitly, not silently; legacy adapter shell-outs sanitize all parameters to prevent injection; (5) **Credential and key storage** — use DPAPI or Windows Credential Manager; no secrets in config files or logs; (6) **Audit log integrity** — append-only audit log with tamper-evidence (hash chaining or write to a separate store the agent cannot modify) | Document each mitigation as a security design decision; note which become architecture constraints |
| Afternoon (Part 3) | **Secure coding practices for C#/.NET** — input validation and allowlisting for all external inputs (manifest parameters, UNC paths, package names); no string interpolation into process arguments in legacy adapter modules (prevents command injection); ASP.NET Core security middleware (HTTPS even on LAN, rate limiting on REST API, request size limits); avoid logging sensitive values (credentials, keys, PII); use `SecureString` or `MemoryMarshal` patterns where secrets are handled in memory | Review OWASP guidance for .NET APIs; produce a secure coding checklist specific to the installer framework |
| EOD | **Security requirements addendum + ADR stubs** — add a security section to the Day 5 requirements doc covering: auth model, package integrity, privilege model, audit trail, and data classification; stub out security ADRs for each major decision (auth mechanism choice, package signing approach, privilege execution model) | Security addendum doc + security ADR stubs ready for Day 7 architecture |

**Output**: Threat model document (DFD + STRIDE threat register), security design decisions document, secure coding checklist, and security requirements addendum.

---

## Day 7 — Architecture Document

**Goal**: Translate requirements into a complete high-level architecture that addresses all deployment scenarios, observability, automation, and the modernization transition

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning | **Component architecture** — identify and define all major components: Orchestrator (C# self-contained API + embedded React UI, runs on a designated on-prem server), Agent (self-contained C# executable on each target workstation, communicates only over LAN), Package Repository (on-prem file share or embedded store for versioned installer packages including SQL Server media, MSIs, ZIPs), Manifest Service (version/EOL metadata), Status/Event Store (on-prem SQLite or SQL Server) | Sketch a component diagram; define all network communication paths and confirm they are LAN-only |
| Afternoon (Part 1) | **Deployment section architecture** — define distinct deployment flows and how each is handled: (1) Fresh Install, (2) Upgrade with config preservation, (3) Rollback, (4) Uninstall, (5) Silent/Automated (no UI, driven by CLI or API), (6) Batch across multiple nodes | Sequence diagrams for each deployment scenario |
| Afternoon (Part 2) | **Upgrade and configuration persistence architecture** — how config files are backed up before upgrade, config schema versioning and migration on upgrade, separation of install artifacts from user config, rollback of config if upgrade fails | Define the upgrade + config lifecycle contract |
| Afternoon (Part 3) | **Observability and telemetry architecture (OpenTelemetry)** — adopt **OpenTelemetry** as the single observability standard: structured logs, metrics, and distributed traces all emitted via OTel SDK; define the OTel pipeline (OTel Collector on-prem, exporting to on-prem backends e.g. Seq for logs, Prometheus + Grafana for metrics, Jaeger/Zipkin for traces); **installation duration hooks**: every installer pipeline step wrapped in an OTel Activity (span) capturing start time, end time, duration, step name, node ID, package ID, and outcome — enabling drill-down into exactly which step is slow or failing; correlation IDs propagated from Orchestrator through Agent via W3C TraceContext headers | Define OTel instrumentation plan: which SDK packages (`OpenTelemetry`, `OpenTelemetry.Extensions.Hosting`), span naming conventions, metric names (e.g. `installer.step.duration`, `installer.job.success_rate`), and what the Grafana/dashboard view looks like |
| Afternoon (Part 4) | **C#-first automation architecture** — CLI interface design (commands: `install`, `upgrade`, `rollback`, `status`, `list-nodes`) implemented as a strongly-typed C# CLI tool (`System.CommandLine`); REST API as the primary automation surface; **Azure DevOps scope is limited to CI/CD of the framework itself** — pipelines build the Orchestrator and Agent binaries, run tests, and deploy the Orchestrator binary to the designated deployment server; workstation deployments are never triggered directly from Azure DevOps pipelines — they are always initiated through the Orchestrator (by operator or automated REST call); **no PowerShell, no bash, no batch** in the automation surface; data-driven job definitions via JSON manifests (describe what to install, in what order, with what parameters) instead of scripts; SCCM (on-prem) integration via REST callback hooks; define how large installer media (e.g., SQL Server ISO) is staged via UNC share referenced in the manifest | Define the strongly-typed automation contract: CLI spec, REST endpoint list, manifest schema; clearly document Azure DevOps boundary (build/deploy framework only) |
| Afternoon (Part 5) | **Modernization and legacy interop architecture** — how the new framework coexists with and gradually replaces legacy C++/InstallScript/VB6 components; define the interop boundary (e.g., Agent can invoke legacy MSI/InstallScript as a pipeline step); phased modernization strategy: wrap → re-implement → retire | Document the interop contract and modernization phases as part of the architecture |
| EOD | **Self-contained deployment architecture + ADR stubs** — how both Orchestrator and Agent achieve zero-prerequisite deployment; `dotnet publish --self-contained --runtime win-x64 -p:PublishSingleFile=true`; embedded Kestrel; React as embedded static files | Document build/packaging strategy; stub out ADRs for key decisions |

**Output**: Architecture document with component diagram, deployment scenario sequences, observability model, automation contract, upgrade/config strategy, security architecture, legacy interop and modernization phases, and ADR stubs.

---

## Day 8 — Detailed Design Document: Part 1 (Core Components)

**Goal**: Define concrete interfaces, data models, and flows for the Orchestrator and Agent

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning | **Orchestrator detailed design** — API contracts (OpenAPI-style endpoint list with request/response schemas), service layer breakdown, data models (Package, InstallJob, AgentNode, UpgradeManifest, ConfigSnapshot, TelemetryEvent), database/state store schema | OpenAPI endpoint list + ER/data model diagram |
| Afternoon (Part 1) | **Modular installer pipeline design** — define the installer as a pipeline of discrete, independently testable C# modules: PreConditionCheck → Backup Config → Stop Services → Copy Files → Apply Config Migration → Start Services → PostInstallVerify → EmitTelemetry; each module implements a strongly-typed `IInstallStep` interface with defined inputs/outputs; **each step is wrapped in an OpenTelemetry Activity (span)** capturing duration, outcome, and relevant attributes — no step completes without emitting timing data; pipeline composition is data-driven (JSON manifest describes step sequence and parameters) not scripted; define how legacy steps (e.g., invoke existing InstallScript custom action) are wrapped as C# adapter modules that shell out to legacy processes while still emitting OTel spans | Interface definitions for each `IInstallStep` module including legacy adapter module pattern; define the OTel Activity wrapping pattern used in every step |
| Afternoon (Part 2) | **Agent detailed design** — agent lifecycle (registration, heartbeat, command polling/subscription), installer pipeline execution, config backup and restore, status reporting contract, local state management, error handling and retry logic, determinism guarantees (idempotent execution) | Sequence diagram for agent install and upgrade flows |
| EOD | **Testability design** — test harness strategy: how to test installer modules without a real target machine (mock filesystem, mock service control, mock registry); integration test strategy using lightweight sandboxes or containers; how to test legacy adapter modules with stubs | Define the test harness interface and what each test level covers |

---

## Day 9 — Detailed Design Document: Part 2 (Cross-Cutting Concerns)

**Goal**: Complete the design document with observability, automation tooling, dashboard, modernization strategy, and security detailed design

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning | **OpenTelemetry detailed design** — implement observability using the OTel .NET SDK (`OpenTelemetry`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`); define: (1) **Traces** — one root span per install job, child spans per pipeline step, propagated from Orchestrator to Agent via W3C TraceContext HTTP headers; (2) **Metrics** — `installer.step.duration` (histogram), `installer.job.success_rate` (counter), `agent.heartbeat.latency`, recorded via `Meter` API; (3) **Logs** — structured logs correlated to the active trace via `ILogger` + OTel log bridge; (4) **Duration hooks** — every `IInstallStep.Execute()` wraps its body in `ActivitySource.StartActivity(stepName)` ensuring no step can run without timing; define on-prem OTel Collector config (OTLP receiver → Prometheus exporter + Jaeger exporter + Seq log exporter); error taxonomy and diagnostic codes | Define OTel SDK setup code pattern, `ActivitySource` naming, metric instrument types, and Collector pipeline config |
| Afternoon (Part 1) | **C#-first automation tooling detailed design** — CLI command spec using `System.CommandLine` (strongly-typed commands, options, exit codes, JSON output mode for machine consumption); **no PowerShell modules** — the CLI is the scripting-free automation surface; REST webhook payload schemas; define the JSON install manifest schema (target nodes, package references, step overrides, parameter values) as the data-driven alternative to scripting; how large installer media (e.g., SQL Server) is referenced via UNC path in the manifest; clarify Azure DevOps boundary: pipelines call `dotnet publish` and copy outputs to the deployment server — from there the Orchestrator takes over workstation deployments entirely | CLI command spec + JSON manifest schema; note what Azure DevOps does vs. what the Orchestrator does |
| Afternoon (Part 2) | **Configuration persistence detailed design** — config backup schema (what is captured, where stored, format), config migration contract (version-to-version migration functions, rollback of config on install failure), how agents report config migration success/failure to the Orchestrator | Config schema versioning design + migration interface |
| Afternoon (Part 3) | **React Dashboard detailed design** — page/component breakdown: Node List (health, version, EOL status), Job Queue (active/scheduled installs), Install/Upgrade Trigger, Upgrade Status with step-level progress, Telemetry/Logs view with correlation ID drill-down, EOL Warnings panel; state management, real-time updates via SignalR/polling | Wireframe or component tree diagram |
| Afternoon (Part 4) | **Security detailed design** — detail the implementation of each security design decision from Day 6: package signing verification flow in C# (`AuthenticodeSignatureHelper`, hash verification before `IInstallStep` execution); Orchestrator–Agent mutual auth implementation (certificate thumbprint validation or HMAC API key middleware in ASP.NET Core); RBAC implementation (ASP.NET Core `IAuthorizationPolicy`); least privilege process execution in legacy adapter modules (`ProcessStartInfo` with explicit user context, sanitized argument building); audit log append-only design with hash chaining; DPAPI-based key storage helper class | Security implementation patterns for each decision; integrate security checks into the `IInstallStep` pipeline |
| EOD | **Architecture Decision Records (ADRs)** — document key decisions made across Days 7-9: choice of installer format (WiX vs. others), self-contained packaging strategy, modular pipeline design with `IInstallStep`, legacy interop adapter pattern, **OpenTelemetry as the telemetry standard** (vs. custom logging), **no-scripting constraint** (C# + data-driven manifests only, with rationale), config persistence pattern, automation surface (CLI + REST, Azure DevOps integration), modernization phasing strategy, **security ADRs** (auth mechanism, package signing approach, privilege execution model, audit log integrity strategy) | Each ADR: context → decision → consequences |

**Output**: Completed detailed design document. Combined with Day 7's architecture doc, this is the full working spec for implementation.

---

## Day 10 — DevOps Pipeline Design and Integration

**Goal**: Design and implement the full Azure DevOps CI/CD pipeline for the framework itself — from code commit to a running Orchestrator on the deployment server, with automated test gates at every stage

| Time | Topic | Resources/Tasks |
|---|---|---|
| Morning (Part 1) | **Pipeline mapping** — map out the full end-to-end pipeline before writing any YAML; define stages and gates: (1) CI — triggered on every PR/commit: restore → build → **unit tests** (must pass before proceeding); (2) Build artifacts — `dotnet publish --self-contained` for Orchestrator and Agent, `npm run build` for React frontend, package outputs as pipeline artifacts; (3) CD — triggered on merge to main: deploy Orchestrator binary to designated deployment server (copy artifact to server drop location or trigger a self-update); (4) **Integration tests** — run against the freshly deployed Orchestrator; (5) **E2E tests** — run Playwright tests against the live React UI on the deployment server | Produce a pipeline stage diagram before writing any YAML; define what a failing gate in each stage means and what happens next |
| Morning (Part 2) | **Azure DevOps CI pipeline — build and unit test stage** — write the Azure DevOps YAML pipeline for CI: `dotnet restore`, `dotnet build`, `dotnet test` (xUnit unit tests with test results published to Azure DevOps), `npm ci` + `npm run build` for the React frontend, `dotnet publish` producing self-contained Orchestrator and Agent binaries, publish pipeline artifacts; unit tests must run and pass before any artifact is produced | Working `azure-pipelines.yml` CI stage; confirm test results appear in Azure DevOps test tab |
| Afternoon (Part 1) | **Azure DevOps CD pipeline — deploy to deployment server** — write the CD stage: copy published Orchestrator artifacts to the deployment server (via file share, SSH, or a deployment agent on that server only — not to workstations); trigger Orchestrator self-update or service restart on the deployment server to pick up the new binary; define rollback strategy if the new binary fails to start (keep previous artifact, auto-revert) | Working CD stage that deploys the Orchestrator binary to the designated server after a successful CI build |
| Afternoon (Part 2) | **Integration test stage** — after the Orchestrator is deployed and healthy, run integration tests: C# xUnit/NUnit integration tests that exercise the Orchestrator REST API end-to-end (register a mock agent, submit an install job, verify status transitions, verify telemetry events emitted); tests run as a pipeline stage gating further promotion; define what counts as a passing integration test run | Integration test project targeting the live Orchestrator; results published to Azure DevOps; pipeline fails if any integration test fails |
| Afternoon (Part 3) | **E2E test stage** — run Playwright E2E tests against the React Dashboard served by the deployed Orchestrator; cover key user flows: view node list, trigger an install, monitor job progress, view telemetry log; tests run headlessly in the pipeline; results published as HTML report artifact | Working Playwright E2E stage in the pipeline; HTML report published as artifact |
| EOD | **Pipeline review and quality gates** — review the full pipeline end-to-end; define branch policies (unit tests must pass to merge PR, integration + E2E must pass before artifact is considered release-ready); define artifact versioning strategy (semantic versioning tied to pipeline run); document the pipeline as part of the project's DevOps runbook | Finalized `azure-pipelines.yml` with all stages; branch policy documented |

**Output**: A fully working Azure DevOps pipeline covering CI (unit tests + build), CD (deploy Orchestrator to deployment server), integration tests, and E2E tests — with a pipeline diagram documenting the stage flow and quality gates.

---

## After Day 10: Ready for Implementation

EJ should be able to:
- Write C# Web API endpoints with tests
- Build a React UI with component and UI tests
- Explain the full landscape of deployment and automation options
- Articulate the current DeltaV installer's structure, tech stack, and modernization path
- Present a complete requirements doc aligned to deterministic, testable, modular, and distributed principles
- Produce a threat model (DFD + STRIDE threat register) and translate it into concrete security design decisions
- Hand off or begin implementation from a detailed design covering: installer pipeline, legacy interop, upgrade + config persistence, observability/telemetry, automation/CLI, EOL awareness, and security
- Justify all key architectural decisions via ADRs, including security ADRs
- Operate and extend the Azure DevOps CI/CD pipeline for the framework (CI, CD to deployment server, integration tests, E2E tests)

