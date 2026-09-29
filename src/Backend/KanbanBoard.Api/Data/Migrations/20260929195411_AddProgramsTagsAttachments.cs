using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace KanbanBoard.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProgramsTagsAttachments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ProgramId",
                table: "WorkItem",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Attachment",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Length = table.Column<long>(type: "INTEGER", nullable: false),
                    Content = table.Column<byte[]>(type: "BLOB", nullable: false),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UploadedBy = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Attachment", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Program",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Program", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tag",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tag", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WorkItemTag",
                columns: table => new
                {
                    WorkItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    TagId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkItemTag", x => new { x.WorkItemId, x.TagId });
                    table.ForeignKey(
                        name: "FK_WorkItemTag_Tag_TagId",
                        column: x => x.TagId,
                        principalTable: "Tag",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_WorkItemTag_WorkItem_WorkItemId",
                        column: x => x.WorkItemId,
                        principalTable: "WorkItem",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "Program",
                columns: new[] { "Id", "CreatedAt", "Name", "NormalizedName", "SortOrder" },
                values: new object[,]
                {
                    { 1, new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Utc), "ProveOut", "PROVEOUT", 0 },
                    { 2, new DateTime(2026, 9, 29, 0, 0, 0, 0, DateTimeKind.Utc), "CallOut", "CALLOUT", 1 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItem_ProgramId",
                table: "WorkItem",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_Attachment_Sha256",
                table: "Attachment",
                column: "Sha256");

            migrationBuilder.CreateIndex(
                name: "IX_Program_NormalizedName",
                table: "Program",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tag_NormalizedName",
                table: "Tag",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WorkItemTag_TagId",
                table: "WorkItemTag",
                column: "TagId");

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItem_Program_ProgramId",
                table: "WorkItem",
                column: "ProgramId",
                principalTable: "Program",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItem_Program_ProgramId",
                table: "WorkItem");

            migrationBuilder.DropTable(
                name: "Attachment");

            migrationBuilder.DropTable(
                name: "Program");

            migrationBuilder.DropTable(
                name: "WorkItemTag");

            migrationBuilder.DropTable(
                name: "Tag");

            migrationBuilder.DropIndex(
                name: "IX_WorkItem_ProgramId",
                table: "WorkItem");

            migrationBuilder.DropColumn(
                name: "ProgramId",
                table: "WorkItem");
        }
    }
}
