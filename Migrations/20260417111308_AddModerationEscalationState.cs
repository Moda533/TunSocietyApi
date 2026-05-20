using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TunSociety.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddModerationEscalationState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EscalatedAtUtc",
                table: "ModerationResults",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EscalationNote",
                table: "ModerationResults",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsEscalated",
                table: "ModerationResults",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_ModerationResults_IsEscalated_EscalatedAtUtc_CreatedAtUtc",
                table: "ModerationResults",
                columns: new[] { "IsEscalated", "EscalatedAtUtc", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ModerationResults_IsEscalated_EscalatedAtUtc_CreatedAtUtc",
                table: "ModerationResults");

            migrationBuilder.DropColumn(
                name: "EscalatedAtUtc",
                table: "ModerationResults");

            migrationBuilder.DropColumn(
                name: "EscalationNote",
                table: "ModerationResults");

            migrationBuilder.DropColumn(
                name: "IsEscalated",
                table: "ModerationResults");
        }
    }
}
