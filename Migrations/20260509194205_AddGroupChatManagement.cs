using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TunSociety.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddGroupChatManagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Notifications",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedGroupConversationId",
                table: "Notifications",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<string>(
                name: "CreateRoomPermission",
                table: "GroupConversations",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "AdminsAndModerators")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "DeletedAtUtc",
                table: "GroupConversations",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Introduction",
                table: "GroupConversations",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "InviteCode",
                table: "GroupConversations",
                type: "varchar(48)",
                maxLength: 48,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "Notice",
                table: "GroupConversations",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<DateTime>(
                name: "ClearedAtUtc",
                table: "GroupConversationMembers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "InvitedAtUtc",
                table: "GroupConversationMembers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "InvitedByUserId",
                table: "GroupConversationMembers",
                type: "char(36)",
                nullable: true,
                collation: "ascii_general_ci");

            migrationBuilder.AddColumn<bool>(
                name: "IsMuted",
                table: "GroupConversationMembers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsPinned",
                table: "GroupConversationMembers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LeftAtUtc",
                table: "GroupConversationMembers",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "GroupConversationMembers",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "Active")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("UPDATE `GroupConversations` SET `InviteCode` = REPLACE(CAST(`Id` AS CHAR), '-', '') WHERE `InviteCode` IS NULL OR `InviteCode` = '';");

            migrationBuilder.AlterColumn<string>(
                name: "InviteCode",
                table: "GroupConversations",
                type: "varchar(48)",
                maxLength: 48,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "varchar(48)",
                oldMaxLength: 48,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedGroupConversationId",
                table: "Notifications",
                column: "RelatedGroupConversationId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupConversations_InviteCode",
                table: "GroupConversations",
                column: "InviteCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GroupConversationMembers_GroupConversationId_Status",
                table: "GroupConversationMembers",
                columns: new[] { "GroupConversationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_GroupConversationMembers_InvitedByUserId",
                table: "GroupConversationMembers",
                column: "InvitedByUserId");

            migrationBuilder.AddForeignKey(
                name: "FK_GroupConversationMembers_Users_InvitedByUserId",
                table: "GroupConversationMembers",
                column: "InvitedByUserId",
                principalTable: "Users",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_GroupConversationMembers_Users_InvitedByUserId",
                table: "GroupConversationMembers");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RelatedGroupConversationId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_GroupConversations_InviteCode",
                table: "GroupConversations");

            migrationBuilder.DropIndex(
                name: "IX_GroupConversationMembers_GroupConversationId_Status",
                table: "GroupConversationMembers");

            migrationBuilder.DropIndex(
                name: "IX_GroupConversationMembers_InvitedByUserId",
                table: "GroupConversationMembers");

            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RelatedGroupConversationId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "CreateRoomPermission",
                table: "GroupConversations");

            migrationBuilder.DropColumn(
                name: "DeletedAtUtc",
                table: "GroupConversations");

            migrationBuilder.DropColumn(
                name: "Introduction",
                table: "GroupConversations");

            migrationBuilder.DropColumn(
                name: "InviteCode",
                table: "GroupConversations");

            migrationBuilder.DropColumn(
                name: "Notice",
                table: "GroupConversations");

            migrationBuilder.DropColumn(
                name: "ClearedAtUtc",
                table: "GroupConversationMembers");

            migrationBuilder.DropColumn(
                name: "InvitedAtUtc",
                table: "GroupConversationMembers");

            migrationBuilder.DropColumn(
                name: "InvitedByUserId",
                table: "GroupConversationMembers");

            migrationBuilder.DropColumn(
                name: "IsMuted",
                table: "GroupConversationMembers");

            migrationBuilder.DropColumn(
                name: "IsPinned",
                table: "GroupConversationMembers");

            migrationBuilder.DropColumn(
                name: "LeftAtUtc",
                table: "GroupConversationMembers");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "GroupConversationMembers");
        }
    }
}
