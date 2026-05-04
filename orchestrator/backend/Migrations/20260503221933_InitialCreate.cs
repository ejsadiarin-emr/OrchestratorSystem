using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Artifacts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PackageId = table.Column<string>(type: "TEXT", nullable: false),
                    PackageName = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    InstallerFile = table.Column<string>(type: "TEXT", nullable: false),
                    ManifestPath = table.Column<string>(type: "TEXT", nullable: false),
                    BinaryPath = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Artifacts", x => x.Id);
                    table.UniqueConstraint("AK_Artifacts_PackageId_Version", x => new { x.PackageId, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "EnrollmentTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Used = table.Column<bool>(type: "INTEGER", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UsedByAgentId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EnrollmentTokens", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workloads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkloadId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkloadName = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    DefinitionPath = table.Column<string>(type: "TEXT", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workloads", x => x.Id);
                    table.UniqueConstraint("AK_Workloads_WorkloadId", x => x.WorkloadId);
                    table.UniqueConstraint("AK_Workloads_WorkloadId_Version", x => new { x.WorkloadId, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "AgentNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    Hostname = table.Column<string>(type: "TEXT", nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", nullable: false),
                    AgentSecret = table.Column<string>(type: "TEXT", nullable: false),
                    LastSeenAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    RegisteredAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AssignedWorkloadId = table.Column<string>(type: "TEXT", nullable: true),
                    AssignedWorkloadVersion = table.Column<string>(type: "TEXT", nullable: true),
                    PollingIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentNodes", x => x.Id);
                    table.UniqueConstraint("AK_AgentNodes_AgentId", x => x.AgentId);
                    table.ForeignKey(
                        name: "FK_AgentNodes_Workloads_AssignedWorkloadId",
                        column: x => x.AssignedWorkloadId,
                        principalTable: "Workloads",
                        principalColumn: "WorkloadId");
                });

            migrationBuilder.CreateTable(
                name: "WorkloadPackages",
                columns: table => new
                {
                    WorkloadId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkloadVersion = table.Column<string>(type: "TEXT", nullable: false),
                    PackageId = table.Column<string>(type: "TEXT", nullable: false),
                    PackageVersion = table.Column<string>(type: "TEXT", nullable: false),
                    PreInitSteps = table.Column<string>(type: "TEXT", nullable: true),
                    PostInitSteps = table.Column<string>(type: "TEXT", nullable: true),
                    DownloadUrl = table.Column<string>(type: "TEXT", nullable: true),
                    Hash = table.Column<string>(type: "TEXT", nullable: true),
                    UpdateStrategy = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadPackages", x => new { x.WorkloadId, x.WorkloadVersion, x.PackageId });
                    table.ForeignKey(
                        name: "FK_WorkloadPackages_Artifacts_PackageId_PackageVersion",
                        columns: x => new { x.PackageId, x.PackageVersion },
                        principalTable: "Artifacts",
                        principalColumns: new[] { "PackageId", "Version" },
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkloadPackages_Workloads_WorkloadId_WorkloadVersion",
                        columns: x => new { x.WorkloadId, x.WorkloadVersion },
                        principalTable: "Workloads",
                        principalColumns: new[] { "WorkloadId", "Version" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentPackages",
                columns: table => new
                {
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    PackageId = table.Column<string>(type: "TEXT", nullable: false),
                    InstalledVersion = table.Column<string>(type: "TEXT", nullable: false),
                    DetectedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentPackages", x => new { x.AgentId, x.PackageId });
                    table.ForeignKey(
                        name: "FK_AgentPackages_AgentNodes_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentNodes",
                        principalColumn: "AgentId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkloadRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgentId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkloadId = table.Column<string>(type: "TEXT", nullable: false),
                    WorkloadVersion = table.Column<string>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkloadRuns_AgentNodes_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentNodes",
                        principalColumn: "AgentId",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkloadRuns_Workloads_WorkloadId_WorkloadVersion",
                        columns: x => new { x.WorkloadId, x.WorkloadVersion },
                        principalTable: "Workloads",
                        principalColumns: new[] { "WorkloadId", "Version" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WorkloadRunSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<int>(type: "INTEGER", nullable: false),
                    PackageId = table.Column<string>(type: "TEXT", nullable: false),
                    PackageVersion = table.Column<string>(type: "TEXT", nullable: false),
                    StepOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Message = table.Column<string>(type: "TEXT", nullable: true),
                    ExitCode = table.Column<int>(type: "INTEGER", nullable: true),
                    StartedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CompletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadRunSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkloadRunSteps_WorkloadRuns_RunId",
                        column: x => x.RunId,
                        principalTable: "WorkloadRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentNodes_AgentId",
                table: "AgentNodes",
                column: "AgentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentNodes_AgentSecret",
                table: "AgentNodes",
                column: "AgentSecret",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentNodes_AssignedWorkloadId",
                table: "AgentNodes",
                column: "AssignedWorkloadId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentNodes_Status",
                table: "AgentNodes",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_Artifacts_PackageId_Version",
                table: "Artifacts",
                columns: new[] { "PackageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EnrollmentTokens_Token",
                table: "EnrollmentTokens",
                column: "Token");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadPackages_PackageId_PackageVersion",
                table: "WorkloadPackages",
                columns: new[] { "PackageId", "PackageVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRuns_AgentId",
                table: "WorkloadRuns",
                column: "AgentId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRuns_Status",
                table: "WorkloadRuns",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRuns_WorkloadId_WorkloadVersion",
                table: "WorkloadRuns",
                columns: new[] { "WorkloadId", "WorkloadVersion" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRunSteps_RunId",
                table: "WorkloadRunSteps",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_Workloads_WorkloadId_Version",
                table: "Workloads",
                columns: new[] { "WorkloadId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentPackages");

            migrationBuilder.DropTable(
                name: "EnrollmentTokens");

            migrationBuilder.DropTable(
                name: "WorkloadPackages");

            migrationBuilder.DropTable(
                name: "WorkloadRunSteps");

            migrationBuilder.DropTable(
                name: "Artifacts");

            migrationBuilder.DropTable(
                name: "WorkloadRuns");

            migrationBuilder.DropTable(
                name: "AgentNodes");

            migrationBuilder.DropTable(
                name: "Workloads");
        }
    }
}
