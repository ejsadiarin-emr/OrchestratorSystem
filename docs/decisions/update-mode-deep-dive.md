# Update Mode — Deep Dive

> Cross-reference: Decision log at [workload-differential-update-rollback.md](workload-differential-update-rollback.md) (especially Decisions 6, 7, 10, 18)

This document explains the update pipeline in detail — how it works, why it works that way, and how to configure each package's upgrade behavior.

---

## The Problem

When a workload updates from one revision to another, some packages change version (e.g., Python 3.13.3 → 3.14.4). The naïve approach — "just run the new installer" — fails for packages whose installers don't remove the old version:

1. **Python** (WiX-based EXE): Installs 3.14.4 *alongside* 3.13.3. After "upgrade," both exist. `PostInstallVerify` detects 3.13.3 (wrong version → **failure**).
2. **.NET Runtime** (WiX Burn bundle): Same behavior as Python — installs the new version without removing the old one.
3. **Git** (Inno Setup): The 2.48.1 installer detects and replaces 2.47.1 in-place. No problem.
4. **DBeaver** (NSIS/Oomph): The 26.0.3 installer replaces 24.3.0. No problem.

The key insight: **whether an installer upgrades in-place or leaves the old version behind is an intrinsic property of the package, not the workload**. Python always leaves the old version. Git always upgrades in-place. This is about how the installer binary works, not about how the workload is composed.

---

## `UpgradeBehavior` — Per-Package Setting

Each package declares how its installer behaves on upgrade via `UpgradeBehavior` on `InstallAdapterConfig`:

| Value | What happens | When to use |
|---|---|---|
| `InPlace` | Run new installer only. The installer handles replacing the old version. | **Default.** Use for self-upgrading installers that safely remove the old version while preserving data (DBeaver, SQL Server, Visual Studio, Git, 7-Zip, Notepad++). |
| `UninstallFirst` | Uninstall old version → install new version. Old version's `UninstallArgs` are used. | Installers that don't remove the old version (Python, .NET Runtime, Node.js LTS → new major). Running the new installer alone leaves the old version on disk. |

### Why package-level, not workload-level?

`UpgradeBehavior` is intrinsic to the installer binary, not to the workload definition:

- Python 3.13→3.14 always needs `UninstallFirst` regardless of which workload references it
- SQL Server always needs `InPlace` because uninstalling first would destroy databases — its installer handles in-place upgrades safely
- DBeaver always upgrades in-place regardless of context
- Setting this per-workload would mean repeating the same value everywhere a package appears, and risk inconsistency

**Critical point about `InPlace` for stateful apps:** Setting `UninstallFirst` on a package like SQL Server would be destructive. SQL Server's uninstaller removes databases, logins, and configuration. The SQL Server setup.exe is designed to upgrade in-place — it detaches databases, replaces binaries, and reattaches. Choosing the wrong `UpgradeBehavior` for stateful apps causes data loss.

### Default is `InPlace`

Most well-behaved Windows installers (Inno Setup, NSIS, WiX with UpgradeTable, InstallShield) handle in-place upgrades. The default preserves backward compatibility: existing packages work without any configuration change.

Only packages that leave the old version behind need explicit `UpgradeBehavior = "UninstallFirst"`.

---

## Pipeline Execution — Phase by Phase

The update pipeline has five phases. Here's what happens for each diff category:

