using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbanBoard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveDefaultWipLimits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BoardColumn",
                keyColumn: "Id",
                keyValue: 2,
                column: "WipLimit",
                value: null);

            migrationBuilder.UpdateData(
                table: "BoardColumn",
                keyColumn: "Id",
                keyValue: 3,
                column: "WipLimit",
                value: null);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.UpdateData(
                table: "BoardColumn",
                keyColumn: "Id",
                keyValue: 2,
                column: "WipLimit",
                value: 5);

            migrationBuilder.UpdateData(
                table: "BoardColumn",
                keyColumn: "Id",
                keyValue: 3,
                column: "WipLimit",
                value: 5);
        }
    }
}
