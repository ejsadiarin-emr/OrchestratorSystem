# Decision: Init Steps Schema Placement

**Date:** 2026-04-30
**Status:** Resolved

## Decision

Init steps live **exclusively at the workload definition level**, NOT on the package manifest.

### Schema structure:

```jsonc
{
  "name": "CustomApp1 Workload",
  "slug": "customapp1-workload",
  "description": "...",
  "version": "2.0.0",
  "defaultShell": "powershell",
  "packages": [
    {
      "name": "custom-app-1-v2",
      "preInitSteps": ["psql --version"],        // []string, per-package
      "postInitSteps": ["echo done"]              // []string, per-package
    },
    {
      "name": "postgresql-v7",
      "preInitSteps": [],
      "postInitSteps": []
    }
  ],
  "postWorkloadSteps": [                           // []string, parent-level
    "COMMAND: Final verification",
    "COMMAND: curl orchestrator endpoint"
  ]
}
```

### Key points

- `preInitSteps` / `postInitSteps` are per-package entries inside the workload's `packages` array
- `postWorkloadSteps` is a parent-level field (runs after all packages complete)
- No init steps on the package manifest (PackageEntity / ingest JSON) — manifests stay general-purpose
- Packages run **sequentially** by PackageIndex, with their pre/post steps bookending each package
- Cross-package sequencing concerns (e.g., "connect app to db after both are installed") are the **workload creator's responsibility** — the system does not handle this automatically
- `defaultShell` is workload-level, defaults to `powershell`

### What this means for PackageAssignment

The `PackageAssignment` contract (sent from orchestrator → agent) will need new fields:

```csharp
public sealed class PackageAssignment
{
    // ... existing fields ...
    public List<string> PreInitSteps { get; set; } = new();
    public List<string> PostInitSteps { get; set; } = new();
}
```

And `AssignRunPayload` gains:

```csharp
public sealed class AssignRunPayload
{
    // ... existing fields ...
    public List<string> PostWorkloadSteps { get; set; } = new();
    public string DefaultShell { get; set; } = "powershell";
}
```