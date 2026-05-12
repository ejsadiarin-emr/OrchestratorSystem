using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class BaselineCreateInstallerSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EnrollmentTokens",
                columns: table => new
                {
                    TokenId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    IssuedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RequestedBy = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    OrchestratorUrl = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    SingleUse = table.Column<bool>(type: "INTEGER", nullable: false),
                    Used = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConsumedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ConsumedByNodeId = table.Column<Guid>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentTokens", x => x.TokenId);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    ReasonCode = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ManifestPackageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    ManifestTargetVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    TargetNodeIdsCsv = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IdempotencyRequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.JobId);
                    table.CheckConstraint("CK_Jobs_Mode", "\"Mode\" IN ('install','upgrade','rollback','modify','cancel')");
                    table.CheckConstraint("CK_Jobs_State", "\"State\" IN ('Queued','Running','Completed','Failed','Cancelled')");
                });

            migrationBuilder.CreateTable(
                name: "Nodes",
                columns: table => new
                {
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Hostname = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    AgentVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    LastSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FirstConnectedUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OsVersion = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Nodes", x => x.NodeId);
                    table.CheckConstraint("CK_Nodes_Status", "\"Status\" IN ('Offline','Online')");
                });

            migrationBuilder.CreateTable(
                name: "Packages",
                columns: table => new
                {
                    PackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SourcePath = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    InstallType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    InstallArgs = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    UninstallArgs = table.Column<string>(type: "TEXT", nullable: false),
                    UninstallCommand = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    UpgradeBehavior = table.Column<string>(type: "TEXT", nullable: false),
                    ExpectedExitCodesJson = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    DetectionConfigJson = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 300),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.PackageId);
                });

            migrationBuilder.CreateTable(
                name: "WorkloadRunTimelines",
                columns: table => new
                {
                    TimelineId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MessageType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    PackageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    PackageIndex = table.Column<int>(type: "INTEGER", nullable: true),
                    StepName = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    AtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadRunTimelines", x => x.TimelineId);
                });

            migrationBuilder.CreateTable(
                name: "AssignmentLeases",
                columns: table => new
                {
                    AssignmentId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LeaseId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AgentId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    TtlSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    LastHeartbeatUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAckedSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentLeases", x => x.AssignmentId);
                    table.CheckConstraint("CK_AssignmentLeases_LastAckedSequence", "\"LastAckedSequence\" >= 0");
                    table.CheckConstraint("CK_AssignmentLeases_State", "\"State\" IN ('Assigned','Released','Expired')");
                    table.CheckConstraint("CK_AssignmentLeases_TtlSeconds", "\"TtlSeconds\" > 0");
                    table.ForeignKey(
                        name: "FK_AssignmentLeases_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "JobId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JobSteps",
                columns: table => new
                {
                    JobStepId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    StepId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    ReasonCode = table.Column<int>(type: "INTEGER", nullable: true),
                    TelemetryRef = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Detail = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    StartedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobSteps", x => x.JobStepId);
                    table.CheckConstraint("CK_JobSteps_Status", "\"Status\" IN ('Pending','Running','Completed','Failed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_JobSteps_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "JobId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ConfigSnapshots",
                columns: table => new
                {
                    ConfigSnapshotId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JobId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    SourceSchemaVersion = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CapturedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StorageLocation = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    IntegrityHash = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConfigSnapshots", x => x.ConfigSnapshotId);
                    table.ForeignKey(
                        name: "FK_ConfigSnapshots_Jobs_JobId",
                        column: x => x.JobId,
                        principalTable: "Jobs",
                        principalColumn: "JobId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ConfigSnapshots_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "NodeWorkloadStates",
                columns: table => new
                {
                    NodeWorkloadStateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkloadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PackageStatesJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "Unknown"),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastProbedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeWorkloadStates", x => x.NodeWorkloadStateId);
                    table.CheckConstraint("CK_NodeWorkloadState_Status", "\"Status\" IN ('Current','Drifted','Unknown')");
                    table.ForeignKey(
                        name: "FK_NodeWorkloadStates_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkloadDefinitions",
                columns: table => new
                {
                    WorkloadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    PublishedRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadDefinitions", x => x.WorkloadId);
                });

            migrationBuilder.CreateTable(
                name: "WorkloadRevisions",
                columns: table => new
                {
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkloadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsPublished = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    PreWorkloadStepsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false, defaultValue: "[]"),
                    PostWorkloadStepsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false, defaultValue: "[]"),
                    PreUninstallStepsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false, defaultValue: "[]"),
                    PostUninstallStepsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false, defaultValue: "[]"),
                    DefaultShell = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false, defaultValue: "powershell")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadRevisions", x => x.RevisionId);
                    table.ForeignKey(
                        name: "FK_WorkloadRevisions_WorkloadDefinitions_WorkloadId",
                        column: x => x.WorkloadId,
                        principalTable: "WorkloadDefinitions",
                        principalColumn: "WorkloadId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkloadPackages",
                columns: table => new
                {
                    WorkloadPackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PackageIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    PreInitStepsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false),
                    PostInitStepsJson = table.Column<string>(type: "TEXT", maxLength: 4096, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadPackages", x => x.WorkloadPackageId);
                    table.ForeignKey(
                        name: "FK_WorkloadPackages_WorkloadRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "WorkloadRevisions",
                        principalColumn: "RevisionId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkloadRuns",
                columns: table => new
                {
                    WorkloadRunRecordId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RunId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkloadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NodeDisplayName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IdempotencyRequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    RiskLevel = table.Column<string>(type: "TEXT", nullable: true),
                    RevisionSnapshotJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ForceInstall = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadRuns", x => x.WorkloadRunRecordId);
                    table.CheckConstraint("CK_WorkloadRuns_Mode", "\"Mode\" IN ('install','update','uninstall','cancel')");
                    table.CheckConstraint("CK_WorkloadRuns_State", "\"State\" IN ('Queued','Running','Completed','Failed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_WorkloadRuns_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_WorkloadRuns_WorkloadDefinitions_WorkloadId",
                        column: x => x.WorkloadId,
                        principalTable: "WorkloadDefinitions",
                        principalColumn: "WorkloadId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkloadRuns_WorkloadRevisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "WorkloadRevisions",
                        principalColumn: "RevisionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentLeases_JobId",
                table: "AssignmentLeases",
                column: "JobId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentLeases_LeaseId",
                table: "AssignmentLeases",
                column: "LeaseId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ConfigSnapshots_JobId_NodeId_PackageId_CapturedAtUtc",
                table: "ConfigSnapshots",
                columns: new[] { "JobId", "NodeId", "PackageId", "CapturedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ConfigSnapshots_NodeId",
                table: "ConfigSnapshots",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentTokens_Token",
                table: "EnrollmentTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_IdempotencyKey",
                table: "Jobs",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JobSteps_JobId_Sequence",
                table: "JobSteps",
                columns: new[] { "JobId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Nodes_Hostname",
                table: "Nodes",
                column: "Hostname",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeWorkloadStates_CurrentRevisionId",
                table: "NodeWorkloadStates",
                column: "CurrentRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_NodeWorkloadStates_NodeId_WorkloadId",
                table: "NodeWorkloadStates",
                columns: new[] { "NodeId", "WorkloadId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_NodeWorkloadStates_WorkloadId",
                table: "NodeWorkloadStates",
                column: "WorkloadId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadDefinitions_Name",
                table: "WorkloadDefinitions",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadDefinitions_PublishedRevisionId",
                table: "WorkloadDefinitions",
                column: "PublishedRevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadPackages_RevisionId_PackageIndex",
                table: "WorkloadPackages",
                columns: new[] { "RevisionId", "PackageIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRevisions_WorkloadId_Version",
                table: "WorkloadRevisions",
                columns: new[] { "WorkloadId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRuns_IdempotencyKey",
                table: "WorkloadRuns",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRuns_NodeId_WorkloadId_Active",
                table: "WorkloadRuns",
                columns: new[] { "NodeId", "WorkloadId" },
                unique: true,
                filter: "\"State\" IN ('Queued','Running')");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRuns_RevisionId",
                table: "WorkloadRuns",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRuns_RunId",
                table: "WorkloadRuns",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRuns_WorkloadId",
                table: "WorkloadRuns",
                column: "WorkloadId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRunTimelines_RunId",
                table: "WorkloadRunTimelines",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRunTimelines_RunId_NodeId",
                table: "WorkloadRunTimelines",
                columns: new[] { "RunId", "NodeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_NodeWorkloadStates_WorkloadDefinitions_WorkloadId",
                table: "NodeWorkloadStates",
                column: "WorkloadId",
                principalTable: "WorkloadDefinitions",
                principalColumn: "WorkloadId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_NodeWorkloadStates_WorkloadRevisions_CurrentRevisionId",
                table: "NodeWorkloadStates",
                column: "CurrentRevisionId",
                principalTable: "WorkloadRevisions",
                principalColumn: "RevisionId",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkloadDefinitions_WorkloadRevisions_PublishedRevisionId",
                table: "WorkloadDefinitions",
                column: "PublishedRevisionId",
                principalTable: "WorkloadRevisions",
                principalColumn: "RevisionId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkloadRevisions_WorkloadDefinitions_WorkloadId",
                table: "WorkloadRevisions");

            migrationBuilder.DropTable(
                name: "AssignmentLeases");

            migrationBuilder.DropTable(
                name: "ConfigSnapshots");

            migrationBuilder.DropTable(
                name: "EnrollmentTokens");

            migrationBuilder.DropTable(
                name: "JobSteps");

            migrationBuilder.DropTable(
                name: "NodeWorkloadStates");

            migrationBuilder.DropTable(
                name: "Packages");

            migrationBuilder.DropTable(
                name: "WorkloadPackages");

            migrationBuilder.DropTable(
                name: "WorkloadRuns");

            migrationBuilder.DropTable(
                name: "WorkloadRunTimelines");

            migrationBuilder.DropTable(
                name: "Jobs");

            migrationBuilder.DropTable(
                name: "Nodes");

            migrationBuilder.DropTable(
                name: "WorkloadDefinitions");

            migrationBuilder.DropTable(
                name: "WorkloadRevisions");
        }
    }
}
