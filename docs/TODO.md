# as of 2026-04-24 03:45

- [ ] UI: remove all header cards in ALL pages, but move the unique description on the Topbar instead (ex. on root "/" page, have: "Workload-first triage surface for node health, run actions, and node-level evidence." - move this to Topbar's "Phase 1 workload-first operations console". Do all to other pages as well so Topbar description now becomes dynamic) 
- Home (/):
    - [ ] UI: remove action panel, action controls in Node details popup window modal/dialog
    - [ ] UI: NODES ONLINE status doesn't update, even though an agent/remote node was connected (showing status: online on /nodes page) - check for the other cards as well - need have real data frontend to backend wiring here
- Workloads (/workloads):
    - [ ] UI: Replace "Create Draft WorkloadDefinition" and "Create Workload Version Draft" cards in the UI to have drag and drop box functionality (like in /artifacts) 
    - [ ] Backend: Workloads have JSON schema (manifest/metadata) that is pre-defined already (see sample schema: ../sample-workload-definition.jsonc - can improve on this better) - note that for this PoC have like 2-3 packages/artifacts only per workload
        - This means that we need to handle this in the backend (if not yet implemented - we need to review and check on this, verify current reality of orchestrator backend)
        - Need to support BULK upload of workloads - meaning there's a global workloads.json that pre-defines MULTIPLE workloads in one JSON file 
        - "Updating" a workload is just inserting the new version (with new packages version - if have, AND preUpgradeActions defined). Retain the old version of the workload for backwards compatibility.
- [ ] Remove /agent-local page
    - We don't need this anymore - we don't even need an Agent web UI now.
- [ ] Need more test artifacts (zip file - bulk upload) and global JSON file with multiple workloads
    - artifacts = actual installer media + manifest/metadata (same name for bulk upload in a .zip flat file - to match artifact binaries to their corresponding manifests)
    - workloads referencing artifacts (in the zip file)

---
- TRY
    - test on powershell
    - test on VM for agent
    - test on personal windows
