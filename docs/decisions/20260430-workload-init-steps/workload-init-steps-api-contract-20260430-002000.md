# Decision: Init Steps API Contract Format

**Date:** 2026-04-30
**Status:** Resolved

## Q15: API Contract Format for Init Steps

**Decision:** Always objects in API contracts. Hybrid format is for import JSON only.

### Three-layer flow

| Layer | Format | Rationale |
|---|---|---|
| Import JSON file | Hybrid (string or object) | Ergonomic for humans authoring workload definitions |
| REST API (Frontend → Orchestrator) | Always object | Strict, typed, validated contract |
| Runtime payload (Orchestrator → Agent) | Always object | Already decided in Q3/Q5 |

### API contract changes

**WorkloadPackageInput** (Create revision request):
```csharp
public sealed class WorkloadPackageInput
{
    [Required] public Guid PackageId { get; set; }
    [Range(1, int.MaxValue)] public int PackageIndex { get; set; }
    public List<string> PreInitSteps { get; set; } = new();
    public List<string> PostInitSteps { get; set; } = new();
}
```

**CreateWorkloadRevisionRequest**:
```csharp
public sealed class CreateWorkloadRevisionRequest
{
    [Required][StringLength(64)] public string Version { get; set; } = string.Empty;
    [Required] public List<WorkloadPackageInput> Packages { get; set; } = new();
    public List<string> PostWorkloadSteps { get; set; } = new();
    public string DefaultShell { get; set; } = "powershell";
}
```