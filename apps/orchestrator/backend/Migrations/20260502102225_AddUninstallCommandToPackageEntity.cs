using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddUninstallCommandToPackageEntity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "UninstallCommand",
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
                name: "UninstallCommand",
                table: "Packages");
        }
    }
}
