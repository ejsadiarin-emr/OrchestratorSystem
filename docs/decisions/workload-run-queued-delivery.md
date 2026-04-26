# Workload Runs Stuck Queued — Root Cause and Fix Decision

## Context

### The Problem
Workload runs created via the orchestrator UI would remain in the `Queued` state indefinitely, never transitioning to `Assigned`, `Running`, or any terminal state. This happened consistently when:
- An agent was offline at the time the run was created
- Or even when the agent was online but the `AssignRun` SignalR message was dispatched

The UI showed runs as `Queued` forever. The agent logs showed connection and identification but never showed receipt of the `AssignRun` message (or showed receipt but then silence).

### Previous Assumptions
Earlier investigation assumed the issue was a **timing/race condition**: SignalR groups do not queue messages for empty groups, so if the agent had not yet called `Identify` and joined its group, the `AssignRun` message would be silently dropped. This led to the implementation of orchestrator-side re-send logic (see Decision below).

However, deeper instrumentation revealed that even when the agent was connected and the orchestrator successfully sent `AssignRun`, the agent would log receipt of the envelope but then produce **no further logs** — no `Received AssignRun: Workload=...`, no `Sent AckClaim`, no exception.

### Root Cause Analysis

#### SignalR JSON Protocol vs. C# Deserializer Mismatch
SignalR's default `JsonHubProtocol` uses **camelCase** for property names. The orchestrator serializes `AssignRunPayload` with camelCase keys (`nodeId`, `runId`, `workloadName`, `packages`, etc.).

On the agent side, `AgentRuntimeService.ParseAssignRunPayload()` received the payload as a `JsonElement` and called:

```csharp
JsonSerializer.Deserialize<AssignRunPayload>(jsonElement.GetRawText())
```

This used **default** `JsonSerializerOptions`, which are:
- `PropertyNamingPolicy = null` (expects PascalCase)
- `PropertyNameCaseInsensitive = false`

Because the JSON properties were camelCase and the deserializer expected PascalCase, **every property was silently ignored**. `AssignRunPayload` was instantiated with all default values:
- `NodeId = Guid.Empty`
- `RunId = Guid.Empty`
- `WorkloadName = ""`
- `Packages = new List<PackageAssignment>()` (collection initializer preserved)

The code then reached the NodeId filter:

```csharp
if (payload.NodeId != nodeId)
{
    _logger.LogDebug("Ignoring AssignRun for NodeId={TargetNodeId}, we are {OurNodeId}", ...);
    return;
}
```

Since `payload.NodeId` was `Guid.Empty` and the agent's real `nodeId` was a valid GUID, the filter returned early. The log was at **Debug** level, invisible in production. No exception was thrown. The run stayed `Queued` forever with zero visible evidence.

**This is why no exception was ever logged despite the `try/catch` block — the deserialization "succeeded" (produced a valid object), it just produced the wrong object.**

#### Secondary Issue: Orchestrator-Side Deserialization
The orchestrator's `NodeWorkloadStateService.TryDeserializePayload()` had the **same bug** for agent-sent messages (`StepStatus`, `Complete`, `Fail`, `Heartbeat`). Once the agent started working, these messages would also silently fail to deserialize, breaking the orchestrator's ability to track run progress.

#### Tertiary Issue: Missing Connection Error Visibility
`AgentRuntimeService` registered `Reconnecting` and `Reconnected` handlers but did **not** register a `Closed` event handler. If an exception ever escaped the `On` handler, SignalR would catch it, close the connection, and pass it to `Closed`. Without a handler, the exception was silently swallowed.

---

## Decision

### Fix 1: Case-Insensitive Deserialization (Agent)
Change `ParseAssignRunPayload` to use `PropertyNameCaseInsensitive = true`:

```csharp
var options = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
};
return JsonSerializer.Deserialize<AssignRunPayload>(jsonElement.GetRawText(), options)
    ?? throw new InvalidOperationException("Failed to deserialize AssignRunPayload");
```

**Rationale:** SignalR's default JSON protocol is camelCase. We cannot control what SignalR sends, but we can control how we deserialize it. Case-insensitive matching is the simplest, most robust fix. Using `PropertyNamingPolicy = JsonNamingPolicy.CamelCase` would also work but is less forgiving if the protocol ever changes.

### Fix 2: Elevate NodeId Filter Log Level
Change the NodeId mismatch log from `LogDebug` to `LogWarning`:

```csharp
_logger.LogWarning("Ignoring AssignRun for NodeId={TargetNodeId}, we are {OurNodeId}", ...);
```

**Rationale:** A NodeId mismatch is never a normal condition. It means either a configuration error (agent has wrong NodeId) or a bug (payload corrupted/misrouted). It should be visible at the default `Information`+ log level.

### Fix 3: Add `Closed` Event Handler (Agent)
Add `Closed` event to `IHubConnection` and `HubConnectionWrapper`, and register a handler in `AgentRuntimeService`:

```csharp
_connection.Closed += ex =>
{
    if (ex is not null)
        _logger.LogError(ex, "SignalR connection closed with error");
    else
        _logger.LogWarning("SignalR connection closed");
    return Task.CompletedTask;
};
```

**Rationale:** Provides visibility into connection-level failures that would otherwise be silently swallowed by `WithAutomaticReconnect()`.

### Fix 4: Case-Insensitive Deserialization (Orchestrator)
Apply the same `PropertyNameCaseInsensitive = true` fix to `NodeWorkloadStateService.TryDeserializePayload()`.

