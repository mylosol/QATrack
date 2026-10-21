using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbanBoard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkItemRank : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Rank",
                table: "WorkItem",
                type: "REAL",
                nullable: false,
                defaultValue: 0.0);

            // Keep today's order: within a priority, cards were listed oldest first.
            migrationBuilder.Sql("UPDATE WorkItem SET Rank = Id;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Rank",
                table: "WorkItem");
        }
    }
}
