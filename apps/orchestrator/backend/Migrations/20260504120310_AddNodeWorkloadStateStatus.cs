using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNodeWorkloadStateStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "NodeWorkloadStates",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "Unknown");

            migrationBuilder.AddCheckConstraint(
                name: "CK_NodeWorkloadState_Status",
                table: "NodeWorkloadStates",
                sql: "\"Status\" IN ('Current','Drifted','Unknown')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_NodeWorkloadState_Status",
                table: "NodeWorkloadStates");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "NodeWorkloadStates");
        }
    }
}
