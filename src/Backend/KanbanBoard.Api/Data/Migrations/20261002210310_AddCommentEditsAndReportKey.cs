using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbanBoard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCommentEditsAndReportKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "EditedAt",
                table: "WorkItemHistory",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ReportKey",
                table: "WorkItem",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "CommentRevision",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    HistoryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: false),
                    ReplacedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ReplacedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CommentRevision", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CommentRevision_WorkItemHistory_HistoryId",
                        column: x => x.HistoryId,
                        principalTable: "WorkItemHistory",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItem_ReportKey",
                table: "WorkItem",
                column: "ReportKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CommentRevision_HistoryId",
                table: "CommentRevision",
                column: "HistoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CommentRevision");

            migrationBuilder.DropIndex(
                name: "IX_WorkItem_ReportKey",
                table: "WorkItem");

            migrationBuilder.DropColumn(
                name: "EditedAt",
                table: "WorkItemHistory");

            migrationBuilder.DropColumn(
                name: "ReportKey",
                table: "WorkItem");
        }
    }
}
