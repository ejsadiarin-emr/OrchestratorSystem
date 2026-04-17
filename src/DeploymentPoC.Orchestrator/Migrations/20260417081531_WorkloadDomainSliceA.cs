using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class WorkloadDomainSliceA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NodeWorkloadStates",
                columns: table => new
                {
                    NodeWorkloadStateId = table.Column<Guid>(type: "TEXT", nullable: false),
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    WorkloadId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PackageStatesJson = table.Column<string>(type: "TEXT", maxLength: 8192, nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NodeWorkloadStates", x => x.NodeWorkloadStateId);
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
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
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
                    PackageIndex = table.Column<int>(type: "INTEGER", nullable: false)
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
                    NodeId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Mode = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    IdempotencyRequestHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    CancelReason = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CompletedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkloadRuns", x => x.WorkloadRunRecordId);
                    table.CheckConstraint("CK_WorkloadRuns_Mode", "\"Mode\" IN ('install','update','rollback','cancel')");
                    table.CheckConstraint("CK_WorkloadRuns_State", "\"State\" IN ('Queued','Running','Completed','Failed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_WorkloadRuns_Nodes_NodeId",
                        column: x => x.NodeId,
                        principalTable: "Nodes",
                        principalColumn: "NodeId",
                        onDelete: ReferentialAction.Cascade);
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
                name: "NodeWorkloadStates");

            migrationBuilder.DropTable(
                name: "WorkloadPackages");

            migrationBuilder.DropTable(
                name: "WorkloadRuns");

            migrationBuilder.DropTable(
                name: "WorkloadDefinitions");

            migrationBuilder.DropTable(
                name: "WorkloadRevisions");
        }
    }
}
