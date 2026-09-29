using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace KanbanBoard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BoardColumn",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    WipLimit = table.Column<int>(type: "INTEGER", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BoardColumn", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Swimlane",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Swimlane", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkItem",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Type = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    State = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false, defaultValue: 2),
                    Severity = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false, defaultValue: "3 - Medium"),
                    AssignedTo = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    AreaPath = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, defaultValue: "Tools\\QA"),
                    IterationPath = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false, defaultValue: "Current"),
                    AiModified = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: false),
                    AiAgentIdentity = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    LastModifiedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItem", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChangeDate = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Author = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IsAiAction = table.Column<bool>(type: "INTEGER", nullable: false),
                    AgentName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ChangedFieldsJson = table.Column<string>(type: "TEXT", nullable: false),
                    Comment = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WorkItemHistory_WorkItem_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "BoardColumn",
                columns: new[] { "Id", "Name", "SortOrder", "State", "WipLimit" },
                values: new object[,]
                {
                    { 1, "New", 0, "New", null },
                    { 2, "Active", 1, "Active", 5 },
                    { 3, "Resolved", 2, "Resolved", 5 },
                    { 4, "Closed", 3, "Closed", null }
                });

            migrationBuilder.InsertData(
                table: "Swimlane",
                columns: new[] { "Id", "IsDefault", "Name", "SortOrder" },
                values: new object[] { 1, true, "Default", 0 });

            migrationBuilder.CreateIndex(
                name: "IX_BoardColumn_State",
                table: "BoardColumn",
                column: "State",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItem_AiModified",
                table: "WorkItem",
                column: "AiModified");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItem_AssignedTo",
                table: "WorkItem",
                column: "AssignedTo");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItem_State",
                table: "WorkItem",
                column: "State");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItem_Type",
                table: "WorkItem",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemHistory_WorkItemId_ChangeDate",
                table: "WorkItemHistory",
                columns: new[] { "WorkItemId", "ChangeDate" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BoardColumn");

            migrationBuilder.DropTable(
                name: "Swimlane");

            migrationBuilder.DropTable(
                name: "WorkItemHistory");

            migrationBuilder.DropTable(
                name: "WorkItem");
        }
    }
}
