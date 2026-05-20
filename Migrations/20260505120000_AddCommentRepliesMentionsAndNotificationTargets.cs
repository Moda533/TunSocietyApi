using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TunSociety.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentRepliesMentionsAndNotificationTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MentionedUserIdsJson",
                table: "PostComments",
                type: "varchar(1000)",
                maxLength: 1000,
                nullable: false,
                defaultValue: "[]")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<Guid>(
                name: "ParentCommentId",
                table: "PostComments",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedCommentId",
                table: "Notifications",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedPostId",
                table: "Notifications",
                type: "char(36)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RelatedReplyId",
                table: "Notifications",
                type: "char(36)",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PostComments_ParentCommentId",
                table: "PostComments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedCommentId",
                table: "Notifications",
                column: "RelatedCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedPostId",
                table: "Notifications",
                column: "RelatedPostId");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_RelatedReplyId",
                table: "Notifications",
                column: "RelatedReplyId");

            migrationBuilder.AddForeignKey(
                name: "FK_PostComments_PostComments_ParentCommentId",
                table: "PostComments",
                column: "ParentCommentId",
                principalTable: "PostComments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PostComments_PostComments_ParentCommentId",
                table: "PostComments");

            migrationBuilder.DropIndex(
                name: "IX_PostComments_ParentCommentId",
                table: "PostComments");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RelatedCommentId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RelatedPostId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_RelatedReplyId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "MentionedUserIdsJson",
                table: "PostComments");

            migrationBuilder.DropColumn(
                name: "ParentCommentId",
                table: "PostComments");

            migrationBuilder.DropColumn(
                name: "RelatedCommentId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RelatedPostId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "RelatedReplyId",
                table: "Notifications");
        }
    }
}
