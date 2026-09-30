using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbanBoard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiscussionStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CommentCount",
                table: "WorkItem",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "HumanReadAt",
                table: "WorkItem",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastAgentCommentAt",
                table: "WorkItem",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastAgentCommentBy",
                table: "WorkItem",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            // Backfill from existing history. HumanReadAt stays empty on purpose,
            // so cards agents already commented on show as "New reply" after the
            // upgrade. Additive: only the four new columns are written.
            migrationBuilder.Sql(
                """
                UPDATE "WorkItem"
                SET "CommentCount" = (
                        SELECT COUNT(*) FROM "WorkItemHistory" h
                        WHERE h."WorkItemId" = "WorkItem"."Id" AND h."Comment" IS NOT NULL),
                    "LastAgentCommentAt" = (
                        SELECT h."ChangeDate" FROM "WorkItemHistory" h
                        WHERE h."WorkItemId" = "WorkItem"."Id" AND h."IsAiAction" = 1 AND h."Comment" IS NOT NULL
                        ORDER BY h."ChangeDate" DESC, h."Id" DESC LIMIT 1),
                    "LastAgentCommentBy" = (
                        SELECT COALESCE(h."AgentName", h."Author") FROM "WorkItemHistory" h
                        WHERE h."WorkItemId" = "WorkItem"."Id" AND h."IsAiAction" = 1 AND h."Comment" IS NOT NULL
                        ORDER BY h."ChangeDate" DESC, h."Id" DESC LIMIT 1);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CommentCount",
                table: "WorkItem");

            migrationBuilder.DropColumn(
                name: "HumanReadAt",
                table: "WorkItem");

            migrationBuilder.DropColumn(
                name: "LastAgentCommentAt",
                table: "WorkItem");

            migrationBuilder.DropColumn(
                name: "LastAgentCommentBy",
                table: "WorkItem");
        }
    }
}
