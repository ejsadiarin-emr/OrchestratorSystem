# 003 - Agent Detect Endpoint (`POST /api/detect`)

## Type

AFK

## Parent PRD

[docs/prd-workload-run-polish.md](../../prd-workload-run-polish.md)

## Blocked by

None — can start immediately.

## What to build

Add a new HTTP endpoint on the agent that accepts a list of package detection configs and returns per-package presence/version using the existing `PackageDetector`. This enables the orchestrator to query real filesystem state before creating runs.

**Shared Contracts (`shared/contracts/`):**

New files:
- `Runtime/Probes/DetectRequest.cs`: `PackageDetectionRequest` (packageId, name, version, DetectionConfig) and `DetectRequest` (List of packages)
- `Runtime/Probes/DetectResponse.cs`: `PackageDetectionResult` (packageId, name, PreCheckStatus, actualVersion) and `NodeDetectResponse` (List of results, DiskInfo)

`PreCheckStatus` enum values: `AlreadySatisfied`, `WrongVersion`, `NotPresent`.

`DiskInfo` includes `freeBytes`, `totalBytes`, `drive`.

**Agent (`apps/agent/backend/`):**

- `Program.cs`: Add `app.MapPost("/api/detect", ...)` endpoint
- New file `Steps/DetectEndpointHandler.cs`: Parse detect request, call `PackageDetector.DetectAsync()` for each package in the request, collect results, return `NodeDetectResponse` with per-package status + disk info. Reuses existing `PackageDetector` — no new detection logic.

**Endpoint contract:**

```
POST http://{node.IpAddress}:5001/api/detect
Content-Type: application/json

{
  "packages": [
    {
      "packageId": "3fa85f64-...",
      "name": "dbeaver",
      "version": "24.3.0",
      "detection": {
        "type": "file",
        "path": "C:\\Program Files\\DBeaver\\dbeaver.exe",
        "expectedVersion": "24.3.0"
      }
    }
  ]
}

Response:
{
  "results": [
    {
      "packageId": "3fa85f64-...",
      "name": "dbeaver",
      "status": "AlreadySatisfied",
      "actualVersion": "24.3.0.0"
    }
  ],
  "diskInfo": {
    "freeBytes": 123456789,
    "totalBytes": 500000000,
    "drive": "C:\\"
  }
}
```

## Acceptance criteria

- [ ] `PackageDetectionRequest` and `PackageDetectionResult` DTOs exist in `shared/contracts/Runtime/Probes/`
- [ ] `NodeDetectResponse` includes `results` array and `diskInfo`
- [ ] `PreCheckStatus` enum has `AlreadySatisfied`, `WrongVersion`, `NotPresent`
- [ ] `POST /api/detect` endpoint is registered in agent `Program.cs`
- [ ] `DetectEndpointHandler` parses request, calls `PackageDetector.DetectAsync()` per package, and returns per-package results
- [ ] Disk info (`freeBytes`, `totalBytes`, `drive`) is included in response
- [ ] Detection reuses existing `PackageDetector` logic (file, version_manifest; registry stub returns `NotPresent`)
- [ ] Request with empty packages array returns 200 with empty results (not 400)
- [ ] `dotnet build` succeeds for agent and contracts projects

## Referenced decisions

- [D3: Pre-Check Scope — Package-Level, Workload-Scoped](../../decisions/workload-run-polish-uninstall-precheck.md#d3-pre-check-scope--package-level-workload-scoped)
- [D15: Probe Communication — Direct HTTP](../../decisions/workload-run-polish-uninstall-precheck.md#d15-probe-communication--direct-http)
