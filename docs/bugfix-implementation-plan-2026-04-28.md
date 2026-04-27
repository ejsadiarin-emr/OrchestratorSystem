# Bugfix Implementation Plan: Agent Stuck "Running" & Pipeline Failures

**Date:** 2026-04-28
**Branch:** `bugfix-six-bugs` (worktree: `.worktrees\bugfix-worktree\`)
**Status:** Planning — not yet implemented
**Priority:** P0 (blocks all VM deployment scenarios)

---

## Executive Summary

The Dev Stack v1 workload stuck in "Running" indefinitely on AGENT1 (clean VM). Investigation identified 6 bugs across 4 files. The root cause is a fire-and-forget pipeline that uses the host's `stoppingToken` for terminal status updates — when cancelled, the PATCH silently fails, leaving the orchestrator-side run in "Running" forever. Additionally, MSI installs silently fail (exit 1603) because the UAC elevation path is unreachable with `UseShellExecute=false`.

---

## Bug Reference Table

| # | ID | Severity | Title | File | Lines |
|---|---|---|---|---|---|
| 1 | RUN-001 | CRITICAL | Pipeline terminal PATCH uses `stoppingToken` — run stuck "Running" | `AgentRuntimeService.cs` | 215, 236, 254 |
| 2 | RUN-002 | HIGH | MSI install UAC retry unreachable — exit 1603 on non-admin VM | `InstallOrUpgrade.cs` | 70, 103-148 |
| 3 | DL-001 | HIGH | No total-bytes validation after chunked download — incomplete = success | `AcquireArtifact.cs` | 149-165 |
| 4 | DL-002 | MEDIUM | `IsValidContentRange` rejects valid 206 responses when total length omitted | `AcquireArtifact.cs` | 400 |
| 5 | PIPE-001 | MEDIUM | `CancellationTokenSource.CreateLinkedTokenSource(ct)` leaked ×3 | `PipelineExecutor.cs` | 47, 85, 125 |
| 6 | DL-003 | MEDIUM | 200 OK fallback discards partial data with no length check | `AcquireArtifact.cs` | 133-138 |

---

## Bug 1 (RUN-001) — CRITICAL

### Pipeline terminal PATCH uses `stoppingToken` — run stuck "Running"

**Symptom:** AGENT1 workload stays "Running" forever on the orchestrator. No terminal status (Completed/Failed) is ever recorded.

**Root cause:** `AgentRuntimeService.cs` launches the pipeline as fire-and-forget:

```csharp
// Line 215
_ = Task.Run(async () => {
    var result = await _pipelineExecutor.ExecuteAsync(context, packageSteps, ct);
    // ...
    await http.PatchAsJsonAsync(..., ct);       // Line 239-245 (success path)
    // OR
    await http.PatchAsJsonAsync(..., ct);       // Line 254-258 (failure path)
}, ct);
```

The `ct` parameter comes from `stoppingToken` (the `BackgroundService` lifetime token). When the service shuts down or the token fires:

1. `Task.Run(..., ct)` cancels before the lambda starts — the PATCH **never executes**.
2. If the lambda is already running, `ExecuteAsync` throws `OperationCanceledException` — caught by `catch (Exception)` which then tries to PATCH with the **already-cancelled** `ct` — this PATCH also fails silently via the inner `catch { /* best effort */ }`.

The orchestrator never receives a terminal status, so the run stays "Running" indefinitely.

**Additional issue:** `_ = Task.Run(...)` discards the `Task` reference. Any exception thrown outside the try/catch (e.g., `NullReferenceException` from a malformed response) becomes an unobserved task exception.

### Fix

**File:** `apps/agent/backend/Services/AgentRuntimeService.cs`

**Replace** the fire-and-forget block (approximately lines 215-258) with:

```csharp
// Use CancellationToken.None for the pipeline Task.Run so the task always runs to completion.
// Pass the stoppingToken-derived ct to PipelineExecutor so it can still be cancelled on shutdown,
// but use CancellationToken.None for the terminal PATCH calls so the orchestrator always
// receives the final status.
_ = Task.Run(async () => {
    try
    {
        var result = await _pipelineExecutor.ExecuteAsync(context, packageSteps, ct);

        // ALWAYS use CancellationToken.None for terminal status updates
        using var successClient = _httpClientFactory.CreateClient("Orchestrator");
        // ... (existing success PATCH logic, but with CancellationToken.None)
        await successClient.PatchAsJsonAsync(url, payload, CancellationToken.None);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Pipeline execution failed for run {RunId}", runId);
        try
        {
            using var failureClient = _httpClientFactory.CreateClient("Orchestrator");
            // ... (existing failure PATCH logic, but with CancellationToken.None)
            await failureClient.PatchAsJsonAsync(url, payload, CancellationToken.None);
        }
        catch { /* best effort */ }
    }
});
```

Key changes:
- `Task.Run` lambda no longer receives `ct` as a cancellation token (or receives `CancellationToken.None`)
- All terminal PATCH calls use `CancellationToken.None` to ensure delivery
- The pipeline executor still receives `ct` for cooperative cancellation of the download/install operations

### Testing

- **Unit test:** Mock `HttpClient` to verify PATCH is called even when `CancellationToken` is cancelled.
- **Integration test:** Start pipeline, cancel `stoppingToken` mid-execution, verify orchestrator receives "Failed" status.
- **Manual test on AGENT1:** Deploy workload, kill agent process mid-execution, verify run transitions to "Failed" on orchestrator.

---

## Bug 2 (RUN-002) — HIGH

### MSI install UAC retry unreachable — exit 1603 on non-admin VM

**Symptom:** On a clean Windows VM without admin elevation, MSI packages fail with `exit_code_1603`. The UAC elevation retry path (lines 103-148) is never triggered.

**Root cause:** `InstallOrUpgrade.cs` sets `UseShellExecute = false` (line 70). With this setting:

- `Process.Start("msiexec", ...)` succeeds — the process launches in the caller's context
- `msiexec.exe` itself requires elevation for `/i` (install) operations
- Since `UseShellExecute = false`, Windows never checks the application manifest for elevation requirements
- `Process.Start` does NOT throw `Win32Exception(740)` — that only happens with `UseShellExecute = true`
- `msiexec` exits with code 1603 (fatal error, insufficient privileges)
- The catch block at line 103 (`catch (Win32Exception ex) when (ex.NativeErrorCode == 740)`) is **unreachable**

The exit code check at line 90 correctly detects the failure and returns `Success = false, Error = "exit_code_1603"`, but no UAC elevation prompt or retry occurs.

**Secondary issue:** For `.exe` installers with elevation manifests, the behavior is inconsistent. Some Windows configurations may throw 740 even with `UseShellExecute = false`, but this is not reliable.

### Fix

**File:** `apps/agent/backend/Steps/InstallOrUpgrade.cs`

**Strategy:** Detect exit code 1603 and retry with UAC elevation. Also refactor the UAC path to be more accessible.

```csharp
// After the existing process.WaitForExit() block (approximately line 87-101):
if (process.ExitCode != 0 && !expectedExitCodes.Contains(process.ExitCode))
{
    // Exit code 1603 = ERROR_INSTALL_FAILURE, commonly caused by insufficient privileges
    if (process.ExitCode == 1603)
    {
        _logger.LogWarning(
            "Installer exited 1603 (insufficient privileges). Retrying with UAC elevation for {Artifact}",
            artifactPath);

        return await ExecuteWithElevationAsync(
            command, arguments, artifactPath, workingDirectory, expectedExitCodes, ct);
    }

    return new InstallResult
    {
        Success = false,
        Error = $"exit_code_{process.ExitCode}",
        StandardError = stderr
    };
}
```

**Additionally**, update `ExecuteWithElevationAsync` to handle MSI specifically:

```csharp
private async Task<InstallResult> ExecuteWithElevationAsync(
    string command, string arguments, string artifactPath,
    string? workingDirectory, int[] expectedExitCodes, CancellationToken ct)
{
    var psi = new ProcessStartInfo
    {
        FileName = command,
        Arguments = arguments,
        UseShellExecute = true,
        Verb = "runas",           // Triggers UAC elevation prompt
        WorkingDirectory = workingDirectory ?? Path.GetDirectoryName(artifactPath)!,
        CreateNoWindow = false    // Required for UAC prompt visibility
    };

    using var elevatedProcess = new Process { StartInfo = psi };
    elevatedProcess.Start();
    // ... (rest of existing elevation logic, wait for exit, etc.)
}
```

**Important consideration for agent/unattended scenarios:** On AGENT1 (unattended VM), a UAC prompt **requires interactive user input**. If no user is logged in or the session is non-interactive, the UAC prompt will time out or fail. The fix should:

1. Set the Windows service to run as SYSTEM (which has implicit admin)
2. OR configure the agent to run under an admin account
3. OR use `msiexec /a` (admin install) if deploying to a network location
4. OR configure Windows policy to auto-elevate for the agent's service account

For **unattended remote deployment** (the AGENT1 use case), the agent should run as a Windows Service under LOCAL_SYSTEM or a domain account with local admin rights. The UAC elevation path serves as a **fallback for interactive sessions**.

### Testing

- **Unit test:** Provide a mock MSI that returns 1603, verify `ExecuteWithElevationAsync` is called.
- **Manual test:** Deploy to AGENT1 (non-admin VM) and verify either: (a) elevation prompt appears (interactive), or (b) agent runs as SYSTEM and install succeeds.
- **Edge case:** Verify `.exe` installers with elevation manifests still work via the Win32Exception(740) path.

---

## Bug 3 (DL-001) — HIGH

### No total-bytes validation after chunked download — incomplete = success

**Symptom:** An artifact download could be incomplete (truncated by network error) but reported as successful, leading to corrupt MSI files that fail installation.

**Root cause:** After the chunked download while-loop at line 149, the code reads `bytesWritten = output.Length` and proceeds to `FinalizeResultAsync` which only checks SHA256 (if provided). There is no validation that `bytesWritten == expectedLength`.

```csharp
// Line 149 (after the while loop)
bytesWritten = output.Length;

// Lines 153-166 — no check that bytesWritten == expectedLength
var result = await FinalizeResultAsync(output, package, bytesWritten, downloadFailure, ct);
```

If a network error occurs mid-transfer and `CopyToAsyncCountingBytes` returns fewer bytes than `expectedChunkLength`, the `downloadFailure` check at line 121 would catch it. But if the stream ends cleanly (e.g., server closes connection gracefully after sending partial data), `CopyToAsyncCountingBytes` may return a value that doesn't match `expectedChunkLength` but the while loop condition `from < expectedLength.Value` might still be false due to the `from = to + 1` update at line 118 not having been executed (since the chunk body was shorter than the range request).

Additionally, if `expectedLength` is null/0, the code falls through to `DownloadFullAsync` which performs no length validation at all.

### Fix

**File:** `apps/agent/backend/Steps/AcquireArtifact.cs`

**After line 149** (after `bytesWritten = output.Length;`), add:

```csharp
if (expectedLength.HasValue && expectedLength.Value > 0 && bytesWritten != expectedLength.Value)
{
    _logger.LogError(
        "Download size mismatch for {Package}: expected {Expected} bytes, got {Actual}",
        package.Name, expectedLength.Value, bytesWritten);

    return new AcquireArtifactResult
    {
        Success = false,
        Error = $"content_length_mismatch: expected {expectedLength.Value} bytes, got {bytesWritten}",
        LocalPath = null
    };
}
```

**In `DownloadFullAsync`** (approximately line 169-178), add similar validation:

```csharp
private async Task<long> DownloadFullAsync(Uri artifactUri, Stream output, CancellationToken ct)
{
    using var response = await _http.GetAsync(artifactUri, HttpCompletionOption.ResponseHeadersRead, ct);
    response.EnsureSuccessStatusCode();

    var declaredLength = response.Content.Headers.ContentLength;
    await response.Content.CopyToAsync(output, ct);

    if (declaredLength.HasValue && output.Length != declaredLength.Value)
    {
        throw new InvalidOperationException(
            $"Full download size mismatch: declared {declaredLength.Value} bytes, got {output.Length}");
    }

    return output.Length;
}
```

### Testing

- **Unit test:** Serve a truncated response (Content-Length says 30MB, but only send 15MB then close), verify `AcquireArtifactResult.Success == false` and error contains "content_length_mismatch".
- **Unit test:** Serve full response matching Content-Length, verify success.
- **Integration test:** Deploy agent, simulate network interruption during download, verify run transitions to "Failed" (not stuck "Running" — this depends on Bug 1 fix).

---

## Bug 4 (DL-002) — MEDIUM

### `IsValidContentRange` rejects valid 206 responses when total length omitted

**Symptom:** Downloads fail with `invalid_partial_content_range` on servers that send `Content-Range: bytes 0-999999/*` (omitting total length).

**Root cause:** At line 400:

```csharp
return contentRange.Length is not null && contentRange.Length.Value == expectedLength;
```

This requires `contentRange.Length` to be non-null and equal to `expectedLength`. Per RFC 7233, the total length in Content-Range can be `*` (unknown), which ASP.NET Core parses as `null`. Valid CDN/proxy servers may omit the total length.

### Fix

**File:** `apps/agent/backend/Steps/AcquireArtifact.cs`

**Replace line ~400** with:

```csharp
// Content-Range total length is optional (RFC 7233 allows */total-length or */*)
// Only validate the total length if the server provides it
if (contentRange.Length.HasValue)
{
    return contentRange.Length.Value == expectedLength;
}
// If total length is unknown (*), validate that the range boundaries are correct
return contentRange.From.HasValue && contentRange.To.HasValue
    && contentRange.To.Value >= contentRange.From.Value;
```

### Testing

- **Unit test:** Send 206 response with `Content-Range: bytes 0-999999/*`, verify `IsValidContentRange` returns `true`.
- **Unit test:** Send 206 response with `Content-Range: bytes 0-999999/2000000`, verify returns `true` when `expectedLength == 2000000` and `false` when `expectedLength != 2000000`.

---

## Bug 5 (PIPE-001) — MEDIUM

### `CancellationTokenSource.CreateLinkedTokenSource(ct)` leaked ×3

**Symptom:** Resource leak — every pipeline execution creates 3+ un-disposed `CancellationTokenSource` objects that pressure the GC and kernel handle pool.

**Root cause:** `PipelineExecutor.cs` lines 47, 85, 125:

```csharp
var stepCt = CancellationTokenSource.CreateLinkedTokenSource(ct).Token;
```

`CancellationTokenSource.CreateLinkedTokenSource` returns a disposable `CancellationTokenSource`. The code extracts `.Token` and discards the source, making it impossible to dispose. Over many polling cycles, this leaks kernel handles.

### Fix

**File:** `apps/agent/backend/Services/PipelineExecutor.cs`

**Option A (simplest):** Since no per-step timeout is added (unlike `InstallOrUpgrade.cs` which adds a command timeout), just use `ct` directly:

```csharp
// Replace all 3 occurrences:
var stepCt = ct;  // No linked source needed — no per-step timeout
```

**Option B (if per-step cancellation might be needed later):**

```csharp
foreach (var step in phaseSteps)
{
    using var stepCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    var stepCt = stepCts.Token;
    // ... (existing loop body)
}
```

**Recommendation:** Option A is preferred. The linked token source adds no value here since no per-step timeout is applied. If timeouts are needed later, add them with proper `using var` disposal.

### Testing

- **Unit test:** Verify pipeline execution still respects cancellation.
- **Manual check:** No `CancellationTokenSource.CreateLinkedTokenSource` calls remain in `PipelineExecutor.cs` without `using var`.

---

## Bug 6 (DL-003) — MEDIUM

### 200 OK fallback discards partial data with no length check

**Symptom:** If the server switches from 206 to 200 mid-download, all previously downloaded data is silently discarded and replaced with whatever the 200 response contains, with no validation.

**Root cause:** `AcquireArtifact.cs` lines 133-138:

```csharp
if (response.StatusCode == HttpStatusCode.OK)
{
    output.SetLength(0);    // Discards all previously downloaded chunks
    output.Position = 0;
    await response.Content.CopyToAsync(output, ct);
    break;                   // Exits while loop — assumed complete
}
```

If a 206 response was successfully received for an earlier chunk, and then the server sends 200 (full content) for a subsequent chunk request, the code:
1. Truncates the output stream to zero, losing all progress
2. Writes the 200 response body to output
3. `CopyToAsync` has NO length validation — truncated 200 responses silently accepted

### Fix

**File:** `apps/agent/backend/Steps/AcquireArtifact.cs`

**Replace lines 133-138** with:

```csharp
if (response.StatusCode == HttpStatusCode.OK)
{
    var contentLength = response.Content.Headers.ContentLength;

    // If we already have partial data from 206 responses, and the server
    // now returns 200 OK, restart the download from scratch with full content.
    _logger.LogInformation(
        "Server returned 200 OK after partial 206 responses. " +
        "Restarting download with full content for {Package}",
        package.Name);

    output.SetLength(0);
    output.Position = 0;
    await response.Content.CopyToAsync(output, ct);

    // Validate downloaded length matches Content-Length if available
    if (contentLength.HasValue && output.Length != contentLength.Value)
    {
        return new AcquireArtifactResult
        {
            Success = false,
            Error = $"content_length_mismatch on 200 OK: " +
                    $"declared {contentLength.Value} bytes, got {output.Length}",
            LocalPath = null
        };
    }

    // Set bytesWritten for outer scope validation
    bytesWritten = output.Length;
    break;
}
```

### Testing

- **Unit test:** Server returns 206 for first chunk, 200 for second chunk. Verify full download succeeds if 200 response is complete.
- **Unit test:** Server returns 206 for first chunk, truncated 200 for second chunk. Verify `AcquireArtifactResult.Success == false` with content_length_mismatch error.

---

## Implementation Order

Fix in this order to unblock AGENT1 deployments as quickly as possible:

| Priority | Bug ID | Rationale |
|---|---|---|
| 1 | RUN-001 | Root cause of "Running" stuck state — must fix first |
| 2 | RUN-002 | MSI 1603 is the second most visible failure on VMs |
| 3 | DL-001 | Incomplete downloads could corrupt artifacts silently |
| 4 | DL-002 | Accept `*` in Content-Range for CDN compatibility |
| 5 | PIPE-001 | Resource leak — easy fix, low risk |
| 6 | DL-003 | 200 fallback rarely triggered, but data loss when it is |

---

## Files to Modify

| File | Bugs Addressed |
|---|---|
| `apps/agent/backend/Services/AgentRuntimeService.cs` | RUN-001 |
| `apps/agent/backend/Steps/InstallOrUpgrade.cs` | RUN-002 |
| `apps/agent/backend/Steps/AcquireArtifact.cs` | DL-001, DL-002, DL-003 |
| `apps/agent/backend/Services/PipelineExecutor.cs` | PIPE-001 |

---

## Verification Plan

After implementing all fixes, verify with this sequence:

1. **Build:** `dotnet build` in both agent and orchestrator projects
2. **Unit tests:** Run existing test suite, add new tests for each bug
3. **Local integration test:**
   - Deploy orchestrator + agent locally
   - Run Dev Stack v1 workload (nodejs + python)
   - Verify: run transitions to "Completed" or "Failed" (not stuck "Running")
   - Verify: python installs successfully (or fails with clear error)
4. **AGENT1 integration test:**
   - Deploy to clean VM (no nodejs/python, non-admin)
   - Run Dev Stack v1 workload
   - Verify: run transitions away from "Running" (to "Failed" if MSI elevation needed)
   - Verify: if agent runs as SYSTEM/admin, MSI install succeeds
5. **Kill-agent test:** Start workload, kill agent process mid-execution. Verify orchestrator does NOT leave run in "Running" state (should timeout or detect agent disconnection).

---

## Previously Fixed Bugs (on `bugfix-six-bugs` branch)

These 6 bugs were already fixed and are in the worktree. They are listed here for reference but do NOT need implementation:

1. MSI files now invoke `msiexec /i` instead of direct execution
2. Binary name aliases (nodejs → node, python → python3)
3. Version normalization strips comparison operators
4. ResolvePlaceholderAdapter reads Detection section
5. Frontend uses `artifact.packageEntityId ?? artifact.id`
6. BulkImport persists `DetectionConfigJson`

---

## Assumptions

- **Agents run with admin privileges.** The agent service is assumed to run as LOCAL_SYSTEM or under a service account with local administrator rights. Bug 2 (RUN-002) addresses the case where `UseShellExecute=false` prevents the UAC elevation path from being reached. Since agents already run with admin privileges, the primary fix is to detect exit code 1603 and retry with `UseShellExecute=true, Verb="runas"` to trigger the shell elevation path — this will work correctly when the agent already has admin rights. No changes to the deployment model or service account configuration are needed.

## Risks & Open Questions

1. **Download timeout:** No explicit download timeout on `AcquireArtifact`. If the VM-to-host network is very slow, the agent may appear stuck for minutes. Consider adding a configurable download timeout (e.g., 10 minutes per artifact).

2. **Retry logic:** Currently, if a download fails, the pipeline halts immediately. Consider adding retry logic (3 retries with exponential backoff) for transient network errors on AGENT1's local network.

3. **Concurrent polling:** `AgentRuntimeService` polls every N seconds. If a pipeline takes longer than the poll interval, the next poll may find the same pending run and launch a duplicate pipeline. Verify that the `PATCH` to claim the run (status → "Running") is idempotent and prevents double-execution.