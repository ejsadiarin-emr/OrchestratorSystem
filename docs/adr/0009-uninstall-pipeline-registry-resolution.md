# Uninstall Pipeline: Registry-Based Command Resolution

The uninstall pipeline originally required an explicit `UninstallCommand` per package. If missing, it downloaded the entire installer artifact (potentially 100+ MB) and executed it as the uninstaller — which is incorrect for most packages (the installer is not the uninstaller).

## Problem

Three interconnected bugs:

1. **Artifact download regression**: When `UninstallCommand` was empty, the pipeline downloaded the full installer artifact and executed it, causing `exit_code_1` because the installer runs in GUI/install mode rather than uninstall mode.
2. **Registry detection was a stub**: `PackageDetector.DetectRegistryAsync` always returned `NotPresent`, making pre-checks unreliable for registry-installed packages. Packages that were actually installed would be skipped.
3. **Dangerous fallback chain**: `UninstallPackage.cs` fell back from `UninstallCommand` → `Command` (the install command) → `artifactPath` (the downloaded installer binary). Neither fallback is appropriate for uninstall.

## Decision

Implement a three-tier uninstall command resolution strategy:

```
Priority chain:
  1. Explicit UninstallCommand (from package manifest/admin)
  2. Windows registry lookup (QuietUninstallString / UninstallString)
  3. Skip-with-warning (no download, no execution)
```

### Registry Resolution

Query all 4 Windows uninstall registry paths:

| Scope | Architecture | Path |
|-------|--------------|------|
| Machine-wide | 64-bit | `HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` |
| Machine-wide | 32-bit (WOW64) | `HKLM\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall` |
| Per-user | 64-bit | `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall` |
| Per-user | 32-bit (WOW64) | `HKCU\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall` |

Match by exact `DisplayName`, prefer `QuietUninstallString` (silent) over `UninstallString` (may show UI). Coverage: ~95% of MSI/NSIS/Inno/WiX/InstallShield installers. Notable gaps: ClickOnce, MSIX/AppX, portable apps.

### Artifact Download

Artifact download still occurs but only when the uninstall command contains `{artifactPath}` placeholder or the package type is `msi` (for `msiexec /x` execution). In all other cases, no download happens during uninstall.

### Registry Detection (Pre-Check)

`PackageDetector.DetectRegistryAsync` now queries all 4 registry paths for `DisplayName` match and `DisplayVersion` comparison, replacing the previous stub that always returned `NotPresent`.

## Consequences

- **Positive**: No more 119MB downloads followed by `exit_code_1`. Uninstall works for most real-world installers without explicit configuration. Pre-checks are accurate for registry-installed packages.
- **Trade-off**: Registry lookup adds a small latency cost per package (acceptable — registry reads are fast). Packages without registry entries (portable apps, MSIX) will be skipped with a warning.
- **Risk**: `UninstallString` can be attacker-controlled. Mitigation: the agent runs the resolved command with a timeout; no further validation is performed in this PoC phase.

## Related Changes

- `PipelineExecutor.cs`: Both uninstall code paths replaced with registry resolution logic
- `UninstallPackage.cs`: Removed `Command` and `artifactPath` fallbacks; added `ResolveRegistryUninstaller`
- `PackageDetector.cs`: `DetectRegistryAsync` implemented with full registry enumeration
- `WorkloadPreCheck.cs`: Added admin privilege, package count, and process lock warnings
- `Dry-run preview`: `GET /api/workload-runs/preview` for read-only package diff per node
