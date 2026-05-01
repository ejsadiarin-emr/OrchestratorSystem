using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceRollbackWithUninstallInCheckConstraint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkloadRuns_Mode",
                table: "WorkloadRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkloadRuns_Mode",
                table: "WorkloadRuns",
                sql: "\"Mode\" IN ('install','update','uninstall','cancel')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WorkloadRuns_Mode",
                table: "WorkloadRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WorkloadRuns_Mode",
                table: "WorkloadRuns",
                sql: "\"Mode\" IN ('install','update','rollback','cancel')");
        }
    }
}