**Rationale:** The agent sends camelCase JSON back to the orchestrator. The same silent-failure mode exists on the orchestrator side.

### Fix 5: Orchestrator Re-Send on Agent Identify
When an agent calls `Identify`, after adding it to its SignalR group, query the database for any `Queued` runs targeting that `NodeId` and re-send `AssignRun`.

**Rationale:** Even with the deserialization fix, the original timing problem still exists — if a run is created while the agent is offline, SignalR drops the `AssignRun` message because the group is empty. Re-sending on identify ensures queued runs are delivered when agents reconnect.

**Implementation:** Extracted `WorkloadRunDispatcher` service from `WorkloadRunsController` to avoid duplicating complex payload construction logic. `AgentRuntimeHub.Identify()` now calls `_dispatcher.DispatchQueuedRunsAsync(nodeId)` after `Groups.AddToGroupAsync()`.

### Fix 6: UI Cancel Button for Queued Runs
Add `'queued'` to the cancel button visibility condition in `WorkloadRuns.tsx`.

**Rationale:** If a run can be created and never proceed (agent offline, agent rejected it, etc.), the user needs a way to clean it up. The backend already supports cancellation.

---

## Resolved Decisions

### Q1: Why did no exception appear in logs?
**Answer:** `JsonSerializer.Deserialize` does not throw when properties don't match — it simply ignores them and produces an object with default values. The failure was **silent data loss**, not a thrown exception.

### Q2: Should we use `PropertyNameCaseInsensitive` or change SignalR's naming policy?
**Answer:** `PropertyNameCaseInsensitive = true` on the deserializer. We don't control the SignalR protocol defaults, and making our deserializer more tolerant is safer than changing global SignalR serialization settings which might affect other messages.

### Q3: Should the orchestrator re-send logic query `Queued` or `Assigned` runs?
**Answer:** Only `Queued`. `Assigned` runs have already been dispatched (the agent received them but may not have AckClaim'd yet). Re-sending `Assigned` runs could cause duplicate processing. `Queued` means the dispatch never reached the agent.

### Q4: Should `DispatchQueuedRunsAsync` change run state before sending?
**Answer:** No. The run stays `Queued` until the agent AckClaim's it. The orchestrator's `NodeWorkloadStateService` handles the `AckClaim` → `Assigned` transition. Changing state before send would lie about delivery.

### Q5: Why extract `WorkloadRunDispatcher` instead of inlining the re-send logic?
**Answer:** `WorkloadRunsController.Create()` already had ~150 lines of complex payload construction (package resolution, detection config, current packages lookup, MSI vs EXE command building). Duplicating this in `AgentRuntimeHub` would create a maintenance nightmare. A shared service is the only sane approach.

---

## Rejected Alternatives

### Alternative A: Change SignalR Protocol to PascalCase
Configure SignalR to use PascalCase naming globally. Rejected because:
- It affects all hubs and all clients
- JavaScript/TypeScript clients expect camelCase by convention
- The real problem is our deserializer being too strict, not SignalR's defaults being wrong

### Alternative B: Add `[JsonPropertyName]` Attributes to Contracts
Decorate every property in `AssignRunPayload` and related types with explicit camelCase names. Rejected because:
- Extremely verbose across the entire contracts project
- Easy to miss properties on future types
- `PropertyNameCaseInsensitive` solves it globally with one line

### Alternative C: Agent Polls for Pending Runs
Instead of orchestrator re-sending on identify, the agent could call `GET /api/nodes/{nodeId}/pending-runs` after identifying. Rejected because:
- Adds an extra round-trip and new API surface
- The orchestrator already knows the agent is back (it's calling `Identify`)
- Re-send on identify is simpler and pushes data rather than requiring the agent to pull

### Alternative D: Persistent Message Queue (RabbitMQ, Azure Service Bus)
Replace SignalR fire-and-forget with a persistent queue. Rejected for this PoC because:
- Massive infrastructure change
- Adds operational complexity (broker deployment, queue management)
- Re-send on identify is sufficient for the reliability level needed

---

## Verification

After applying all fixes and restarting both services:

1. Agent connected and identified successfully
2. Orchestrator found 1 queued run and re-sent `AssignRun`
3. Agent correctly deserialized payload: `Received AssignRun: Workload=Utility Pack, Packages=2`
4. Agent sent `AckClaim`, orchestrator processed it
5. Pipeline started: `Pipeline starting: RunId=..., Workload=Utility Pack, Mode=install`
6. Run transitioned from `Queued` → `Assigned` → `Running` in the UI

The run later failed due to a missing artifact (404 from `api/artifacts`), which is a **separate issue** unrelated to the queued-run delivery problem.

---

## References

- `apps/agent/backend/Services/AgentRuntimeService.cs` — `ParseAssignRunPayload`, `HandleAssignRunAsync`, connection event handlers
- `apps/agent/backend/Services/IHubConnection.cs` — `Closed` event addition
- `apps/agent/backend/Services/HubConnectionWrapper.cs` — `Closed` event wrapper
- `apps/orchestrator/backend/Services/NodeWorkloadStateService.cs` — `TryDeserializePayload` fix
- `apps/orchestrator/backend/Services/WorkloadRunDispatcher.cs` — New dispatcher service
- `apps/orchestrator/backend/Hubs/AgentRuntimeHub.cs` — `Identify` re-send logic
- `apps/orchestrator/backend/Controllers/WorkloadRunsController.cs` — Refactored to use dispatcher
- `apps/orchestrator/web/src/pages/WorkloadRuns.tsx` — UI cancel button for queued runs
