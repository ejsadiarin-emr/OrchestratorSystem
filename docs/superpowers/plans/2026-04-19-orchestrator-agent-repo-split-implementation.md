# Orchestrator + Agent Repo Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the repository into symmetric `apps/orchestrator` and `apps/agent` projects (each with .NET backend + embedded React web), move contracts/tests into canonical shared locations, and remove `/api/jobs` runtime surfaces.

**Architecture:** Execute a big-bang physical move while preserving behavior by rewiring solution/project references immediately after moves. Keep Orchestrator and Agent app layouts symmetric (`backend/` + `web/`) and use `shared/contracts` as the only cross-app contract authority. Remove legacy jobs endpoints and route surfaces in the same migration stream so runtime semantics are consistently workload-run based.

**Tech Stack:** .NET 10 ASP.NET Core, C# class libraries, React 19 + TypeScript + Vite + Vitest, root Visual Studio solution.

---

## File Structure

- Create: `apps/orchestrator/`
- Create: `apps/orchestrator/backend/`
- Create: `apps/orchestrator/web/`
- Create: `apps/agent/`
- Create: `apps/agent/backend/`
- Create: `apps/agent/web/`
- Create: `shared/contracts/`
- Create: `tests/orchestrator/unit/`
- Create: `tests/orchestrator/integration/`
- Create: `tests/agent/integration/`
- Create: `tests/contracts/`
- Modify: `DeploymentPoC.sln`
- Modify: `.gitignore`
- Modify: moved `.csproj` files for project references
- Modify: `apps/orchestrator/backend/Program.cs`
- Modify: `apps/orchestrator/backend/Controllers/*` (remove jobs surfaces)
- Modify: `apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj`
- Modify: `apps/agent/backend/DeploymentPoC.Agent.csproj`
- Create: `apps/agent/backend/Program.cs`
- Modify: `apps/orchestrator/web/*` configs and source
- Modify: `apps/agent/web/*` configs and source

---

### Task 1: Create target directories and move backend/contracts/tests projects

**Files:**
- Move: `src/DeploymentPoC.Orchestrator` -> `apps/orchestrator/backend`
- Move: `src/DeploymentPoC.Agent` -> `apps/agent/backend`
- Move: `src/DeploymentPoC.Contracts` -> `shared/contracts`
- Move: `tests/DeploymentPoC.Orchestrator.Tests` -> `tests/orchestrator/unit`
- Move: `tests/DeploymentPoC.Orchestrator.IntegrationTests` -> `tests/orchestrator/integration`
- Move: `tests/DeploymentPoC.Agent.IntegrationTests` -> `tests/agent/integration`
- Move: `tests/DeploymentPoC.Contracts.Tests` -> `tests/contracts`

- [ ] **Step 1: Create app/shared/test directory skeleton**

Run:

```bash
mkdir -p apps/orchestrator apps/agent shared tests/orchestrator tests/agent
```

Expected: directories exist without changing project file contents yet.

- [ ] **Step 2: Move source and test folders to target topology**

Run:

```bash
mv src/DeploymentPoC.Orchestrator apps/orchestrator/backend && mv src/DeploymentPoC.Agent apps/agent/backend && mv src/DeploymentPoC.Contracts shared/contracts && mv tests/DeploymentPoC.Orchestrator.Tests tests/orchestrator/unit && mv tests/DeploymentPoC.Orchestrator.IntegrationTests tests/orchestrator/integration && mv tests/DeploymentPoC.Agent.IntegrationTests tests/agent/integration && mv tests/DeploymentPoC.Contracts.Tests tests/contracts
```

Expected: old paths no longer exist; new paths exist with same contents.

- [ ] **Step 3: Run path sanity checks**

Run:

```bash
ls apps/orchestrator/backend apps/agent/backend shared/contracts tests/orchestrator/unit tests/orchestrator/integration tests/agent/integration tests/contracts
```

Expected: each path lists moved project files.

- [ ] **Step 4: Commit move-only baseline**

```bash
git add apps shared tests src DeploymentPoC.sln .gitignore
git commit -m "refactor(repo): move projects into apps and shared topology"
```

### Task 2: Rewire solution and project references after physical move

**Files:**
- Modify: `DeploymentPoC.sln`
- Modify: `tests/orchestrator/unit/DeploymentPoC.Orchestrator.Tests.csproj`
- Modify: `tests/orchestrator/integration/DeploymentPoC.Orchestrator.IntegrationTests.csproj`
- Modify: `tests/agent/integration/DeploymentPoC.Agent.IntegrationTests.csproj`
- Modify: `tests/contracts/DeploymentPoC.Contracts.Tests.csproj`
- Modify: `.gitignore`

- [ ] **Step 1: Update solution project path entries**

`DeploymentPoC.sln` path replacements:

