using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbanBoard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLastHumanComment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastHumanCommentAt",
                table: "WorkItem",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastHumanCommentBy",
                table: "WorkItem",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            // Backfill from existing history so comments made before 1.8.0 count too.
            // Additive: only fills the two new columns; no other data is touched.
            migrationBuilder.Sql(
                """
                UPDATE "WorkItem"
                SET "LastHumanCommentAt" = (
                        SELECT h."ChangeDate" FROM "WorkItemHistory" h
                        WHERE h."WorkItemId" = "WorkItem"."Id" AND h."IsAiAction" = 0 AND h."Comment" IS NOT NULL
                        ORDER BY h."ChangeDate" DESC, h."Id" DESC LIMIT 1),
                    "LastHumanCommentBy" = (
                        SELECT h."Author" FROM "WorkItemHistory" h
                        WHERE h."WorkItemId" = "WorkItem"."Id" AND h."IsAiAction" = 0 AND h."Comment" IS NOT NULL
                        ORDER BY h."ChangeDate" DESC, h."Id" DESC LIMIT 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItem_UpdatedAt",
                table: "WorkItem",
                column: "UpdatedAt");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_WorkItem_UpdatedAt",
                table: "WorkItem");

            migrationBuilder.DropColumn(
                name: "LastHumanCommentAt",
                table: "WorkItem");

            migrationBuilder.DropColumn(
                name: "LastHumanCommentBy",
                table: "WorkItem");
        }
    }
}
