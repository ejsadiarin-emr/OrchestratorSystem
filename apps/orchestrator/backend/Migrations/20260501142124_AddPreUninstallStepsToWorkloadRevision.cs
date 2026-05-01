using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPreUninstallStepsToWorkloadRevision : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PostUninstallStepsJson",
                table: "WorkloadRevisions",
                type: "TEXT",
                maxLength: 4096,
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "PreUninstallStepsJson",
                table: "WorkloadRevisions",
                type: "TEXT",
                maxLength: 4096,
                nullable: false,
                defaultValue: "[]");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PostUninstallStepsJson",
                table: "WorkloadRevisions");

            migrationBuilder.DropColumn(
                name: "PreUninstallStepsJson",
                table: "WorkloadRevisions");
        }
    }
}
