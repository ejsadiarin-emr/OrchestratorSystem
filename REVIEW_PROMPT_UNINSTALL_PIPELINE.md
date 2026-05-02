# Review Request: Uninstall Pipeline & Pre-check Architecture

NOTE: this is mostly for Windows

- We may need to create/edit scripts (for downloading artifacts & generating manifests/workloads) and Makefile for the commands if we want to test it locally.

BUT I just need you to do an in-depth review and a thorough audit that points out the ISSUES in the codebase, especially the logic. You may explore as you need to.

**USE SUBAGENTS AS NEEDED.**

## Problem Statement

The uninstall workflow in the Run Creator modal has several issues that need architectural review and refactoring:

### 1. Uninstall downloads artifacts unnecessarily
- **Current behavior:** When a package has no dedicated `UninstallCommand`, the `PipelineExecutor` downloads the full artifact to a temp path and passes it to `UninstallPackage.ExecuteAsync()` as a fallback.
- **Log evidence:** `Step AcquireArtifactForUninstall` initiates chunked download (~119MB artifact), then `UninstallPackage` fails with `exit_code_1`.
- **Expected behavior:** Uninstall should **NOT** download artifacts. It should either:
  - Use a dedicated `UninstallCommand` (e.g., `C:\Program Files\DBeaver\unins000.exe`) with `UninstallArgs`.
  - Or use system-level uninstall mechanisms (registry-based, winget, msiexec product code).
  - The artifact is the **INSTALLER**, not the uninstaller — downloading it for uninstall is wasteful and often wrong.

### 2. Pre-checks are manual and not informative
- **Current behavior:** User must click "Run pre-check" button manually.
- When issues are found, the badge shows "pre-check: issues" but doesn't display **WHAT** failed without hovering.
- **Expected behavior:** Pre-checks should happen automatically when the Run Creator modal opens (background), and results should be visible inline.

### 3. UI is cramped
- The Run Creator modal is `w-[min(92vw,48rem)]` — too narrow to show node details, pre-check results, and version info clearly.
- Node list shows multiple inline badges (OS, version, pre-check status, drift) causing overflow.

NOTE: YOU SHOULD check if the problem is at the agent's backend or orchestrator's backend.

## Context: Already Applied Fixes

1. **Backend (`NodeWorkloadStateResponse.cs`, `NodesController.cs`)**: Added `CurrentRevisionId` to fix uninstall node filtering.
2. **Frontend (`api.ts`, `types.ts`, `WorkloadRuns.tsx`)**: Fixed version string vs GUID mismatches in uninstall filtering.
3. **Agent (`PipelineExecutor.cs`)**: Removed early halt for missing `UninstallCommand` and added artifact acquisition fallback (this introduced the download problem).
4. **Frontend badge tooltip**: Enhanced to show actual failing pre-check items.

NOTE: VERIFY the fixes, these are applied BUT the actual application isn't working as expected (can't uninstall)

### Context Scenario

Local agent has Amazing Workload v1 installed already (Install mode works).
- Tried to uninstall: DOESN'T WORK!
- Pre-checks doesn't properly output results (has mocks i suspect - not ideal)
- Pre-checks doesn't properly output results (has mocks i suspect - not ideal)