```text
src\DeploymentPoC.Orchestrator\DeploymentPoC.Orchestrator.csproj -> apps\orchestrator\backend\DeploymentPoC.Orchestrator.csproj
src\DeploymentPoC.Agent\DeploymentPoC.Agent.csproj -> apps\agent\backend\DeploymentPoC.Agent.csproj
src\DeploymentPoC.Contracts\DeploymentPoC.Contracts.csproj -> shared\contracts\DeploymentPoC.Contracts.csproj
tests\DeploymentPoC.Orchestrator.Tests\DeploymentPoC.Orchestrator.Tests.csproj -> tests\orchestrator\unit\DeploymentPoC.Orchestrator.Tests.csproj
tests\DeploymentPoC.Orchestrator.IntegrationTests\DeploymentPoC.Orchestrator.IntegrationTests.csproj -> tests\orchestrator\integration\DeploymentPoC.Orchestrator.IntegrationTests.csproj
tests\DeploymentPoC.Agent.IntegrationTests\DeploymentPoC.Agent.IntegrationTests.csproj -> tests\agent\integration\DeploymentPoC.Agent.IntegrationTests.csproj
```

- [ ] **Step 2: Update test project references**

Use these exact `ProjectReference` targets:

```xml
<!-- tests/orchestrator/unit/DeploymentPoC.Orchestrator.Tests.csproj -->
<ProjectReference Include="..\..\..\apps\orchestrator\backend\DeploymentPoC.Orchestrator.csproj" />

<!-- tests/orchestrator/integration/DeploymentPoC.Orchestrator.IntegrationTests.csproj -->
<ProjectReference Include="..\..\..\apps\orchestrator\backend\DeploymentPoC.Orchestrator.csproj" />

<!-- tests/agent/integration/DeploymentPoC.Agent.IntegrationTests.csproj -->
<ProjectReference Include="..\..\..\apps\agent\backend\DeploymentPoC.Agent.csproj" />

<!-- tests/contracts/DeploymentPoC.Contracts.Tests.csproj -->
<ProjectReference Include="..\..\shared\contracts\DeploymentPoC.Contracts.csproj" />
```

- [ ] **Step 3: Update ignored runtime-db path for moved orchestrator backend**

In `.gitignore`:

```text
src/DeploymentPoC.Orchestrator/data/ -> apps/orchestrator/backend/data/
src/DeploymentPoC.Orchestrator/data/**/*.db* -> apps/orchestrator/backend/data/**/*.db*
```

- [ ] **Step 4: Verify solution restore/build now resolves moved paths**

Run:

```bash
dotnet build DeploymentPoC.sln
```

Expected: solution builds with new project locations.

- [ ] **Step 5: Commit reference rewiring**

```bash
git add DeploymentPoC.sln tests .gitignore
git commit -m "chore(repo): rewire solution and project references after move"
```

### Task 3: Split `web` into symmetric orchestrator/agent web apps

**Files:**
- Move/Copy: `web` -> `apps/orchestrator/web`
- Create: `apps/agent/web` (from shared baseline)
- Modify: `apps/orchestrator/web/package.json`
- Modify: `apps/agent/web/package.json`
- Modify: `apps/orchestrator/web/vite.config.ts`
- Modify: `apps/agent/web/vite.config.ts`
- Modify: app-specific `src/App.tsx`, `src/components/Layout.tsx`, `src/pages/*`

- [ ] **Step 1: Move current web app into orchestrator location**

Run:

```bash
mv web apps/orchestrator/web
```

Expected: `apps/orchestrator/web` contains current app state and tests.

- [ ] **Step 2: Create agent web app by copying orchestrator baseline**

Run:

```bash
cp -R apps/orchestrator/web apps/agent/web
```

Expected: both app folders exist with initial identical toolchain.

- [ ] **Step 3: Set unique package names and app titles**

```json
// apps/orchestrator/web/package.json
"name": "orchestrator-web"

// apps/agent/web/package.json
"name": "agent-web"
```

- [ ] **Step 4: Wire Vite output to each sibling backend wwwroot**

```ts
// apps/orchestrator/web/vite.config.ts
outDir: path.resolve(__dirname, '../backend/wwwroot')

// apps/agent/web/vite.config.ts
outDir: path.resolve(__dirname, '../backend/wwwroot')
```

- [ ] **Step 5: Prune UI surfaces per ownership boundary**

- Orchestrator web keeps fleet/control-plane pages.
- Agent web keeps local runtime pages (including `AgentLocal`).
- Remove cross-owned navigation routes in each app.

- [ ] **Step 6: Build and test both web apps**

Run:

```bash
npm ci --prefix apps/orchestrator/web && npm run test --prefix apps/orchestrator/web && npm run build --prefix apps/orchestrator/web && npm ci --prefix apps/agent/web && npm run test --prefix apps/agent/web && npm run build --prefix apps/agent/web
```

Expected: both test suites and builds pass; outputs land in sibling backend `wwwroot` folders.

- [ ] **Step 7: Commit web split**

```bash
git add apps/orchestrator/web apps/agent/web
git commit -m "feat(repo): split web into orchestrator and agent embedded apps"
```

### Task 4: Make agent backend self-contained host and align backend static hosting

