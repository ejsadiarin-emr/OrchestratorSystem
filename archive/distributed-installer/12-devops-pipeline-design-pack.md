# DevOps Pipeline Design Pack

Date: 2026-04-11  
Status: Draft (prefilled from locked decisions)

## Purpose

Define minimal but complete CI/CD design artifacts for PoC closure.

Implementation phasing tags used in this document:

- `[PoC Phase 1]` required for implementation-plan baseline
- `[Hardening Phase 2]` explicitly deferred until post-PoC hardening

Policy lock:

- Azure DevOps builds/tests/signs/publishes framework artifacts and deploys orchestrator only.
- Workstation package deployments are triggered through Orchestrator API/CLI, not directly from pipeline.

---

## 1) Stage diagram (text form)

`CI (build + unit) -> publish artifacts -> deploy orchestrator -> integration tests -> E2E tests`

Diagram path (working): `docs/distributed-installer/diagrams/install-sequence.ascii.md`

---

## 2) Pipeline skeleton (`azure-pipelines.yml`)

```yaml
trigger:
  - main

stages:
  - stage: CI
    jobs:
      - job: build_and_unit
        steps:
          - script: dotnet restore
          - script: dotnet build --no-restore
          - script: dotnet test --no-build

  - stage: Publish
    dependsOn: CI
    jobs:
      - job: publish_artifacts
        steps:
          - script: dotnet publish src/Orchestrator/Orchestrator.csproj --self-contained --runtime win-x64 -p:PublishSingleFile=true
          - script: dotnet publish src/Agent/Agent.csproj --self-contained --runtime win-x64 -p:PublishSingleFile=true
          - script: npm ci && npm run build
          - script: ./scripts/sign-artifacts.ps1 -Path "$(Build.ArtifactStagingDirectory)"
            displayName: Sign orchestrator, agent, and manifest artifacts

  - stage: PackagingValidation
    dependsOn: Publish
    jobs:
      - job: clean_host_launch_test
        steps:
          - script: echo "Validate orchestrator launches on clean Windows host without preinstalled .NET runtime or IIS"

  - stage: DeployOrchestrator
    dependsOn: PackagingValidation
    jobs:
      - job: deploy
        steps:
          - script: echo "Deploy orchestrator artifact to deployment server"
          - script: echo "Rollback contract: if /health/live or /health/ready fails 3 consecutive checks in 90s, redeploy previous signed artifact"

  - stage: Integration
    dependsOn: DeployOrchestrator
    jobs:
      - job: integration_tests
        steps:
          - script: dotnet test tests/Integration

  - stage: E2E
    dependsOn: Integration
    jobs:
      - job: e2e_tests
        steps:
          - script: npm run test:e2e
```

---

## 2A) Self-contained packaging requirement

Required publish posture for orchestrator artifacts:

- `dotnet publish --self-contained --runtime win-x64 -p:PublishSingleFile=true`
- React UI build artifacts embedded into orchestrator executable static assets
- Output is runnable on clean target machine without pre-installed .NET runtime/IIS

Agent artifacts are separate self-contained binaries distributed through approved bootstrap channels; pipeline still builds and signs them as framework artifacts.

## 2B) On-prem / air-gap gate realism

Required constraints for Integration/E2E gates:

- No public internet dependency during required release gates
- Test fixtures and artifacts are sourced from internal mirrors or preloaded assets
- Signature/hash negative tests use local signed/unsigned fixtures
- Telemetry assertions target on-prem or local collector endpoints

---

## 3) Gate policy table

| Stage | Gate condition | On fail |
|---|---|---|
| CI | Build + unit tests pass | Stop pipeline |
| Publish | Artifact publish success | Stop pipeline |
| PackagingValidation | Clean-host launch test passes (no preinstalled .NET/IIS) | Stop promotion |
| DeployOrchestrator | Health check passes | Auto-rollback to previous orchestrator artifact |
| Integration | Integration tests pass | Stop promotion |
| E2E | E2E tests pass | Mark non-release-ready |

---

## 4) Artifact versioning strategy

- Version format: `MAJOR.MINOR.PATCH+build.<pipelineRunId>`
- Source of truth: Git tag for release versions; pipeline run metadata for pre-release builds
- Signed artifacts required: orchestrator and agent binaries, package manifests where applicable

---

## 5) Branch and release policy

| Branch | Required checks |
|---|---|
| PR to main | [PoC Phase 1] CI + PackagingValidation required |
| main release candidate | [PoC Phase 1] CI + Integration + E2E required |

---

## 6) Explicit non-goal statement

Direct workstation deployment from Azure DevOps pipeline is out of scope by policy.

All workstation installation actions must be triggered via Orchestrator API/CLI.

Runtime install/upgrade/rollback operations are never executed directly from pipeline jobs.

Pipeline responsibilities are restricted to build, test, sign, publish framework artifacts, and deploy the orchestrator artifact.

---

## 7) Open items

- Environment names, deployment transport, and concrete orchestrator rollback script are pending environment setup details.

## 8) PoC boundary note

- `[PoC Phase 1]` pipeline baseline: build, unit tests, self-contained publish (orchestrator + agent), signing, clean-host packaging validation, orchestrator deploy, integration/E2E gates, and explicit no-direct-workstation-deploy boundary.
- [Hardening Phase 2] Operational enhancements: environment-matrix expansion, advanced rollout rings, broader release governance automation, and extended evidence retention/reporting.
