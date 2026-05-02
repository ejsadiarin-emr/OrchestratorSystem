I need to debug the DeploymentPoC agent on a VM. The bugfix branch bugfix-six-bugs has 6 fixes already applied. See the handoff report at docs/reports/handoff-agent-vm-debugging.md for full context. I need to deploy orchestrator + agent to separate VMs / test MSI installation on clean machine / verify binary aliases / etc.... 

I just ran a workload for testing on an Agent VM (I ran Dev Stack v1 on AGENT1 node - note that in this node there is no nodejs and python installed so everything is fresh), so then it was "Running" indefinitely (indicated by the UI). The Diff engine worked perfectly (Added=2 ...), there was a "download" attempt or something (i can't verify WHERE this is downloaded if ever), but there is no sign of installation whatsoever (verify this).

These are the remote node agent (VM) or AGENT1 node logs:
PS C:\Users\ej\Documents> .\DeploymentPoC.Agent.exe --enroll enroll-d85d1c5c32ad4e339f0cc3cbb90d9737 --orchestrator-url http://192.168.174.1:5000
Enrollment successful. NodeId=ebb13645-93a6-4fb0-84fb-9624122a614d
warn: Microsoft.AspNetCore.StaticFiles.StaticFileMiddleware[16]
      The WebRootPath was not found: C:\Users\ej\Documents\wwwroot. Static files may be unavailable.
warn: Microsoft.AspNetCore.StaticFiles.StaticFileMiddleware[16]
      The WebRootPath was not found: C:\Users\ej\Documents\wwwroot. Static files may be unavailable.
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://localhost:5001
info: Microsoft.Hosting.Lifetime[0]
      Application started. Press Ctrl+C to shut down.
info: Microsoft.Hosting.Lifetime[0]
      Hosting environment: Production
info: Microsoft.Hosting.Lifetime[0]
      Content root path: C:\Users\ej\Documents\
info: DeploymentPoC.Agent.Services.AgentRuntimeService[0]
      Agent polling loop starting. NodeId=ebb13645-93a6-4fb0-84fb-9624122a614d, Orchestrator=http://192.168.174.1:5000
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 28.9223ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 38.9709ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 24.315ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 25.2346ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 13.108ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 13.5183ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 22.7984ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 23.3683ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request PATCH http://192.168.174.1:5000/api/workload-runs/95f4b3cb-01d7-44d9-977e-50d6834a269d
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request PATCH http://192.168.174.1:5000/api/workload-runs/95f4b3cb-01d7-44d9-977e-50d6834a269d
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 16.3685ms - 204
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 16.5868ms - 204
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Step PreCheckProbe: PackageIndex=0, PackageId=e4311b64-24c3-3a39-2390-dd1bdd3491b5
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://192.168.174.1:5000/api/workload-runs/95f4b3cb-01d7-44d9-977e-50d6834a269d/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://192.168.174.1:5000/api/workload-runs/95f4b3cb-01d7-44d9-977e-50d6834a269d/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 12.7897ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 12.9891ms - 201
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Step PreCheckProbe: PackageIndex=0, PackageId=6c81318e-b3bc-25a5-1770-423b9bbdff22
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://192.168.174.1:5000/api/workload-runs/95f4b3cb-01d7-44d9-977e-50d6834a269d/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://192.168.174.1:5000/api/workload-runs/95f4b3cb-01d7-44d9-977e-50d6834a269d/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 12.9406ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 13.19ms - 201
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Pipeline diff computed: Added=2, Removed=0, Changed=0, Unchanged=0
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Pipeline starting: RunId=95f4b3cb-01d7-44d9-977e-50d6834a269d, Workload=Dev Tools Stack, Mode=install, TargetPackages=2
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Step AcquireArtifact: PackageIndex=0, PackageId=e4311b64-24c3-3a39-2390-dd1bdd3491b5, Url=http://192.168.174.1:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request HEAD http://192.168.174.1:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request HEAD http://192.168.174.1:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 23.5975ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 23.8703ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://192.168.174.1:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://192.168.174.1:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 17.4064ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 17.729ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://192.168.174.1:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101] 

For more context, I ran the same workload run (installationm Dev Stack v1) on a local agent (has nodejs installed prior but newer version v24, no python installed - it "Failed" but it ACTUALLY SUCCEEDED to install python, for nodejs since it was a newer version it was marked as "fail" since the nodejs package that the workload referenced is an older one - msi exit code doesn't allow downgrading versions on silent installs). This is the actual logs of this local agent:
```
updated agent logs (indicating the fail):
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.1345ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.172ms - 201
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Step PostInstallVerify: PackageIndex=0, PackageId=6c81318e-b3bc-25a5-1770-423b9bbdff22, DetectionType=version_manifest
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 4.7174ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 4.7545ms - 201
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Step AcquireArtifact: PackageIndex=0, PackageId=e4311b64-24c3-3a39-2390-dd1bdd3491b5, Url=http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request HEAD http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request HEAD http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 1.5979ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 1.6243ms - 200
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.2358ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.2948ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 1.6878ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 1.7403ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 1.5207ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 1.5588ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/artifacts/e4311b64-24c3-3a39-2390-dd1bdd3491b5/download
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 1.9659ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 2.0187ms - 206
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 3.0063ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.0525ms - 201
info: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Step InstallOrUpgrade: PackageIndex=0, PackageId=e4311b64-24c3-3a39-2390-dd1bdd3491b5, AdapterType=msi
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request POST http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request POST http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee/timeline?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 2.9783ms - 201
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 3.0212ms - 201
fail: DeploymentPoC.Agent.Pipeline.PipelineExecutor[0]
      Pipeline halted at InstallOrUpgrade: PackageIndex=0, Error=exit_code_1603
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request PATCH http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request PATCH http://localhost:5000/api/workload-runs/3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 11.6267ms - 204
info: System.Net.Http.HttpClient.Default.LogicalHandler[101]
      End processing HTTP request after 11.6691ms - 204
info: DeploymentPoC.Agent.Services.AgentRuntimeService[0]
      Pipeline completed: RunId=3c3ad6bb-4d21-4f05-b886-8d18ef72f5ee, Success=False
info: System.Net.Http.HttpClient.Default.LogicalHandler[100]
      Start processing HTTP request GET http://localhost:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[100]
      Sending HTTP request GET http://localhost:5000/api/workload-runs/pending?*
info: System.Net.Http.HttpClient.Default.ClientHandler[101]
      Received HTTP response headers after 6.4638ms - 200 
```

Also these are the logs for orchestrator, and the run failed (indicated in the UI):
```
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadRunRecordId", "w"."CancelReason", "w"."CompletedAtUtc", "w"."CreatedAtUtc", "w"."ForceInstall", "w"."IdempotencyKey", "w"."IdempotencyRequestHash", "w"."Mode", "w"."NodeDisplayName", "w"."NodeId", "w"."RevisionId", "w"."RevisionSnapshotJson", "w"."RiskLevel", "w"."RunId", "w"."State", "w"."UpdatedAtUtc", "w"."WorkloadId", "w0"."WorkloadId", "w0"."CreatedAtUtc", "w0"."Description", "w0"."Name", "w0"."PublishedRevisionId", "w0"."UpdatedAtUtc", "w1"."RevisionId", "w1"."CreatedAtUtc", "w1"."IsPublished", "w1"."Version", "w1"."WorkloadId", "w2"."WorkloadPackageId", "w2"."PackageId", "w2"."PackageIndex", "w2"."RevisionId"
      FROM "WorkloadRuns" AS "w"
      INNER JOIN "WorkloadDefinitions" AS "w0" ON "w"."WorkloadId" = "w0"."WorkloadId"
      INNER JOIN "WorkloadRevisions" AS "w1" ON "w"."RevisionId" = "w1"."RevisionId"
      LEFT JOIN "WorkloadPackages" AS "w2" ON "w1"."RevisionId" = "w2"."RevisionId"
      WHERE "w"."NodeId" = @__agentId_0 AND "w"."State" = 'Queued'
      ORDER BY "w"."WorkloadRunRecordId", "w0"."WorkloadId", "w1"."RevisionId"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__packageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "p0"."value"
          FROM json_each(@__packageIds_0) AS "p0"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid), @__workloadIds_1='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeWorkloadStateId", "n"."CurrentRevisionId", "n"."NodeId", "n"."PackageStatesJson", "n"."UpdatedAtUtc", "n"."WorkloadId"
      FROM "NodeWorkloadStates" AS "n"
      WHERE "n"."NodeId" = @__agentId_0 AND "n"."WorkloadId" IN (
          SELECT "w"."value"
          FROM json_each(@__workloadIds_1) AS "w"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentRevisionIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadPackageId", "w"."PackageId", "w"."PackageIndex", "w"."RevisionId"
      FROM "WorkloadPackages" AS "w"
      WHERE "w"."RevisionId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentRevisionIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentPackageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentPackageIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__p_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeId", "n"."AgentId", "n"."AgentVersion", "n"."Description", "n"."DisplayName", "n"."FirstConnectedUtc", "n"."Hostname", "n"."IpAddress", "n"."LastSeenUtc", "n"."OsVersion", "n"."Status"
      FROM "Nodes" AS "n"
      WHERE "n"."NodeId" = @__p_0
      LIMIT 1
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@p1='?' (DbType = Guid), @p0='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      UPDATE "Nodes" SET "LastSeenUtc" = @p0
      WHERE "NodeId" = @p1
      RETURNING 1;
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__cutoff_0='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      SELECT COUNT(*)
      FROM "Nodes" AS "n"
      WHERE "n"."LastSeenUtc" < @__cutoff_0
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadRunRecordId", "w"."CancelReason", "w"."CompletedAtUtc", "w"."CreatedAtUtc", "w"."ForceInstall", "w"."IdempotencyKey", "w"."IdempotencyRequestHash", "w"."Mode", "w"."NodeDisplayName", "w"."NodeId", "w"."RevisionId", "w"."RevisionSnapshotJson", "w"."RiskLevel", "w"."RunId", "w"."State", "w"."UpdatedAtUtc", "w"."WorkloadId"
      FROM "WorkloadRuns" AS "w"
      ORDER BY "w"."CreatedAtUtc" DESC
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__workloadIds_0='?' (Size = 40)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadId", "w"."CreatedAtUtc", "w"."Description", "w"."Name", "w"."PublishedRevisionId", "w"."UpdatedAtUtc"
      FROM "WorkloadDefinitions" AS "w"
      WHERE "w"."WorkloadId" IN (
          SELECT "w0"."value"
          FROM json_each(@__workloadIds_0) AS "w0"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__revisionIds_0='?' (Size = 40)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."RevisionId", "w"."CreatedAtUtc", "w"."IsPublished", "w"."Version", "w"."WorkloadId"
      FROM "WorkloadRevisions" AS "w"
      WHERE "w"."RevisionId" IN (
          SELECT "r"."value"
          FROM json_each(@__revisionIds_0) AS "r"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__cutoff_0='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeId" AS "Id", "n"."Hostname", "n"."DisplayName", "n"."IpAddress", CASE
          WHEN "n"."LastSeenUtc" >= @__cutoff_0 THEN 'online'
          ELSE 'offline'
      END AS "Status", "n"."LastSeenUtc" AS "LastSeenAt", "n"."FirstConnectedUtc" AS "FirstConnectedAt", "n"."Description", "n"."OsVersion", "n"."AgentVersion"
      FROM "Nodes" AS "n"
      ORDER BY "n"."Hostname"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadId", "w"."Name", "w"."Description", "w"."PublishedRevisionId", "w"."CreatedAtUtc", "w"."UpdatedAtUtc", (
          SELECT COUNT(*)
          FROM "WorkloadRevisions" AS "w0"
          WHERE "w"."WorkloadId" = "w0"."WorkloadId"), "w3"."RevisionId", "w3"."Version", "w3"."IsPublished", "w3"."c"
      FROM "WorkloadDefinitions" AS "w"
      LEFT JOIN (
          SELECT "w2"."RevisionId", "w2"."Version", "w2"."IsPublished", "w2"."c", "w2"."WorkloadId"
          FROM (
              SELECT "w1"."RevisionId", "w1"."Version", "w1"."IsPublished", 1 AS "c", "w1"."WorkloadId", ROW_NUMBER() OVER(PARTITION BY "w1"."WorkloadId" ORDER BY "w1"."IsPublished" DESC, "w1"."CreatedAtUtc" DESC) AS "row"
              FROM "WorkloadRevisions" AS "w1"
          ) AS "w2"
          WHERE "w2"."row" <= 1
      ) AS "w3" ON "w"."WorkloadId" = "w3"."WorkloadId"
      ORDER BY "w"."Name"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadRunRecordId", "w"."CancelReason", "w"."CompletedAtUtc", "w"."CreatedAtUtc", "w"."ForceInstall", "w"."IdempotencyKey", "w"."IdempotencyRequestHash", "w"."Mode", "w"."NodeDisplayName", "w"."NodeId", "w"."RevisionId", "w"."RevisionSnapshotJson", "w"."RiskLevel", "w"."RunId", "w"."State", "w"."UpdatedAtUtc", "w"."WorkloadId"
      FROM "WorkloadRuns" AS "w"
      ORDER BY "w"."CreatedAtUtc" DESC
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__workloadIds_0='?' (Size = 40)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadId", "w"."CreatedAtUtc", "w"."Description", "w"."Name", "w"."PublishedRevisionId", "w"."UpdatedAtUtc"
      FROM "WorkloadDefinitions" AS "w"
      WHERE "w"."WorkloadId" IN (
          SELECT "w0"."value"
          FROM json_each(@__workloadIds_0) AS "w0"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__revisionIds_0='?' (Size = 40)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."RevisionId", "w"."CreatedAtUtc", "w"."IsPublished", "w"."Version", "w"."WorkloadId"
      FROM "WorkloadRevisions" AS "w"
      WHERE "w"."RevisionId" IN (
          SELECT "r"."value"
          FROM json_each(@__revisionIds_0) AS "r"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadId", "w"."Name", "w"."Description", "w"."PublishedRevisionId", "w"."CreatedAtUtc", "w"."UpdatedAtUtc", (
          SELECT COUNT(*)
          FROM "WorkloadRevisions" AS "w0"
          WHERE "w"."WorkloadId" = "w0"."WorkloadId"), "w3"."RevisionId", "w3"."Version", "w3"."IsPublished", "w3"."c"
      FROM "WorkloadDefinitions" AS "w"
      LEFT JOIN (
          SELECT "w2"."RevisionId", "w2"."Version", "w2"."IsPublished", "w2"."c", "w2"."WorkloadId"
          FROM (
              SELECT "w1"."RevisionId", "w1"."Version", "w1"."IsPublished", 1 AS "c", "w1"."WorkloadId", ROW_NUMBER() OVER(PARTITION BY "w1"."WorkloadId" ORDER BY "w1"."IsPublished" DESC, "w1"."CreatedAtUtc" DESC) AS "row"
              FROM "WorkloadRevisions" AS "w1"
          ) AS "w2"
          WHERE "w2"."row" <= 1
      ) AS "w3" ON "w"."WorkloadId" = "w3"."WorkloadId"
      ORDER BY "w"."Name"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) [Parameters=[@__cutoff_0='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeId" AS "Id", "n"."Hostname", "n"."DisplayName", "n"."IpAddress", CASE
          WHEN "n"."LastSeenUtc" >= @__cutoff_0 THEN 'online'
          ELSE 'offline'
      END AS "Status", "n"."LastSeenUtc" AS "LastSeenAt", "n"."FirstConnectedUtc" AS "FirstConnectedAt", "n"."Description", "n"."OsVersion", "n"."AgentVersion"
      FROM "Nodes" AS "n"
      ORDER BY "n"."Hostname"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadRunRecordId", "w"."CancelReason", "w"."CompletedAtUtc", "w"."CreatedAtUtc", "w"."ForceInstall", "w"."IdempotencyKey", "w"."IdempotencyRequestHash", "w"."Mode", "w"."NodeDisplayName", "w"."NodeId", "w"."RevisionId", "w"."RevisionSnapshotJson", "w"."RiskLevel", "w"."RunId", "w"."State", "w"."UpdatedAtUtc", "w"."WorkloadId", "w0"."WorkloadId", "w0"."CreatedAtUtc", "w0"."Description", "w0"."Name", "w0"."PublishedRevisionId", "w0"."UpdatedAtUtc", "w1"."RevisionId", "w1"."CreatedAtUtc", "w1"."IsPublished", "w1"."Version", "w1"."WorkloadId", "w2"."WorkloadPackageId", "w2"."PackageId", "w2"."PackageIndex", "w2"."RevisionId"
      FROM "WorkloadRuns" AS "w"
      INNER JOIN "WorkloadDefinitions" AS "w0" ON "w"."WorkloadId" = "w0"."WorkloadId"
      INNER JOIN "WorkloadRevisions" AS "w1" ON "w"."RevisionId" = "w1"."RevisionId"
      LEFT JOIN "WorkloadPackages" AS "w2" ON "w1"."RevisionId" = "w2"."RevisionId"
      WHERE "w"."NodeId" = @__agentId_0 AND "w"."State" = 'Queued'
      ORDER BY "w"."WorkloadRunRecordId", "w0"."WorkloadId", "w1"."RevisionId"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__packageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "p0"."value"
          FROM json_each(@__packageIds_0) AS "p0"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid), @__workloadIds_1='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeWorkloadStateId", "n"."CurrentRevisionId", "n"."NodeId", "n"."PackageStatesJson", "n"."UpdatedAtUtc", "n"."WorkloadId"
      FROM "NodeWorkloadStates" AS "n"
      WHERE "n"."NodeId" = @__agentId_0 AND "n"."WorkloadId" IN (
          SELECT "w"."value"
          FROM json_each(@__workloadIds_1) AS "w"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentRevisionIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadPackageId", "w"."PackageId", "w"."PackageIndex", "w"."RevisionId"
      FROM "WorkloadPackages" AS "w"
      WHERE "w"."RevisionId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentRevisionIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentPackageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentPackageIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__p_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeId", "n"."AgentId", "n"."AgentVersion", "n"."Description", "n"."DisplayName", "n"."FirstConnectedUtc", "n"."Hostname", "n"."IpAddress", "n"."LastSeenUtc", "n"."OsVersion", "n"."Status"
      FROM "Nodes" AS "n"
      WHERE "n"."NodeId" = @__p_0
      LIMIT 1
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@p1='?' (DbType = Guid), @p0='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      UPDATE "Nodes" SET "LastSeenUtc" = @p0
      WHERE "NodeId" = @p1
      RETURNING 1;
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadRunRecordId", "w"."CancelReason", "w"."CompletedAtUtc", "w"."CreatedAtUtc", "w"."ForceInstall", "w"."IdempotencyKey", "w"."IdempotencyRequestHash", "w"."Mode", "w"."NodeDisplayName", "w"."NodeId", "w"."RevisionId", "w"."RevisionSnapshotJson", "w"."RiskLevel", "w"."RunId", "w"."State", "w"."UpdatedAtUtc", "w"."WorkloadId", "w0"."WorkloadId", "w0"."CreatedAtUtc", "w0"."Description", "w0"."Name", "w0"."PublishedRevisionId", "w0"."UpdatedAtUtc", "w1"."RevisionId", "w1"."CreatedAtUtc", "w1"."IsPublished", "w1"."Version", "w1"."WorkloadId", "w2"."WorkloadPackageId", "w2"."PackageId", "w2"."PackageIndex", "w2"."RevisionId"
      FROM "WorkloadRuns" AS "w"
      INNER JOIN "WorkloadDefinitions" AS "w0" ON "w"."WorkloadId" = "w0"."WorkloadId"
      INNER JOIN "WorkloadRevisions" AS "w1" ON "w"."RevisionId" = "w1"."RevisionId"
      LEFT JOIN "WorkloadPackages" AS "w2" ON "w1"."RevisionId" = "w2"."RevisionId"
      WHERE "w"."NodeId" = @__agentId_0 AND "w"."State" = 'Queued'
      ORDER BY "w"."WorkloadRunRecordId", "w0"."WorkloadId", "w1"."RevisionId"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__packageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "p0"."value"
          FROM json_each(@__packageIds_0) AS "p0"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid), @__workloadIds_1='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeWorkloadStateId", "n"."CurrentRevisionId", "n"."NodeId", "n"."PackageStatesJson", "n"."UpdatedAtUtc", "n"."WorkloadId"
      FROM "NodeWorkloadStates" AS "n"
      WHERE "n"."NodeId" = @__agentId_0 AND "n"."WorkloadId" IN (
          SELECT "w"."value"
          FROM json_each(@__workloadIds_1) AS "w"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentRevisionIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadPackageId", "w"."PackageId", "w"."PackageIndex", "w"."RevisionId"
      FROM "WorkloadPackages" AS "w"
      WHERE "w"."RevisionId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentRevisionIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentPackageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentPackageIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__p_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeId", "n"."AgentId", "n"."AgentVersion", "n"."Description", "n"."DisplayName", "n"."FirstConnectedUtc", "n"."Hostname", "n"."IpAddress", "n"."LastSeenUtc", "n"."OsVersion", "n"."Status"
      FROM "Nodes" AS "n"
      WHERE "n"."NodeId" = @__p_0
      LIMIT 1
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@p1='?' (DbType = Guid), @p0='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      UPDATE "Nodes" SET "LastSeenUtc" = @p0
      WHERE "NodeId" = @p1
      RETURNING 1;
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadRunRecordId", "w"."CancelReason", "w"."CompletedAtUtc", "w"."CreatedAtUtc", "w"."ForceInstall", "w"."IdempotencyKey", "w"."IdempotencyRequestHash", "w"."Mode", "w"."NodeDisplayName", "w"."NodeId", "w"."RevisionId", "w"."RevisionSnapshotJson", "w"."RiskLevel", "w"."RunId", "w"."State", "w"."UpdatedAtUtc", "w"."WorkloadId", "w0"."WorkloadId", "w0"."CreatedAtUtc", "w0"."Description", "w0"."Name", "w0"."PublishedRevisionId", "w0"."UpdatedAtUtc", "w1"."RevisionId", "w1"."CreatedAtUtc", "w1"."IsPublished", "w1"."Version", "w1"."WorkloadId", "w2"."WorkloadPackageId", "w2"."PackageId", "w2"."PackageIndex", "w2"."RevisionId"
      FROM "WorkloadRuns" AS "w"
      INNER JOIN "WorkloadDefinitions" AS "w0" ON "w"."WorkloadId" = "w0"."WorkloadId"
      INNER JOIN "WorkloadRevisions" AS "w1" ON "w"."RevisionId" = "w1"."RevisionId"
      LEFT JOIN "WorkloadPackages" AS "w2" ON "w1"."RevisionId" = "w2"."RevisionId"
      WHERE "w"."NodeId" = @__agentId_0 AND "w"."State" = 'Queued'
      ORDER BY "w"."WorkloadRunRecordId", "w0"."WorkloadId", "w1"."RevisionId"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__packageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "p0"."value"
          FROM json_each(@__packageIds_0) AS "p0"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid), @__workloadIds_1='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeWorkloadStateId", "n"."CurrentRevisionId", "n"."NodeId", "n"."PackageStatesJson", "n"."UpdatedAtUtc", "n"."WorkloadId"
      FROM "NodeWorkloadStates" AS "n"
      WHERE "n"."NodeId" = @__agentId_0 AND "n"."WorkloadId" IN (
          SELECT "w"."value"
          FROM json_each(@__workloadIds_1) AS "w"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentRevisionIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadPackageId", "w"."PackageId", "w"."PackageIndex", "w"."RevisionId"
      FROM "WorkloadPackages" AS "w"
      WHERE "w"."RevisionId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentRevisionIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentPackageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentPackageIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__p_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeId", "n"."AgentId", "n"."AgentVersion", "n"."Description", "n"."DisplayName", "n"."FirstConnectedUtc", "n"."Hostname", "n"."IpAddress", "n"."LastSeenUtc", "n"."OsVersion", "n"."Status"
      FROM "Nodes" AS "n"
      WHERE "n"."NodeId" = @__p_0
      LIMIT 1
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@p1='?' (DbType = Guid), @p0='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      UPDATE "Nodes" SET "LastSeenUtc" = @p0
      WHERE "NodeId" = @p1
      RETURNING 1;
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__cutoff_0='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      SELECT COUNT(*)
      FROM "Nodes" AS "n"
      WHERE "n"."LastSeenUtc" < @__cutoff_0
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadRunRecordId", "w"."CancelReason", "w"."CompletedAtUtc", "w"."CreatedAtUtc", "w"."ForceInstall", "w"."IdempotencyKey", "w"."IdempotencyRequestHash", "w"."Mode", "w"."NodeDisplayName", "w"."NodeId", "w"."RevisionId", "w"."RevisionSnapshotJson", "w"."RiskLevel", "w"."RunId", "w"."State", "w"."UpdatedAtUtc", "w"."WorkloadId", "w0"."WorkloadId", "w0"."CreatedAtUtc", "w0"."Description", "w0"."Name", "w0"."PublishedRevisionId", "w0"."UpdatedAtUtc", "w1"."RevisionId", "w1"."CreatedAtUtc", "w1"."IsPublished", "w1"."Version", "w1"."WorkloadId", "w2"."WorkloadPackageId", "w2"."PackageId", "w2"."PackageIndex", "w2"."RevisionId"
      FROM "WorkloadRuns" AS "w"
      INNER JOIN "WorkloadDefinitions" AS "w0" ON "w"."WorkloadId" = "w0"."WorkloadId"
      INNER JOIN "WorkloadRevisions" AS "w1" ON "w"."RevisionId" = "w1"."RevisionId"
      LEFT JOIN "WorkloadPackages" AS "w2" ON "w1"."RevisionId" = "w2"."RevisionId"
      WHERE "w"."NodeId" = @__agentId_0 AND "w"."State" = 'Queued'
      ORDER BY "w"."WorkloadRunRecordId", "w0"."WorkloadId", "w1"."RevisionId"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__packageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "p0"."value"
          FROM json_each(@__packageIds_0) AS "p0"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__agentId_0='?' (DbType = Guid), @__workloadIds_1='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeWorkloadStateId", "n"."CurrentRevisionId", "n"."NodeId", "n"."PackageStatesJson", "n"."UpdatedAtUtc", "n"."WorkloadId"
      FROM "NodeWorkloadStates" AS "n"
      WHERE "n"."NodeId" = @__agentId_0 AND "n"."WorkloadId" IN (
          SELECT "w"."value"
          FROM json_each(@__workloadIds_1) AS "w"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentRevisionIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadPackageId", "w"."PackageId", "w"."PackageIndex", "w"."RevisionId"
      FROM "WorkloadPackages" AS "w"
      WHERE "w"."RevisionId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentRevisionIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__currentPackageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "c"."value"
          FROM json_each(@__currentPackageIds_0) AS "c"
      )
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__p_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "n"."NodeId", "n"."AgentId", "n"."AgentVersion", "n"."Description", "n"."DisplayName", "n"."FirstConnectedUtc", "n"."Hostname", "n"."IpAddress", "n"."LastSeenUtc", "n"."OsVersion", "n"."Status"
      FROM "Nodes" AS "n"
      WHERE "n"."NodeId" = @__p_0
      LIMIT 1
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@p1='?' (DbType = Guid), @p0='?' (DbType = DateTime)], CommandType='Text', CommandTimeout='30']
      UPDATE "Nodes" SET "LastSeenUtc" = @p0
      WHERE "NodeId" = @p1
      RETURNING 1;
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (1ms) [Parameters=[@__agentId_0='?' (DbType = Guid)], CommandType='Text', CommandTimeout='30']
      SELECT "w"."WorkloadRunRecordId", "w"."CancelReason", "w"."CompletedAtUtc", "w"."CreatedAtUtc", "w"."ForceInstall", "w"."IdempotencyKey", "w"."IdempotencyRequestHash", "w"."Mode", "w"."NodeDisplayName", "w"."NodeId", "w"."RevisionId", "w"."RevisionSnapshotJson", "w"."RiskLevel", "w"."RunId", "w"."State", "w"."UpdatedAtUtc", "w"."WorkloadId", "w0"."WorkloadId", "w0"."CreatedAtUtc", "w0"."Description", "w0"."Name", "w0"."PublishedRevisionId", "w0"."UpdatedAtUtc", "w1"."RevisionId", "w1"."CreatedAtUtc", "w1"."IsPublished", "w1"."Version", "w1"."WorkloadId", "w2"."WorkloadPackageId", "w2"."PackageId", "w2"."PackageIndex", "w2"."RevisionId"
      FROM "WorkloadRuns" AS "w"
      INNER JOIN "WorkloadDefinitions" AS "w0" ON "w"."WorkloadId" = "w0"."WorkloadId"
      INNER JOIN "WorkloadRevisions" AS "w1" ON "w"."RevisionId" = "w1"."RevisionId"
      LEFT JOIN "WorkloadPackages" AS "w2" ON "w1"."RevisionId" = "w2"."RevisionId"
      WHERE "w"."NodeId" = @__agentId_0 AND "w"."State" = 'Queued'
      ORDER BY "w"."WorkloadRunRecordId", "w0"."WorkloadId", "w1"."RevisionId"
info: Microsoft.EntityFrameworkCore.Database.Command[20101]
      Executed DbCommand (0ms) [Parameters=[@__packageIds_0='?' (Size = 2)], CommandType='Text', CommandTimeout='30']
      SELECT "p"."PackageId", "p"."CreatedAtUtc", "p"."DetectionConfigJson", "p"."ExpectedExitCodesJson", "p"."InstallArgs", "p"."InstallType", "p"."Name", "p"."SourcePath", "p"."TimeoutSeconds", "p"."UninstallArgs", "p"."Version"
      FROM "Packages" AS "p"
      WHERE "p"."PackageId" IN (
          SELECT "p0"."value"
          FROM json_each(@__packageIds_0) AS "p0"
      ) 
```

---

We need to run an in-depth review and deep review or audit or investigation on this. Use subagents to parallelize the reviews as needed. You may ask or consult me for questions and for more context (if you need) for shared understanding.

IMPORTANT: investigate on the bugfix-six-bugs repository (you may create a git worktree OR investigate directly on the .worktrees/bugfix-worktree/ directory from root).

-------------------


You are implementing bugfixes for a DeploymentPoC agent pipeline that gets stuck in "Running" state on VM deployments.

## Context
The DeploymentPoC system has an orchestrator (ASP.NET host) and an agent (BackgroundService that polls for work). A Dev Stack v1 workload (nodejs + python) was deployed to a clean VM (AGENT1). The pipeline correctly computed a diff (Added=2) and started downloading, but the orchestrator run stayed "Running" forever — the agent never sent a terminal status (Completed/Failed). On a local dev agent, the pipeline ran to completion but nodejs failed with exit_code_1603 (MSI install insufficient privileges — can't downgrade).
## Implementation Plan
Full plan with code-level fix proposals: `docs/bugfix-implementation-plan-2026-04-28.md`
Bugfix branch: `bugfix-six-bugs` (worktree: `.worktrees/bugfix-worktree/`)
## Bugs to Fix (in priority order)
1. **RUN-001 (CRITICAL)** — `AgentRuntimeService.cs`: Fire-and-forget `Task.Run` uses `stoppingToken`-derived `ct` for the final PATCH calls. When the token cancels, the PATCH silently fails, leaving the run stuck "Running" forever. Fix: Use `CancellationToken.None` for terminal status PATCH calls.
2. **RUN-002 (HIGH)** — `InstallOrUpgrade.cs`: MSI install uses `UseShellExecute=false`, so `Process.Start` succeeds but msiexec exits 1603. The Win32Exception(740) catch is unreachable. Fix: Detect exit code 1603 and retry with `UseShellExecute=true, Verb="runas"`. Agents run with admin privileges.
3. **DL-001 (HIGH)** — `AcquireArtifact.cs`: No validation that `bytesWritten == expectedLength` after chunked download. Incomplete downloads silently reported as success. Fix: Add length validation after the download loop.
4. **DL-002 (MEDIUM)** — `AcquireArtifact.cs`: `IsValidContentRange` requires Content-Range total length, rejecting valid 206 responses from servers that omit it (use `*`). Fix: Allow null total length, validate range boundaries only.
5. **PIPE-001 (MEDIUM)** — `PipelineExecutor.cs`: `CancellationTokenSource.CreateLinkedTokenSource(ct).Token` leaks 3 disposable objects per pipeline execution. Fix: Use `ct` directly (no per-step timeout needed).
6. **DL-003 (MEDIUM)** — `AcquireArtifact.cs`: 200 OK fallback discards previously downloaded chunks with no length validation. Fix: Add Content-Length validation to the 200 OK path.
## Key Files
| File | Bugs |
|---|---|
| `apps/agent/backend/Services/AgentRuntimeService.cs` | RUN-001 |
| `apps/agent/backend/Steps/InstallOrUpgrade.cs` | RUN-002 |
| `apps/agent/backend/Steps/AcquireArtifact.cs` | DL-001, DL-002, DL-003 |
| `apps/agent/backend/Services/PipelineExecutor.cs` | PIPE-001 |
## Assumptions
- Agents run with admin privileges (LOCAL_SYSTEM or admin service account)
- The bugfix-six-bugs branch already has 6 previously-fixed bugs in the worktree
## Build & Verify
After implementing, run `dotnet build` from the worktree root. Add unit tests where feasible. See the plan's Verification section for the full test matrix. 

---
Implement the plan @docs\bugfix-implementation-plan-2026-04-28.md based on the context given above.

Use subagents to parallelize work whenever possible and skills like tdd when needed. You can consult me for questions for shared understanding.

IMPORTANT: create a git worktree for this (reference main branch, since bugfix-six-bugs branch have been merged to main - verify this) inside .worktrees/ directory.














