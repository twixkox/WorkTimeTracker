using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkTimeTracker.Migrations
{
    /// <inheritdoc />
    public partial class RbacAndNotificationCenter : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Category",
                table: "Notifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.CreateTable(
                name: "UserRoleScopes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    RoleId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    Scope = table.Column<int>(type: "INTEGER", nullable: false),
                    DepartmentId = table.Column<int>(type: "INTEGER", nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ValidTo = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoleScopes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserRoleScopes_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoleScopes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoleScopes_Departments_DepartmentId",
                        column: x => x.DepartmentId,
                        principalTable: "Departments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleScopes_DepartmentId",
                table: "UserRoleScopes",
                column: "DepartmentId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleScopes_RoleId",
                table: "UserRoleScopes",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoleScopes_UserId_RoleId_Scope_DepartmentId",
                table: "UserRoleScopes",
                columns: new[] { "UserId", "RoleId", "Scope", "DepartmentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserRoleScopes");

            migrationBuilder.DropColumn(
                name: "Category",
                table: "Notifications");
        }
    }
}