**Files:**
- Modify: `apps/agent/backend/DeploymentPoC.Agent.csproj`
- Create: `apps/agent/backend/Program.cs`
- Modify: `apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj`
- Modify: `apps/orchestrator/backend/Program.cs`

- [ ] **Step 1: Promote agent backend to executable/self-contained publishable app**

Add to `apps/agent/backend/DeploymentPoC.Agent.csproj`:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net10.0</TargetFramework>
  <RuntimeIdentifier>linux-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>true</PublishSingleFile>
  <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
</PropertyGroup>

<ItemGroup>
  <FrameworkReference Include="Microsoft.AspNetCore.App" />
</ItemGroup>
```

- [ ] **Step 2: Add minimal agent backend host serving its embedded web output**

Create `apps/agent/backend/Program.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health", () => Results.Ok(new { service = "agent", status = "ok" }));
app.Run();
```

- [ ] **Step 3: Simplify orchestrator static hosting to single wwwroot source**

In `apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj`, remove `Static/**` embedded resource model so `wwwroot` is authoritative.

- [ ] **Step 4: Validate backend builds**

Run:

```bash
dotnet build apps/orchestrator/backend/DeploymentPoC.Orchestrator.csproj && dotnet build apps/agent/backend/DeploymentPoC.Agent.csproj
```

Expected: both backends compile from new app paths.

- [ ] **Step 5: Commit backend hosting alignment**

```bash
git add apps/orchestrator/backend apps/agent/backend
git commit -m "feat(repo): make agent backend hostable and align static hosting"
```

### Task 5: Remove `/api/jobs` runtime surfaces and migrate references

**Files:**
- Modify/Delete: `apps/orchestrator/backend/Controllers/JobsController.cs`
- Modify: `apps/orchestrator/backend/Program.cs` routing/registration as needed
- Modify: `apps/orchestrator/web/src/pages/Jobs.tsx` and any `/jobs` routes/nav links
- Modify: tests referencing `/api/jobs` runtime endpoints

- [ ] **Step 1: Write failing orchestrator integration test asserting `/api/jobs` endpoint absence**

Add a test that requests a representative `/api/jobs` runtime endpoint and expects non-success (404 or removed route behavior).

- [ ] **Step 2: Run targeted integration test to confirm current failure**

Run:

```bash
dotnet test tests/orchestrator/integration/DeploymentPoC.Orchestrator.IntegrationTests.csproj --filter Jobs
```

Expected: FAIL prior to controller/route removal.

- [ ] **Step 3: Remove jobs controller/route and migrate UI references to workload-runs surfaces**

Apply code changes:
- delete or retire `JobsController.cs` from runtime pipeline,
- remove `/jobs` nav/route in orchestrator web,
- use workload-run pages as canonical runtime lifecycle UI.

- [ ] **Step 4: Re-run targeted tests and full orchestrator web tests**

Run:

```bash
dotnet test tests/orchestrator/integration/DeploymentPoC.Orchestrator.IntegrationTests.csproj && npm run test --prefix apps/orchestrator/web
```

Expected: PASS with no `/api/jobs` runtime path dependencies.

- [ ] **Step 5: Commit jobs-removal migration**

```bash
git add apps/orchestrator/backend apps/orchestrator/web tests/orchestrator/integration
git commit -m "refactor(orchestrator): remove jobs runtime endpoints and ui references"
```

### Task 6: Final verification and docs alignment

**Files:**
- Modify: docs that reference old `src/*` and `web/*` paths where needed
- Modify: `docs/superpowers/specs/2026-04-19-orchestrator-agent-repo-split-design.md` (status updates if needed)

- [ ] **Step 1: Run full .NET test suite from root**

Run:

```bash
dotnet test DeploymentPoC.sln
```

Expected: all .NET tests pass with moved project topology.

- [ ] **Step 2: Run full web test/build suite for both apps**

Run:

```bash
npm run test --prefix apps/orchestrator/web && npm run build --prefix apps/orchestrator/web && npm run test --prefix apps/agent/web && npm run build --prefix apps/agent/web
```

Expected: both web apps pass tests and produce embedded build output.

- [ ] **Step 3: Run final git status validation**

Run:

```bash
git status
```

Expected: only intended migration changes remain staged/unstaged; no unexpected generated noise beyond accepted build outputs.

- [ ] **Step 4: Commit verification/docs alignment**

```bash
git add docs
git commit -m "docs(repo): align paths and migration notes with app split topology"
```

## Self-Review Checklist (completed)

- **Spec coverage:** all requirements in `docs/superpowers/specs/2026-04-19-orchestrator-agent-repo-split-design.md` map to Tasks 1-6, including symmetric app shape, contracts move, and `/api/jobs` removal.
- **Placeholder scan:** no TODO/TBD placeholders; each task has concrete files/commands.
- **Consistency check:** path scheme is consistently `apps/orchestrator/*`, `apps/agent/*`, `shared/contracts`, `tests/*` across tasks.
