using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamWare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentTaskAssignmentPermissions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentTaskAssignmentPermissions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgentUserId = table.Column<string>(type: "TEXT", nullable: false),
                    AllowedAssignerUserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentTaskAssignmentPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentTaskAssignmentPermissions_AspNetUsers_AgentUserId",
                        column: x => x.AgentUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AgentTaskAssignmentPermissions_AspNetUsers_AllowedAssignerUserId",
                        column: x => x.AllowedAssignerUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskAssignmentPermissions_AgentUserId_AllowedAssignerUserId",
                table: "AgentTaskAssignmentPermissions",
                columns: new[] { "AgentUserId", "AllowedAssignerUserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentTaskAssignmentPermissions_AllowedAssignerUserId",
                table: "AgentTaskAssignmentPermissions",
                column: "AllowedAssignerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentTaskAssignmentPermissions");
        }
    }
}
