# Gap Analysis: Meeting Notes vs Current Docs

Date: 2026-04-07
Session: Meeting notes review against existing research and design docs

## Purpose

Review meeting notes questions against current `docs/distributed-installer/` content to identify what is answered, partially answered, and missing.

---

## ✅ Answered in Docs

| Question | Where Answered |
|----------|---------------|
| **Users are system admins (master node)** | `00-meeting-summary-notes.md:9` — "System admins do not have a modern, unified, reliable remote install workflow" |
| **Self-contained (one .exe)** | `01-research-report.md:46-47` — "Self-contained binaries simplify deployment" |
| **Pull-based agent model** | `01-research-report.md:52-69` — pull-first by default, push optional later |
| **Ansible SSH push problems** | `02-market-and-ansible-comparison.md:36-38` — Ansible Windows remote execution hits delegation/logon/interactivity edge cases |
| **Rollback/compensation** | `01-research-report.md:113-116`, `03-architecture-and-design.md:157-162` — MSI native rollback, EXE requires compensating actions |
| **Job orchestrator / task queue** | `03-architecture-and-design.md:31-33` — "Job queue, assignment, execution, and status tracking" |
| **Job state machine** | `01-research-report.md:131-144`, `03-architecture-and-design.md:117-135` — full state definitions |
| **Happy path flow** | `03-architecture-and-design.md:228-233` — Scenario A covers it |
| **Different machine configs** | Implicit in `03-architecture-and-design.md:182-188` manifest/targeting contract — per-job targeting with detection rules |

---

## ⚠️ Partially Answered

| Question | Coverage | Gap |
|----------|----------|-----|
| **How to install agent on remote machines (bootstrap)** | Docs assume agent exists as a Windows service, but **no bootstrap/onboarding mechanism** is specified. No mention of how the agent gets onto a fresh machine in the first place. | Need: initial provisioning story — manual install? push script? self-registration? |
| **Ephemeral agent lifecycle** | Not addressed. Docs assume persistent Windows service agents. | Need: design for spin-up → execute → spin-down agents |
| **Agent communication protocol** | Docs mention "heartbeat" and "pull" but **no specific protocol** (HTTP/gRPC/WebSocket/SignalR). | Need: protocol decision |
| **Dry-run / pre-validation reflecting reality** | `03-architecture-and-design.md:149` has `PreConditionCheck` step, but no discussion of how dry-run outputs are validated against actual state. | Need: dry-run semantics and confidence scoring |
| **How ephemeral agents discover/read jobs** | Not addressed — assumes persistent polling agents. | Need: job discovery mechanism for ephemeral agents |
| **How Airflow/Dagster-style orchestration works** | Not researched. Docs cover job state machines but not DAG-style dependency orchestration. | Could be useful reference |
| **Self-hosted CI/CD runner patterns** (GitHub Actions, etc.) | Not researched. | Relevant for ephemeral agent + job pull patterns |
| **C# in-memory task queue equivalent to Celery** | Not specified. Docs mention a job queue but no library recommendation (e.g., Hangfire, Coravel, MassTransit in-memory). | Need: library decision |

---

## ❌ Not Answered

| Question | Notes |
|----------|-------|
| **Q#1: Do all remote machines need same config/installation?** | Docs imply heterogeneity (detection rules, per-node targeting) but never explicitly answer this. Should be stated clearly. |
| **Agent protocol specification** | No wire-level protocol defined (REST? gRPC? SignalR? message format?) |
| **Auto-rollback on agent install failure** | Rollback is defined for *install jobs*, not for the *agent installation itself*. If agent bootstrap fails midway, there's no recovery story. |
| **How agents know they're ephemeral** | Not addressed at all |

---

## Priority Gaps to Address

1. **Agent bootstrap/onboarding** — how does the agent get onto a machine initially?
2. **Communication protocol** — HTTP, gRPC, SignalR, or other?
3. **Ephemeral agent lifecycle** — design for temporary agents that spin up, execute, and tear down
4. **C# task queue library** — in-memory queue recommendation (Hangfire, Coravel, etc.)
5. **Dry-run validation confidence** — how to ensure pre-check results match reality
6. **Self-hosted CI/CD runner patterns** — research GitHub Actions runner, GitLab runner models for ephemeral agent inspiration
7. **Explicit answer on machine config heterogeneity** — should be stated clearly in design docs