```
┌─────────────────────────────────────────────────────────────────────────┐
│ Phase 0: PreCheck — probe each package's Detection config              │
│   → Override diff categories based on actual state                     │
│   → "added" package already installed? → "unchanged"                  │
│   → "unchanged" package wrong version? → "changed"                   │
│   → "changed" package already installed? → "unchanged"                │
│   → "changed" package not present? → "added"                          │
├─────────────────────────────────────────────────────────────────────────┤
│ Phase 0.5: PreWorkload — run workload-level preInitSteps              │
├─────────────────────────────────────────────────────────────────────────┤
│ Phase 1: Uninstall — removed packages + UninstallFirst changed pkgs   │
│   Removed (all):            UninstallPackage (old config + UninstallArgs)│
│   Changed (UninstallFirst): UninstallPackage (old config + UninstallArgs)│
│   Changed (InPlace):        — nothing —                                │
│   Order: descending PackageIndex                                      │
├─────────────────────────────────────────────────────────────────────────┤
│ Phase 2: Install — added + changed packages, ascending PackageIndex   │
│   Added:   AcquireArtifact → PreInitSteps → InstallOrUpgrade →        │
│            PostInstallVerify → PostInitSteps                           │
│   Changed (InPlace):        same as Added                              │
│   Changed (UninstallFirst): same as Added (old already uninstalled)    │
├─────────────────────────────────────────────────────────────────────────┤
│ Phase 3: PostWorkload — run workload-level postInitSteps              │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## Walkthrough Examples

### Example 1: Python 3.13.3 → 3.14.4 (`UninstallFirst`)

**Scenario**: "Amazing Workload" v1 (Python 3.13.3 + DBeaver 24.3.0) → v2 (Python 3.14.4 + DBeaver 26.0.3)

**Diff computed:**
- `dbeaver-24.3.0 → dbeaver-26.0.3` — changed (`UpgradeBehavior = "InPlace"`)
- `python-3.13.3 → python-3.14.4` — changed (`UpgradeBehavior = "UninstallFirst"`)

**Pipeline execution:**

```
Phase 0   PreCheck probes → both packages detected with wrong version → confirmed "changed"
Phase 0.5 PreWorkload steps run
Phase 1   UninstallPackage for python-3.13.3 (uses OLD config: python-3.13.3-amd64.exe /quiet /uninstall)
          → exit code 0 (success)
Phase 2   dbeaver-26.0.3: AcquireArtifact → PreInitSteps → InstallOrUpgrade → PostInstallVerify → PostInitSteps
          python-3.14.4:  AcquireArtifact → PreInitSteps → InstallOrUpgrade → PostInstallVerify → PostInitSteps
Phase 3   PostWorkload steps run
```

**What goes wrong without `UninstallFirst`:** The Python 3.14.4 installer runs while 3.13.3 is still on disk. It installs alongside it. `PostInstallVerify` checks `python --version` and gets `3.13.3` because 3.13.3 appears first in PATH. Result: **pipeline failure** with `version_mismatch: expected 3.14.4, got 3.13.3`.

**What `UninstallFirst` fixes:** Phase 1 removes Python 3.13.3 (WiX uninstall cleans registry + PATH). Phase 2 installs 3.14.4 to a clean slate. `PostInstallVerify` finds `3.14.4` → **success**.

### Example 2: Git 2.47.1 → 2.48.1 (`InPlace` — default)

**Scenario**: "Utility Pack" v1 → v2

**Diff computed:**
- `7zip-24.09 → 7zip-26.00` — changed (`UpgradeBehavior = "InPlace"`)
- `git-2.47.1 → git-2.48.1` — changed (`UpgradeBehavior = "InPlace"`)

**Pipeline execution:**

```
Phase 0   PreCheck probes
Phase 0.5 PreWorkload steps run
Phase 1   Nothing to uninstall (no removed packages, no UninstallFirst changed packages)
Phase 2   7zip-26.00: AcquireArtifact → InstallOrUpgrade → PostInstallVerify
          git-2.48.1:  AcquireArtifact → InstallOrUpgrade → PostInstallVerify
