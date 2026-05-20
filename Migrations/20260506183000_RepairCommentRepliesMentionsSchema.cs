using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TunSociety.Api.Data;

#nullable disable

namespace TunSociety.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260506183000_RepairCommentRepliesMentionsSchema")]
    public partial class RepairCommentRepliesMentionsSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AddColumnIfMissing(
                migrationBuilder,
                "PostComments",
                "MentionedUserIdsJson",
                "ALTER TABLE `PostComments` ADD COLUMN `MentionedUserIdsJson` varchar(1000) CHARACTER SET utf8mb4 NOT NULL DEFAULT '[]';");

            AddColumnIfMissing(
                migrationBuilder,
                "PostComments",
                "ParentCommentId",
                "ALTER TABLE `PostComments` ADD COLUMN `ParentCommentId` char(36) NULL;");

            AddColumnIfMissing(
                migrationBuilder,
                "Notifications",
                "RelatedCommentId",
                "ALTER TABLE `Notifications` ADD COLUMN `RelatedCommentId` char(36) NULL;");

            AddColumnIfMissing(
                migrationBuilder,
                "Notifications",
                "RelatedPostId",
                "ALTER TABLE `Notifications` ADD COLUMN `RelatedPostId` char(36) NULL;");

            AddColumnIfMissing(
                migrationBuilder,
                "Notifications",
                "RelatedReplyId",
                "ALTER TABLE `Notifications` ADD COLUMN `RelatedReplyId` char(36) NULL;");

            AddIndexIfMissing(
                migrationBuilder,
                "PostComments",
                "IX_PostComments_ParentCommentId",
                "ALTER TABLE `PostComments` ADD INDEX `IX_PostComments_ParentCommentId` (`ParentCommentId`);");

            AddIndexIfMissing(
                migrationBuilder,
                "Notifications",
                "IX_Notifications_RelatedCommentId",
                "ALTER TABLE `Notifications` ADD INDEX `IX_Notifications_RelatedCommentId` (`RelatedCommentId`);");

            AddIndexIfMissing(
                migrationBuilder,
                "Notifications",
                "IX_Notifications_RelatedPostId",
                "ALTER TABLE `Notifications` ADD INDEX `IX_Notifications_RelatedPostId` (`RelatedPostId`);");

            AddIndexIfMissing(
                migrationBuilder,
                "Notifications",
                "IX_Notifications_RelatedReplyId",
                "ALTER TABLE `Notifications` ADD INDEX `IX_Notifications_RelatedReplyId` (`RelatedReplyId`);");

            // Some local databases already contain legacy PostComments metadata that rejects
            // this self-reference. The runtime queries need the column and indexes above.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }

        private static void AddColumnIfMissing(MigrationBuilder migrationBuilder, string tableName, string columnName, string alterSql)
        {
            migrationBuilder.Sql($"""
                SET @column_exists := (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_SCHEMA = DATABASE()
                        AND TABLE_NAME = '{tableName}'
                        AND COLUMN_NAME = '{columnName}'
                );
                SET @statement := IF(@column_exists = 0, '{EscapeSql(alterSql)}', 'SELECT 1');
                PREPARE schema_repair_statement FROM @statement;
                EXECUTE schema_repair_statement;
                DEALLOCATE PREPARE schema_repair_statement;
                """);
        }

        private static void AddIndexIfMissing(MigrationBuilder migrationBuilder, string tableName, string indexName, string alterSql)
        {
            migrationBuilder.Sql($"""
                SET @index_exists := (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.STATISTICS
                    WHERE TABLE_SCHEMA = DATABASE()
                        AND TABLE_NAME = '{tableName}'
                        AND INDEX_NAME = '{indexName}'
                );
                SET @statement := IF(@index_exists = 0, '{EscapeSql(alterSql)}', 'SELECT 1');
                PREPARE schema_repair_statement FROM @statement;
                EXECUTE schema_repair_statement;
                DEALLOCATE PREPARE schema_repair_statement;
                """);
        }

        private static void AddForeignKeyIfMissing(MigrationBuilder migrationBuilder, string constraintName, string alterSql)
        {
            migrationBuilder.Sql($"""
                SET @constraint_exists := (
                    SELECT COUNT(*)
                    FROM INFORMATION_SCHEMA.TABLE_CONSTRAINTS
                    WHERE TABLE_SCHEMA = DATABASE()
                        AND CONSTRAINT_NAME = '{constraintName}'
                );
                SET @statement := IF(@constraint_exists = 0, '{EscapeSql(alterSql)}', 'SELECT 1');
                PREPARE schema_repair_statement FROM @statement;
                EXECUTE schema_repair_statement;
                DEALLOCATE PREPARE schema_repair_statement;
                """);
        }

        private static string EscapeSql(string sql)
        {
            return sql.Replace("'", "''");
        }
    }
}
