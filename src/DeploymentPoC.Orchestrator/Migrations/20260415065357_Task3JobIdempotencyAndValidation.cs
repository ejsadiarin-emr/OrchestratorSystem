using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DeploymentPoC.Orchestrator.Migrations
{
    /// <inheritdoc />
    public partial class Task3JobIdempotencyAndValidation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancelReason",
                table: "Jobs",
                type: "TEXT",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "Jobs",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IdempotencyRequestHash",
                table: "Jobs",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_IdempotencyKey",
                table: "Jobs",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Jobs_IdempotencyKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "CancelReason",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "Jobs");

            migrationBuilder.DropColumn(
                name: "IdempotencyRequestHash",
                table: "Jobs");
        }
    }
}
