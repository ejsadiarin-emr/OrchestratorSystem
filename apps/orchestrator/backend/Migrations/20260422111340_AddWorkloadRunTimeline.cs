using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkloadRunTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRunTimelines_RunId",
                table: "WorkloadRunTimelines",
                column: "RunId");

            migrationBuilder.CreateIndex(
                name: "IX_WorkloadRunTimelines_RunId_NodeId",
                table: "WorkloadRunTimelines",
                columns: new[] { "RunId", "NodeId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WorkloadRunTimelines");
        }
    }
}