Phase 3   PostWorkload steps run
```

No `UninstallPackage` step needed. The Inno Setup (git) and NSIS (7-Zip) installers detect and replace the old version.

### Example 3: Package removal — test-agent dropped from v2

**Scenario**: "Runtime Environment" v1 (dotnet-runtime-9.0.5 + test-agent-1.0.1) → v2 (dotnet-runtime-10.0.7 only)

**Diff computed:**
- `dotnet-runtime-9.0.5 → dotnet-runtime-10.0.7` — changed (`UpgradeBehavior = "UninstallFirst"`)
- `test-agent-1.0.1` — removed

**Pipeline execution:**

```
Phase 0   PreCheck probes
Phase 0.5 PreWorkload steps run
Phase 1   UninstallPackage for test-agent-1.0.1 (uses test-agent's UninstallArgs)
          UninstallPackage for dotnet-runtime-9.0.5 (uses OLD config: UninstallArgs)
          → [descending PackageIndex order]
Phase 2   dotnet-runtime-10.0.7: AcquireArtifact → InstallOrUpgrade → PostInstallVerify
Phase 3   PostWorkload steps run
```

---

## How `UninstallFirst` Gets the Old Package's Config

When a changed package has `UpgradeBehavior = "UninstallFirst"`, the `PipelineExecutor` needs the **old** package's `InstallAdapterConfig` (specifically `Command` + `UninstallArgs`) to perform the uninstall.

This is why Decision 15 specifies full `PackageAssignment` for `CurrentPackages`:

```
Agent receives:
  CurrentPackages:  [ python-3.13.3 with its InstallAdapter config ]
  Packages:         [ python-3.14.4 with its InstallAdapter config ]

Diff result:
  Changed:  [ python-3.14.4 (target) ]

PipelineExecutor resolves old config:
  var oldPackage = CurrentPackages.First(p => p.Name == changedPackage.Name);
  // oldPackage.InstallAdapter.UninstallArgs → "/quiet /uninstall"
  // oldPackage.InstallAdapter.Command → "python-3.13.3-amd64.exe"
```

The uninstall step uses the **old** config because:
1. The old version's uninstaller is the one that knows how to remove the old installation
2. The new version's `UninstallArgs` describe how to uninstall the new version, not the old one
3. This matches Chocolatey's model: the old package's uninstall script removes the old version

---

## Configuration Reference

### `InstallAdapterConfig` (shared contract)

```csharp
public sealed class InstallAdapterConfig
{
    public string Type { get; set; } = string.Empty;           // "msi" | "exe" | "zip"
    public string Command { get; set; } = string.Empty;        // executable or "msiexec.exe"
    public string Arguments { get; set; } = string.Empty;       // install arguments
    public string UninstallArgs { get; set; } = string.Empty;   // uninstall arguments
    public string UpgradeBehavior { get; set; } = "InPlace";    // "InPlace" | "UninstallFirst"
    public List<int> ExpectedExitCodes { get; set; } = [0];
    public int TimeoutSeconds { get; set; } = 300;
}
```

### `PackageEntity` (orchestrator database — new column)

```csharp
public string UpgradeBehavior { get; set; } = "InPlace";
```

This maps directly to `PackageAssignment.InstallAdapter.UpgradeBehavior` when the orchestrator constructs the payload.

### Manifest-driven configuration (no pattern registry defaults)

**Decision [ADR-0007](../adr/0007-manifest-driven-upgrade-configuration.md)**: There is no pattern registry or automatic default inference. The orchestrator rejects packages with missing `upgradeBehavior` at creation/import time. Admins must explicitly declare each package's behavior in the manifest.

**Rationale**: Installer behavior is not reliably inferable from filename or version number. A WiX Burn bundle might be `InPlace` (if the author configured `UpgradeCode`) or `UninstallFirst` (if not). Guessing risks data loss for stateful packages like SQL Server. Explicit configuration is the only safe approach.

---

## UninstallArgs Patterns by Installer Family

There is no universal uninstall command. Each installer family has its own conventions:

### MSI (Microsoft Installer)

MSI packages use `msiexec.exe` for both install and uninstall. The product code or MSI file identifies what to remove.

```json
{
  "type": "msi",
  "command": "msiexec.exe",
  "arguments": "/i \"{artifactPath}\" /qn /norestart",
  "uninstallArgs": "/x \"{artifactPath}\" /qn /norestart",
  "upgradeBehavior": "InPlace"
}
```

**Why `InPlace`**: MSI has built-in upgrade logic via the `UpgradeTable`. If the new MSI's `UpgradeCode` matches the old version, the installer handles removing the old version automatically during major upgrades.

**When `UninstallFirst`**: Only if the MSI lacks an `UpgradeTable` (rare for well-authored MSIs) or installs to a parallel location (e.g., per-user vs per-machine conflicts).

### WiX Burn Bundle (EXE)

WiX Burn bundles are EXE wrappers that embed MSI packages. They look like EXE installers but have MSI internals.

```json
{
  "type": "exe",
  "command": "artifact.bin",
  "arguments": "/quiet InstallAllUsers=1 PrependPath=1",
  "uninstallArgs": "/quiet /uninstall",
  "upgradeBehavior": "UninstallFirst"
}
```

**Why `UninstallFirst`**: Python and .NET use WiX Burn bundles. The bundle installs the new version alongside the old one — it does NOT remove the old version unless the `UpgradeCode` is explicitly configured (Python's installer doesn't). The WiX uninstall flag (`/uninstall`) removes the specific version it was built for.

### Inno Setup (EXE)

Inno Setup installers support silent install/uninstall via flags.

```json
{
  "type": "exe",
  "command": "artifact.bin",
  "arguments": "/VERYSILENT /NORESTART /NOCANCEL /SP-",
  "uninstallArgs": "/VERYSILENT /UNINSTALL",
  "upgradeBehavior": "InPlace"
}
```

**Why `InPlace`**: Inno Setup detects existing installations by `AppId` and replaces them. The `/VERYSILENT` flag handles both install and uninstall. Running the new installer automatically removes the old version (same `AppId`).

**Special case**: If uninstall is needed separately (e.g., for `UninstallFirst`), the Inno uninstaller is typically at `C:\Program Files\{AppName}\unins000.exe`. The `/VERYSILENT /UNINSTALL` flag triggers it from the original installer EXE.

### NSIS (EXE)

NSIS installers are similar to Inno but use `/S` for silent mode and `/D` for install directory.

```json
{
  "type": "exe",
  "command": "artifact.bin",
  "arguments": "/S /D=\"C:\\Program Files\\7-Zip\"",
  "uninstallArgs": "/S /D=\"C:\\Program Files\\7-Zip\"",
  "upgradeBehavior": "InPlace"
}
```

**Why `InPlace`**: NSIS installers typically overwrite existing installations when run with `/S`. The `/D` path must match for uninstall to work correctly.

### Custom EXE Installers

For custom or unknown installers, there's no universal pattern. The admin must provide:

- `UninstallArgs`: the flags that trigger silent uninstall
- `UpgradeBehavior`: determined by testing whether the installer replaces the old version or leaves it behind

**How to test**: Install version 1, then install version 2 without uninstalling 1. Check if version 1 is still present (filesystem, registry, PATH). If yes → `UninstallFirst`. If no → `InPlace`.

---

## Failure Modes and Recovery

### `UninstallFirst` — Uninstall Succeeds, Install Fails

**State**: Old version removed, new version not yet installed.

**Recovery**: Re-run the update. Since the old version is gone, the diff will classify the package as `added` instead of `changed`, and a fresh install will be attempted.

### `UninstallFirst` — Uninstall Fails

**State**: Old version still present. Pipeline halts (Decision 11: fail-fast).

**Recovery**: Operator investigates. The uninstall command may need adjustment (wrong `UninstallArgs`, process locks, permission elevation). Once resolved, retry the update.

### `InPlace` — PostInstallVerify Detects Old Version

**State**: New installer ran, but `DetectionConfig` still finds the old version number. This is the exact bug that triggered Decision 18.

**Root cause**: The installer left the old version behind (the package should have been `UninstallFirst`).

**Fix**: Change the package's `UpgradeBehavior` from `InPlace` to `UninstallFirst` and retry.

---

## Industry Analogies

### Microsoft Intune — Supersedence Model

Intune has a formal Supersedence feature with two modes:

| Intune Mode | Uninstall Previous? | DeploymentPoC Equivalent |
|---|---|---|
| App Update | No | `InPlace` |
| App Replacement | Yes — Intune uninstalls old, then installs new | `UninstallFirst` |

Intune gives the admin an **explicit choice** per supersedence relationship. DeploymentPoC makes this choice per-package via `UpgradeBehavior` since it's an intrinsic property of the installer.

### Chocolatey — Package-Authored Uninstall Scripts

Chocolatey runs `chocolateyUninstall.ps1` from the **old** package before `chocolateyInstall.ps1` from the **new** package. This matches DeploymentPoC's `UninstallFirst` behavior: the old package's `InstallAdapter` config (including `Command` + `UninstallArgs`) is used for uninstall.

Difference: Chocolatey always uninstalls-then-installs. DeploymentPoC only does this when `UpgradeBehavior = "UninstallFirst"`, preserving efficiency for in-place upgrade packages.

### MSI — UpgradeTable

MSI packages declare upgrade relationships in the `UpgradeTable`. When a major upgrade is detected (same `UpgradeCode`, different `ProductCode`), the new MSI runs `RemoveExistingProducts` which uninstalls the old version.

This is effectively MSI's built-in `UninstallFirst` — but only for packages that declare the relationship. DeploymentPoC makes this declaration explicit at the package configuration level rather than relying on the installer to self-describe.

---

## Summary Decision Tree

```
Package changes version in an update.
│
├─ Does the installer replace the old version when run?
│  │
│  ├─ YES → UpgradeBehavior = "InPlace" (default)
│  │         → Phase 2 only: run new installer
│  │         → Examples: Git, 7-Zip, DBeaver, Notepad++
│  │
│  └─ NO (old version persists after running new installer)
│     │
│     └─ UpgradeBehavior = "UninstallFirst"
│        → Phase 1: uninstall old → Phase 2: install new
│        → Examples: Python, .NET Runtime
│
│     How to determine: Install v1, then install v2 without removing v1.
│     Check if v1 is still present (filesystem, registry, PATH).
│     If yes → UninstallFirst.
```