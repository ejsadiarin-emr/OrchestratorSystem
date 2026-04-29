# Decision: Workload Import JSON Format for Init Steps

**Date:** 2026-04-30
**Status:** Resolved

## Q14: Workload Import JSON Format

**Decision:** Hybrid — packages can be strings OR objects in the import JSON.

### Format

```jsonc
{
  "name": "CustomApp1 Workload",
  "slug": "customapp1-workload",
  "version": "2.0.0",
  "defaultShell": "powershell",
  "packages": [
    "nodejs-24.13.0",                    // string shorthand (no init steps)
    {
      "name": "custom-app-1-v2",         // object form (with init steps)
      "preInitSteps": ["psql --version"],
      "postInitSteps": ["echo done"]
    }
  ],
  "postWorkloadSteps": [
    "COMMAND: Final verification"
  ]
}
```

### Backward compatibility

- String packages are deserialized as `{ name: value, preInitSteps: [], postInitSteps: [] }`
- Existing test data (`"nodejs-24.13.0"`) works unchanged
- New workloads can mix string and object forms in the same `packages` array

### Implementation

The `WorkloadImportService` will use a custom JSON converter that handles both `JsonTokenType.String` and `JsonTokenType.StartObject` for package entries.