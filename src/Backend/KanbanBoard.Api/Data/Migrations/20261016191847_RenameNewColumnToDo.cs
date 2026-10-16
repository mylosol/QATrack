using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbanBoard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RenameNewColumnToDo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BoardColumn",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "To Do");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BoardColumn",
                keyColumn: "Id",
                keyValue: 1,
                column: "Name",
                value: "New");
        }
    }
}
