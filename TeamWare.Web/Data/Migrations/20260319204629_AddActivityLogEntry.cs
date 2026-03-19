using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamWare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddActivityLogEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ActivityLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ChangeType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    NewValue = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityLogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityLogEntries_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityLogEntries_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ActivityLogEntries_TaskItems_TaskItemId",
                        column: x => x.TaskItemId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_CreatedAt",
                table: "ActivityLogEntries",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_ProjectId",
                table: "ActivityLogEntries",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_TaskItemId",
                table: "ActivityLogEntries",
                column: "TaskItemId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_UserId",
                table: "ActivityLogEntries",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityLogEntries");
        }
    }
}
