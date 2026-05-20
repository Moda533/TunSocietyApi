using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using TunSociety.Api.Data;

#nullable disable

namespace TunSociety.Api.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(ApplicationDbContext))]
    [Migration("20260513100000_AddRolePermissions")]
    public partial class AddRolePermissions : Migration
    {
        private static readonly DateTime SeededAtUtc = new(2026, 5, 13, 0, 0, 0, DateTimeKind.Utc);

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RolePermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false, collation: "ascii_general_ci"),
                    Role = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Permission = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    UpdatedAtUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RolePermissions", x => x.Id);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RolePermissions_Role_Permission",
                table: "RolePermissions",
                columns: new[] { "Role", "Permission" },
                unique: true);

            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122001", "Admin", "users.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122002", "Admin", "users.edit");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122003", "Admin", "users.delete");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122004", "Admin", "departments.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122005", "Admin", "departments.manage");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122006", "Admin", "badges.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122007", "Admin", "badges.manage");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122008", "Admin", "events.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122009", "Admin", "events.manage");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122010", "Admin", "appeals.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122011", "Admin", "appeals.review");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122012", "Admin", "moderation.review");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122013", "Admin", "moderation.freeze");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122014", "Admin", "moderation.ban");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122015", "Admin", "role-permissions.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122016", "Admin", "role-permissions.manage");

            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122101", "Moderator", "users.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122102", "Moderator", "departments.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122103", "Moderator", "badges.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122104", "Moderator", "events.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122105", "Moderator", "events.manage");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122106", "Moderator", "appeals.read");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122107", "Moderator", "appeals.review");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122108", "Moderator", "moderation.review");
            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122109", "Moderator", "moderation.freeze");

            SeedPermission(migrationBuilder, "d0c53cf8-1233-49b7-9f67-e2484f122201", "User", "events.read");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "RolePermissions");
        }

        private static void SeedPermission(MigrationBuilder migrationBuilder, string id, string role, string permission)
        {
            migrationBuilder.Sql($"""
                INSERT INTO `RolePermissions` (`Id`, `Role`, `Permission`, `CreatedAtUtc`, `UpdatedAtUtc`)
                VALUES ('{id}', '{role}', '{permission}', '{SeededAtUtc:yyyy-MM-dd HH:mm:ss.ffffff}', '{SeededAtUtc:yyyy-MM-dd HH:mm:ss.ffffff}');
                """);
        }
    }
}
