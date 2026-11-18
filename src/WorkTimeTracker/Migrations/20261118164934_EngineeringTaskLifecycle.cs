using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace WorkTimeTracker.Migrations
{
    /// <inheritdoc />
    public partial class EngineeringTaskLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "BoatId",
                table: "EngineeringTasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "DeadlineReminderSentOn",
                table: "EngineeringTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "OverdueReminderSentOn",
                table: "EngineeringTasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                table: "EngineeringTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "EngineeringTasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql(
                """
                UPDATE "EngineeringTasks"
                SET "Priority" = 1,
                    "Status" = CASE
                        WHEN "CompletedOn" IS NOT NULL THEN 3
                        WHEN "StartedOn" IS NOT NULL THEN 2
                        WHEN "AssigneeId" IS NOT NULL THEN 1
                        ELSE 0
                    END;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringTasks_BoatId",
                table: "EngineeringTasks",
                column: "BoatId");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringTasks_Deadline",
                table: "EngineeringTasks",
                column: "Deadline");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringTasks_Priority",
                table: "EngineeringTasks",
                column: "Priority");

            migrationBuilder.CreateIndex(
                name: "IX_EngineeringTasks_Status",
                table: "EngineeringTasks",
                column: "Status");

            migrationBuilder.AddForeignKey(
                name: "FK_EngineeringTasks_Boats_BoatId",
                table: "EngineeringTasks",
                column: "BoatId",
                principalTable: "Boats",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_EngineeringTasks_Boats_BoatId",
                table: "EngineeringTasks");

            migrationBuilder.DropIndex(
                name: "IX_EngineeringTasks_BoatId",
                table: "EngineeringTasks");

            migrationBuilder.DropIndex(
                name: "IX_EngineeringTasks_Deadline",
                table: "EngineeringTasks");

            migrationBuilder.DropIndex(
                name: "IX_EngineeringTasks_Priority",
                table: "EngineeringTasks");

            migrationBuilder.DropIndex(
                name: "IX_EngineeringTasks_Status",
                table: "EngineeringTasks");

            migrationBuilder.DropColumn(
                name: "BoatId",
                table: "EngineeringTasks");

            migrationBuilder.DropColumn(
                name: "DeadlineReminderSentOn",
                table: "EngineeringTasks");

            migrationBuilder.DropColumn(
                name: "OverdueReminderSentOn",
                table: "EngineeringTasks");

            migrationBuilder.DropColumn(
                name: "Priority",
                table: "EngineeringTasks");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "EngineeringTasks");
        }
    }
}