Local agent logs:
```log
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 89.3025ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 89.4074ms - 201
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Step PreCheckProbe: PackageIndex=0, PackageId=6c81318e-b3bc-25a5-1770-423b9bbdff22
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 12.4228ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 12.6189ms - 201
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Pipeline diff computed: Added=0, Removed=0, Changed=0, Unchanged=2
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Pipeline starting: RunId=eda24ac2-7f29-4172-b68d-2d8fda247b0e, Workload=Amazing Workload, Mode=uninstall, TargetPackages=2
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Step UninstallPackage: PackageIndex=0, PackageId=58731731-098c-59f0-af67-790a62ad660e
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Step AcquireArtifactForUninstall: PackageIndex=0, PackageId=58731731-098c-59f0-af67-790a62ad660e, Url=http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request HEAD http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request HEAD http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 62.9389ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 63.1191ms - 200
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 0-2097151 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 42.4327ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 42.6009ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 0-2097151 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 1.7618323206682145%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 2097152-4194303 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 16.6181ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 16.7383ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 2097152-4194303 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 3.523664641336429%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 4194304-6291455 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.3894ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.481ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 4194304-6291455 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 5.285496962004643%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 6291456-8388607 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 5.1575ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 5.2931ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 6291456-8388607 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 7.047329282672858%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 8388608-10485759 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.0765ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.1593ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 8388608-10485759 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 8.809161603341073%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 10485760-12582911 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.8842ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.999ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 10485760-12582911 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 10.570993924009287%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 12582912-14680063 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.6366ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.7099ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 12582912-14680063 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 12.332826244677502%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 14680064-16777215 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.3774ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.469ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 14680064-16777215 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 14.094658565345716%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 16777216-18874367 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.6378ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.7114ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 16777216-18874367 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 15.85649088601393%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 18874368-20971519 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.5089ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.5935ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 18874368-20971519 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 17.618323206682145%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 20971520-23068671 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.6002ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.6767ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 20971520-23068671 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 19.38015552735036%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 23068672-25165823 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.0603ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.1492ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 23068672-25165823 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 21.141987848018573%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 25165824-27262975 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.9625ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.0438ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 25165824-27262975 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 22.90382016868679%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 27262976-29360127 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.7523ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.8351ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 27262976-29360127 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 24.665652489355004%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 29360128-31457279 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.4555ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.5269ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 29360128-31457279 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 26.427484810023216%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 31457280-33554431 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.989ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.0573ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 31457280-33554431 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 28.189317130691432%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 33554432-35651583 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.01ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.0885ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 33554432-35651583 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 29.951149451359647%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 35651584-37748735 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.85ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.9386ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 35651584-37748735 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 31.71298177202786%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 37748736-39845887 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.2905ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.3988ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 37748736-39845887 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 33.47481409269608%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 39845888-41943039 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.4802ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.5718ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 39845888-41943039 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 35.23664641336429%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 41943040-44040191 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.6998ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.7972ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 41943040-44040191 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 36.9984787340325%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 44040192-46137343 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.9688ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.0442ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 44040192-46137343 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 38.76031105470072%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 46137344-48234495 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.3091ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.3904ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 46137344-48234495 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 40.522143375368934%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 48234496-50331647 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.5567ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.6374ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 48234496-50331647 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 42.283975696037146%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 50331648-52428799 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.8232ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.9664ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 50331648-52428799 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 44.045808016705365%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 52428800-54525951 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.5411ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.6972ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 52428800-54525951 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 45.80764033737358%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 54525952-56623103 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.5532ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.6377ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 54525952-56623103 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 47.56947265804179%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 56623104-58720255 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 5.4917ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 5.5876ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 56623104-58720255 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 49.33130497871001%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 58720256-60817407 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.2521ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.3496ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 58720256-60817407 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 51.09313729937822%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 60817408-62914559 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.8823ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.9567ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 60817408-62914559 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 52.85496962004643%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 62914560-65011711 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.0245ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.1034ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 62914560-65011711 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 54.61680194071465%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 65011712-67108863 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.2005ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.2771ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 65011712-67108863 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 56.378634261382864%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 67108864-69206015 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.0333ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.1762ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 67108864-69206015 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 58.140466582051076%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 69206016-71303167 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.608ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.6959ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 69206016-71303167 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 59.902298902719295%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 71303168-73400319 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.8743ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.9893ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 71303168-73400319 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 61.66413122338751%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 73400320-75497471 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.5777ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.6684ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 73400320-75497471 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 63.42596354405572%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 75497472-77594623 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.8976ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.9685ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 75497472-77594623 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 65.18779586472394%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 77594624-79691775 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.3141ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.3879ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 77594624-79691775 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 66.94962818539216%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 79691776-81788927 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.8742ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.0194ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 79691776-81788927 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 68.71146050606036%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 81788928-83886079 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.5525ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.6255ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 81788928-83886079 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 70.47329282672858%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 83886080-85983231 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.441ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.53ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 83886080-85983231 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 72.2351251473968%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 85983232-88080383 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.7571ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.845ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 85983232-88080383 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 73.996957468065%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 88080384-90177535 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 5.1856ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 5.2935ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 88080384-90177535 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 75.75878978873322%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 90177536-92274687 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.7212ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.7964ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 90177536-92274687 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 77.52062210940144%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 92274688-94371839 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.1531ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.2802ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 92274688-94371839 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 79.28245443006965%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 94371840-96468991 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.1772ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.3375ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 94371840-96468991 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 81.04428675073787%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 96468992-98566143 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.7623ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.8821ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 96468992-98566143 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 82.80611907140609%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 98566144-100663295 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.187ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.2603ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 98566144-100663295 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 84.56795139207429%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 100663296-102760447 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.1309ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.2108ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 100663296-102760447 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 86.32978371274251%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 102760448-104857599 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.8797ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.9605ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 102760448-104857599 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 88.09161603341073%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 104857600-106954751 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.5176ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.6797ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 104857600-106954751 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 89.85344835407894%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 106954752-109051903 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.587ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.7306ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 106954752-109051903 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 91.61528067474715%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 109051904-111149055 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.8538ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.0519ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 109051904-111149055 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 93.37711299541537%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 111149056-113246207 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.1755ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.2474ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 111149056-113246207 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 95.13894531608358%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 113246208-115343359 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.2481ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.3571ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 113246208-115343359 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 96.9007776367518%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 115343360-117440511 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.4139ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.501ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 115343360-117440511 (2097152 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 98.66260995742002%
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloading chunk 117440512-119032439 for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.0799ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.1518ms - 206
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Downloaded chunk 117440512-119032439 (1591928 bytes) for http://localhost:5000/api/artifacts/58731731-098c-59f0-af67-790a62ad660e/download. Progress: 100%
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 5.868ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 5.9795ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 6.4261ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 6.5604ms - 201
fail: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Pipeline halted at UninstallPackage: PackageIndex=0, Error=exit_code_1
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.6837ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.7762ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request PATCH http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request PATCH http://localhost:5000/api/workload-runs/eda24ac2-7f29-4172-b68d-2d8fda247b0e?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 18.09ms - 204
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 18.1597ms - 204
info: DeploymentPoC.Agent.Services.AgentRuntimeService[0]
      Pipeline completed: RunId=eda24ac2-7f29-4172-b68d-2d8fda247b0e, Success=False
```

