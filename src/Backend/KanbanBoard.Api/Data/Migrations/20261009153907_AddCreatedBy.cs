using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KanbanBoard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCreatedBy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CreatedBy",
                table: "WorkItem",
                type: "TEXT",
                maxLength: 256,
                nullable: true);

            // Backfill from each card's first history entry (its creation), so
            // existing in-app reports show their reporter too. Additive: only the
            // new column is written.
            migrationBuilder.Sql(
                """
                UPDATE "WorkItem"
                SET "CreatedBy" = (
                        SELECT h."Author" FROM "WorkItemHistory" h
                        WHERE h."WorkItemId" = "WorkItem"."Id"
                        ORDER BY h."ChangeDate", h."Id" LIMIT 1)
                WHERE "CreatedBy" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedBy",
                table: "WorkItem");
        }
    }
}
