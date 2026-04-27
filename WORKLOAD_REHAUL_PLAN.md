# Workload Installation Rehaul — Final Implementation Plan

> **Status:** Approved for implementation. All review amendments folded in.

---

## The Real Situation in One Sentence

> The dispatch works, the pipeline architecture is fine, but **one stub method causes every package to report "already installed"** — so nothing ever runs.

---

## Phase 1 — Unblock the Pipeline (Do This First)

### 1a. Fix `DetectVersionManifestAsync` Stub

**File:** `apps/agent/backend/Steps/PackageDetector.cs`

```csharp
// CURRENT (broken)
return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied });

// FIXED
return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent });
```

### 1b. Fix `DetectRegistryAsync` Stub

**Same file, same pattern:**

```csharp
// CURRENT (broken)
return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.AlreadySatisfied });

// FIXED
return Task.FromResult(new PreCheckResult { Status = PreCheckStatus.NotPresent });
```

### 1c. Verify Immediately

Run a workload. Logs should now show `Added=3` instead of `Unchanged=3`. If you see `InstallOrUpgrade` being called, the fix worked — **before touching any dispatch code.**

---

## Phase 2 — Replace SignalR Dispatch with HTTP Polling

### 2a. Orchestrator — New/Modified Endpoints

#### `GET /api/workloadruns/pending?agent_id={guid}`

Returns pending runs for the agent, with per-package download URLs.

```json
{
  "runId": "...",
  "workloadName": "Dev Tools Stack",
  "packages": [
    {
      "packageEntityId": "57aca7ca-...",
      "name": "nodejs",
      "version": "24.13.0",
      "filename": "node-v24.13.0-x64.msi",
      "downloadUrl": "/api/artifacts/57aca7ca-.../download"
    }
  ]
}
```

#### `PATCH /api/workloadruns/{runId}` (Atomic Claim)

```csharp
var updated = await _db.WorkloadRuns
    .Where(r => r.Id == runId && r.Status == "Queued")
    .ExecuteUpdateAsync(s => s
        .SetProperty(r => r.Status, update.Status)
        .SetProperty(r => r.UpdatedAt, DateTime.UtcNow));

if (updated == 0) return Conflict(); // already claimed
```

**Status values:** `InProgress`, `Done`, `Failed`, `Queued` (manual reset).

#### `POST /api/workloadruns/{runId}/timeline`

Step-level status reporting (replaces SignalR `SendMessage`).

```csharp
_db.WorkloadRunTimelines.Add(new WorkloadRunTimelineEntity {
    RunId = runId,
    Step = dto.Step,
    Status = dto.Status,
    Message = dto.Message,
    Timestamp = DateTime.UtcNow
});
```

#### `WorkloadRunDispatcher.cs`

Keep the method, **comment out** the SignalR push at the end. Add `// TODO: remove after polling validated`. The run sits as `Queued` until the agent polls it.

### 2b. Agent — Poll Loop

**`AgentRuntimeService.cs`** — replace SignalR `AssignRun` handler with:

```csharp
protected override async Task ExecuteAsync(CancellationToken stoppingToken)
{
    _logger.LogInformation("Agent polling loop started. NodeId={NodeId}", _nodeId);
    
    while (!stoppingToken.IsCancellationRequested)
    {
        try
        {
            await PollAndProcessAsync(stoppingToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Poll cycle failed — will retry");
        }
        
        await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
    }
}

private async Task PollAndProcessAsync(CancellationToken ct)
{
    var runs = await _httpClient.GetFromJsonAsync<List<WorkloadRunDto>>(
        $"/api/workloadruns/pending?agent_id={_nodeId}", ct);
    
    foreach (var run in runs ?? [])
    {
        // Atomic claim
        var claim = await _httpClient.PatchAsJsonAsync(
            $"/api/workloadruns/{run.Id}", 
            new { status = "InProgress" }, ct);
        
        if (!claim.IsSuccessStatusCode) continue; // already claimed
        
        _ = Task.Run(() => _pipelineExecutor.ExecuteAsync(run, ct), ct);
    }
}
```

### 2c. Agent — HTTP Status Reporting

**`PipelineExecutor.cs`** — replace `_hubConnection.SendAsync("SendMessage", ...)` with:

```csharp
// Step status
await _httpClient.PostAsJsonAsync(
    $"/api/workloadruns/{runId}/timeline",
    new { step = stepName, status = stepStatus, message = detail });

// Final status
await _httpClient.PatchAsJsonAsync(
    $"/api/workloadruns/{runId}",
    new { status = success ? "Done" : "Failed", error = errorMessage });
```

### 2d. Artifact Download by `PackageEntityId`

Add `GET /api/artifacts/{packageEntityId}/download` to avoid special-character issues in `name/version` URLs. Agent uses the `downloadUrl` provided in the pending-run DTO.

---

## Phase 3 — Verification Order

| Step | What to Check |
|---|---|
| 1 | UI deploy button → run appears as `Queued` in DB |
| 2 | Agent polls → claims run (`InProgress`) → logs show `Added > 0` |
| 3 | `AcquireArtifact` downloads from `downloadUrl` |
| 4 | `InstallOrUpgrade` runs → package physically present on agent machine |
| 5 | `POST /timeline` events appear in DB |
| 6 | `PATCH` final status → `Done` in DB, reflected in UI |
| 7 | Test failure path: bad package → `Failed` with error message |

---

## Phase 4 — Hardening (If Time)

- [ ] `GET /api/workloadruns?agent_id=X` for UI status view
- [ ] Handle runaway `InProgress` (document as known limitation; manual reset via `PATCH status: Queued`)
- [ ] Remove commented SignalR code once polling is validated

---

## What to Leave Alone

| Component | Reason |
|---|---|
| `PipelineExecutor` step architecture | Correct, just blocked by stubs |
| `AcquireArtifact` HTTP download logic | Works when called; only URL source changes |
| `InstallOrUpgrade` subprocess logic | Works when reached; Windows edge cases already handled |
| `WorkloadRunEntity` schema | Use it, don't change it |
| Existing artifact serving (`name/version`) | Keep as fallback; add `packageEntityId` route |

---

## Known Limitations (Documented)

1. **Runaway `InProgress`:** If agent crashes after claiming, run stays stuck. Recovery: manually `PATCH` back to `Queued`.
2. **Double-agent race:** Extremely unlikely in POC. Mitigated by atomic `WHERE Status = 'Queued'` claim.

---

## Checklist

```
PHASE 1
  [ ] Fix DetectVersionManifestAsync stub → NotPresent
  [ ] Fix DetectRegistryAsync stub → NotPresent
  [ ] Verify pipeline logs show Added > 0
  [ ] Confirm package physically present on agent machine post-run

PHASE 2
  [ ] Add GET /api/workloadruns/pending?agent_id= with package downloadUrls
  [ ] Add PATCH /api/workloadruns/{runId} (atomic claim + final status)
  [ ] Add POST /api/workloadruns/{runId}/timeline (step events)
  [ ] Add GET /api/artifacts/{packageEntityId}/download
  [ ] Comment out SignalR push in WorkloadRunDispatcher
  [ ] Replace AgentRuntimeService SignalR AssignRun with poll loop
  [ ] Replace PipelineExecutor SignalR SendMessage with HTTP POST timeline

PHASE 3
  [ ] UI deploy → Queued in DB
  [ ] Agent polls → claims → pipeline executes
  [ ] Package installed on agent machine
  [ ] Status → Done in DB, UI reflects it
  [ ] Failure path tested
```

---

*Plan version: Final (post-review)*
*Last updated: 2026-04-27*
