# Security Pack

Date: 2026-04-11  
Status: Draft (prefilled from locked decisions)

## Purpose

Implementation-ready security artifact pack for PoC closure.

Implementation phasing tags used in this document:

- `[PoC Phase 1]` required for implementation-plan baseline
- `[Hardening Phase 2]` explicitly deferred until post-PoC hardening

This pack must include:

1. Security DFD with trust boundaries
2. STRIDE register with likelihood/impact scoring
3. Mitigation-to-component mapping
4. Secure coding checklist mapped to implementation touchpoints

---

## 1) Security DFD and trust boundaries

### Components

- Orchestrator API
- SignalR hub
- Agent service
- Package/artifact source
- Audit/event store

### Trust boundaries

| ID | Boundary | Data crossing boundary | Threat concern |
|---|---|---|---|
| TB-01 | Admin UI/API caller -> Orchestrator API | Job commands, status queries, auth context | Spoofing, privilege abuse, repudiation |
| TB-02 | Agent -> SignalR hub | Assignment, claim, lease heartbeats, step status, logs | Agent spoofing, replay/out-of-order updates, DoS |
| TB-03 | Orchestrator/Agent -> Package source | Artifact retrieval and metadata | Tampering, substitution, stale content |
| TB-04 | Orchestrator -> Audit/event store | Security and operation events | Repudiation, audit tampering |

### DFD reference

- Diagram path: `docs/distributed-installer/diagrams/architecture.ascii.md`

Trust boundary to architecture mapping:

- `TB-01` maps to System Admin UI/API caller -> Orchestrator API edge.
- `TB-02` maps to Agent service -> SignalR hub runtime channel.
- `TB-03` maps to Orchestrator/Agent -> Artifact source retrieval channel.
- `TB-04` maps to Orchestrator API -> Audit/event store write path.

---

## 2) STRIDE register

Scoring scale (example): Likelihood 1-5, Impact 1-5, Risk = L x I.

Residual scoring uses the same 1-5 x 1-5 method after mitigation. PoC signoff requires explicit risk acceptance if any residual risk score is greater than 9.

| Threat ID | Component | STRIDE category | Description | L | I | Risk | Mitigation | Residual risk |
|---|---|---|---|---:|---:|---:|---|---|
| TH-001 | SignalR hub | Spoofing | [PoC Phase 1] Rogue node attempts to impersonate valid agent | 3 | 5 | 15 | Enrollment token + per-agent mTLS identity + cert validation | Medium |
| TH-002 | Package source | Tampering | [PoC Phase 1] Artifact replaced or modified post-publish | 3 | 5 | 15 | Signature verification + checksum verification before execution | Low-Medium |
| TH-003 | Orchestrator API | Repudiation | [PoC Phase 1] Operator denies having triggered deployment | 2 | 4 | 8 | Append-oriented audit with actor ID/role/correlation IDs and tamper-evident hash-chain verification | Low |
| TH-004 | Agent logs/telemetry | Information Disclosure | [PoC Phase 1] Secrets leak via logs/status payloads | 3 | 4 | 12 | Redaction policy + no plaintext secret storage + allowlisted log fields | Medium |
| TH-005 | SignalR/queue path | Denial of Service | [PoC Phase 1] Heartbeat flood or retry storm degrades orchestrator | 3 | 4 | 12 | Rate limits, bounded retries, lease timeouts, queue depth monitoring, bounded queue capacity, and connection limits | Medium |
| TH-006 | Legacy adapter execution | Elevation of Privilege | [PoC Phase 1] Unsafe shell invocation runs arbitrary code | 2 | 5 | 10 | Argument sanitization, adapter allowlists, isolated child process execution | Medium |
| TH-007A | Orchestrator/Agent executables | Tampering | [PoC Phase 1] Self-contained executable substitution attack | 3 | 5 | 15 | Signature/publisher verification checks at startup and update | Medium |
| TH-007B | Orchestrator/Agent executables | Elevation of Privilege | [PoC Phase 1] Downgrade to vulnerable but trusted executable version | 3 | 5 | 15 | Version floor (anti-downgrade) checks at startup and update | Medium |

---

## 3) Mitigation-to-component mapping

| Mitigation ID | Decision ref | Mitigation | Component(s) | Enforcement point | Control owner | Verification cadence | Evidence/verification |
|---|---|---|---|---|---|---|---|
| M-001 | D7 | [PoC Phase 1] mTLS per-agent identity after short-lived enrollment token bootstrap | SignalR hub, agent service | Agent register/auth pipeline and hub auth middleware | Platform security + backend engineer | Per release + cert rotation event | Failed cert auth tests + revoked-cert reconnect test |
| M-002 | D13 | [PoC Phase 1] Artifact signature and checksum verification (fail closed) | Agent executor, package source | `ValidateSignatureAndHash` step before install execution | Release engineering + agent engineer | Every build artifact + per release integration run | Unsigned/tampered artifact negative tests |
| M-003 | D13 | [PoC Phase 1] Role-based API authorization and tamper-evident audit linkage | Orchestrator API, audit store | API auth middleware + audit emission + hash-chain verifier | Backend engineer + security reviewer | Per PR security review + per release verification | Unauthorized role denied + audit event validation + tamper-evidence check |
| M-004 | D11, D17 | [PoC Phase 1] Sequence/idempotency enforcement for status updates | SignalR hub, job state store | StepStatus ingest/upsert logic | Backend engineer (runtime protocol) | Per release + reconnect/stale-lease regression suite | Replay/out-of-order test suite |
| M-005 | D13 | [PoC Phase 1] On-prem-first secret handling | Orchestrator, agent | DPAPI/cert store/credential manager integration points | Security engineer + DevOps | Per CI run (secret scan) + quarterly manual audit | Secret scanning + no-plaintext config validation |
| M-006 | D9 | [PoC Phase 1] Executable trust and anti-downgrade enforcement for self-contained binaries | Orchestrator startup/update path, agent startup/update path | Signature and publisher validation + version floor checks | Release engineering + platform security | Per build + per release deploy validation | Unsigned/publisher-mismatch/downgrade negative tests |

---

## 4) Secure coding checklist by touchpoint

### 4.1 Orchestrator API

- Input allowlists and schema validation are enforced.
- AuthN/AuthZ checks are enforced per endpoint.
- Rate limit and request size controls are configured.

### 4.2 SignalR hub

- mTLS identity is bound to node identity.
- Message sequence and idempotency checks are enforced.
- Reconnect/resume contract validation is enforced.

### 4.3 Agent executor

- Least privilege execution is used where possible.
- Process invocation is argument-sanitized and constrained.
- Artifact integrity checks run before execution.

### 4.4 Legacy adapters

- Executables and arguments are allowlisted.
- Exit codes are mapped and failure states normalized.
- Telemetry/audit emission avoids secret leakage.

---

## 5) Open items

- Final numeric risk acceptance threshold (e.g., max accepted risk score for PoC signoff).
- [Hardening Phase 2] Detailed key/certificate rotation cadence and operational runbook steps.
- [Hardening Phase 2] Extended incident response and forensics workflow depth.
- [Hardening Phase 2] Expanded audit retention operations and governance cadence.

## 6) PoC boundary note

- Security controls tagged `[PoC Phase 1]` are required baseline controls for this implementation cycle.
- Additional operational depth (expanded key lifecycle governance, extended incident runbooks, broader audit retention policy operations) is intentionally deferred to `[Hardening Phase 2]`.
