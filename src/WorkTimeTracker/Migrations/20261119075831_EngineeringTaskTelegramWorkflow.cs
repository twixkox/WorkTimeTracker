using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkTimeTracker.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringTaskTelegramWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedAt",
                table: "EngineeringTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "TelegramLinkedAt",
                table: "AspNetUsers",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "TelegramUserId",
                table: "AspNetUsers",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TelegramUsername",
                table: "AspNetUsers",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EngineeringTaskAttachments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EngineeringTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    StoredFileName = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    UploadedByUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    UploadedByName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringTaskAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngineeringTaskAttachments_EngineeringTasks_EngineeringTaskId",
                        column: x => x.EngineeringTaskId,
                        principalTable: "EngineeringTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "EngineeringTaskComments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EngineeringTaskId = table.Column<int>(type: "INTEGER", nullable: false),
                    AuthorUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    AuthorName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Text = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EngineeringTaskComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EngineeringTaskComments_EngineeringTasks_EngineeringTaskId",
                        column: x => x.EngineeringTaskId,
                        principalTable: "EngineeringTasks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TelegramLinkCodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: false),
                    CodeHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramLinkCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TelegramLinkCodes_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_TelegramUserId",
                table: "AspNetUsers",
                column: "TelegramUserId",
                unique: true,
                filter: "[TelegramUserId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringTaskAttachments_EngineeringTaskId",
                table: "EngineeringTaskAttachments",
                column: "EngineeringTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringTaskComments_EngineeringTaskId",
                table: "EngineeringTaskComments",
                column: "EngineeringTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinkCodes_CodeHash",
                table: "TelegramLinkCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TelegramLinkCodes_UserId",
                table: "TelegramLinkCodes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EngineeringTaskAttachments");

            migrationBuilder.DropTable(
                name: "EngineeringTaskComments");

            migrationBuilder.DropTable(
                name: "TelegramLinkCodes");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_TelegramUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                table: "EngineeringTasks");

            migrationBuilder.DropColumn(
                name: "TelegramLinkedAt",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TelegramUserId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "TelegramUsername",
                table: "AspNetUsers");
        }
    }
}
