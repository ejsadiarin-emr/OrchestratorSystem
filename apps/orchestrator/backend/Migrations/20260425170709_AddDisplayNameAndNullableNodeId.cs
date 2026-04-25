using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class AddDisplayNameAndNullableNodeId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkloadRuns_Nodes_NodeId",
                table: "WorkloadRuns");

            migrationBuilder.AlterColumn<Guid>(
                name: "NodeId",
                table: "WorkloadRuns",
                type: "TEXT",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "TEXT");

            migrationBuilder.AddColumn<string>(
                name: "NodeDisplayName",
                table: "WorkloadRuns",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "Nodes",
                type: "TEXT",
                maxLength: 255,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkloadRuns_Nodes_NodeId",
                table: "WorkloadRuns",
                column: "NodeId",
                principalTable: "Nodes",
                principalColumn: "NodeId",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkloadRuns_Nodes_NodeId",
                table: "WorkloadRuns");

            migrationBuilder.DropColumn(
                name: "NodeDisplayName",
                table: "WorkloadRuns");

            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "Nodes");

            migrationBuilder.AlterColumn<Guid>(
                name: "NodeId",
                table: "WorkloadRuns",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "TEXT",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkloadRuns_Nodes_NodeId",
                table: "WorkloadRuns",
                column: "NodeId",
                principalTable: "Nodes",
                principalColumn: "NodeId",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
