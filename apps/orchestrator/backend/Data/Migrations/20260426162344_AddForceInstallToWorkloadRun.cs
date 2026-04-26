using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddForceInstallToWorkloadRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ForceInstall",
                table: "WorkloadRuns",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "DetectionConfigJson",
                table: "Packages",
                type: "TEXT",
                maxLength: 2048,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ForceInstall",
                table: "WorkloadRuns");

            migrationBuilder.DropColumn(
                name: "DetectionConfigJson",
                table: "Packages");
        }
    }
}
