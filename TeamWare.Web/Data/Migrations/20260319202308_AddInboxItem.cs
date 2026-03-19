using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamWare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInboxItem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InboxItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", maxLength: 20, nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ConvertedToTaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InboxItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InboxItems_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InboxItems_TaskItems_ConvertedToTaskId",
                        column: x => x.ConvertedToTaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InboxItems_ConvertedToTaskId",
                table: "InboxItems",
                column: "ConvertedToTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_InboxItems_Status",
                table: "InboxItems",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_InboxItems_UserId",
                table: "InboxItems",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InboxItems");
        }
    }
}
