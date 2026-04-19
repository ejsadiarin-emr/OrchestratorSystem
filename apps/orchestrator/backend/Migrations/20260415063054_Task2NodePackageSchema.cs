using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class Task2NodePackageSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Nodes",
                type: "TEXT",
                maxLength: 512,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "IpAddress",
                table: "Nodes",
                type: "TEXT",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

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
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Packages", x => x.PackageId);
                });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Nodes_Status",
                table: "Nodes",
                sql: "\"Status\" IN ('Offline','Online')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobSteps_Status",
                table: "JobSteps",
                sql: "\"Status\" IN ('Pending','Running','Completed','Failed','Cancelled')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Jobs_Mode",
                table: "Jobs",
                sql: "\"Mode\" IN ('install','upgrade','rollback','modify','cancel')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Jobs_State",
                table: "Jobs",
                sql: "\"State\" IN ('Queued','Running','Completed','Failed','Cancelled')");

            migrationBuilder.CreateIndex(
                name: "IX_ConfigSnapshots_NodeId",
                table: "ConfigSnapshots",
                column: "NodeId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentLeases_JobId",
                table: "AssignmentLeases",
                column: "JobId");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AssignmentLeases_LastAckedSequence",
                table: "AssignmentLeases",
                sql: "\"LastAckedSequence\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AssignmentLeases_State",
                table: "AssignmentLeases",
                sql: "\"State\" IN ('Assigned','Released','Expired')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_AssignmentLeases_TtlSeconds",
                table: "AssignmentLeases",
                sql: "\"TtlSeconds\" > 0");

            migrationBuilder.AddForeignKey(
                name: "FK_AssignmentLeases_Jobs_JobId",
                table: "AssignmentLeases",
                column: "JobId",
                principalTable: "Jobs",
                principalColumn: "JobId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ConfigSnapshots_Jobs_JobId",
                table: "ConfigSnapshots",
                column: "JobId",
                principalTable: "Jobs",
                principalColumn: "JobId",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_ConfigSnapshots_Nodes_NodeId",
                table: "ConfigSnapshots",
                column: "NodeId",
                principalTable: "Nodes",
                principalColumn: "NodeId",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AssignmentLeases_Jobs_JobId",
                table: "AssignmentLeases");

            migrationBuilder.DropForeignKey(
                name: "FK_ConfigSnapshots_Jobs_JobId",
                table: "ConfigSnapshots");

            migrationBuilder.DropForeignKey(
                name: "FK_ConfigSnapshots_Nodes_NodeId",
                table: "ConfigSnapshots");

            migrationBuilder.DropTable(
                name: "Packages");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Nodes_Status",
                table: "Nodes");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobSteps_Status",
                table: "JobSteps");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Jobs_Mode",
                table: "Jobs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Jobs_State",
                table: "Jobs");

            migrationBuilder.DropIndex(
                name: "IX_ConfigSnapshots_NodeId",
                table: "ConfigSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_AssignmentLeases_JobId",
                table: "AssignmentLeases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AssignmentLeases_LastAckedSequence",
                table: "AssignmentLeases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AssignmentLeases_State",
                table: "AssignmentLeases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_AssignmentLeases_TtlSeconds",
                table: "AssignmentLeases");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "Nodes");

            migrationBuilder.DropColumn(
                name: "IpAddress",
                table: "Nodes");
        }
    }
}
