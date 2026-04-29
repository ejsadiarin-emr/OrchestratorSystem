# Decision: Init Steps Exit Code Handling

**Date:** 2026-04-30
**Status:** Resolved

## Q11: Exit Codes for Init Step Commands

**Decision:** Always treat exit code `0` as success. No configurable `ExpectedExitCodes` for init steps.

### Rationale

- Init steps are user-authored shell commands, not opaque installer binaries
- A well-written shell command returns 0 on success
- If a command needs special handling (e.g., `schtasks` returning 1), the workload author wraps it in a PowerShell script that normalizes the exit code
- Keeps the schema simple — no `expectedExitCodes` field on step entries
- Contrast with `InstallAdapterConfig.ExpectedExitCodes` which handles opaque installers (e.g., MSI returning 3010 for reboot-required)