## Files Involved

- `apps/agent/backend/Pipeline/PipelineExecutor.cs` — main pipeline orchestration
- `apps/agent/backend/Steps/UninstallPackage.cs` — uninstall execution logic
- `apps/orchestrator/backend/Controllers/NodesController.cs` — pre-check endpoint (`ReconcileProbeResults`)
- `apps/orchestrator/backend/Services/WorkloadRunDispatcher.cs` — payload assembly
- `apps/orchestrator/web/src/pages/WorkloadRuns.tsx` — Run Creator UI
- `shared/contracts/Runtime/RunPayloads/InstallAdapterConfig.cs` — adapter config schema

NOTE: YOU SHOULD verify and do an in-depth-review on the other files that you may suspect is affected.

### Other files

- Makefile -> to see commands
- dist/ -> contains everything (sqlite database, artifact store, workloads, manifest JSON for the artifacts/packages)

## Goal

Design and implement a complete uninstall pipeline that:

1. **Never downloads artifacts for uninstall** — instead relies on `UninstallCommand` + `UninstallArgs` or system-level detection.
2. **Auto-runs pre-checks** — when `/workload-runs` page loads OR when Run Creator opens, pre-check all online nodes in background for the selected workload.
3. **Shows pre-check results inline** — expand UI width and display per-node pre-check status with expandable detail (not just hover tooltip).
4. **Handles missing `UninstallCommand` gracefully** — either:
   - Fail fast with clear error: "Package 'X' has no uninstall command configured"
   - Or auto-detect uninstaller from registry/Add Remove Programs using the package name

## Review Request

Please review the current architecture and provide:

1. **Architectural assessment** — Is downloading artifacts for uninstall fundamentally wrong? What should the uninstall contract look like?
2. **Uninstall strategy options** — How should the agent uninstall packages without the installer artifact? (registry lookup, dedicated uninstall command, etc.)
3. **Pre-check integration design** — Should pre-checks be:
   - Triggered automatically on modal open?
   - Cached per-node and refreshed periodically?
   - Run for ALL modes (install/update/uninstall) since they detect current state?
4. **UI/UX recommendations** — How wide should the modal be? How to display per-node pre-check details without clutter?
5. **Implementation plan** — Step-by-step changes needed across backend, agent, and frontend.

## What I want (BIG PICTURE GOAL):

**For the Uninstall mode to work properly**

-In the /workload-runs Run Creator modal UI: 
   - Uninstall tab -> Properly select a workload and specific version -> THEN nodes that has that specific workload and version will appear in the Target Nodes (agents) list -> Select a specific node -> run uninstall -> packages (based on the workload definition) on the selected agent/node WILL BE uninstalled completely. 

# Main Output

A comprehensive report (with actual code examples for fixes if possible) that details the ISSUES THAT NEEDED TO BE FIXED with the actual fix itself (with code is better).
- Put context as much as needed for the other agents to pick the context and work on the implementation

NOTE: This report will serve as the SPEC and implementation plan for this issue for the core functionality, the "Uninstall" mode feature with proper pre-checks.