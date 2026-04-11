using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamWare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWhiteboard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Whiteboards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    OwnerId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    CurrentPresenterId = table.Column<string>(type: "TEXT", nullable: true),
                    CanvasData = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Whiteboards", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Whiteboards_AspNetUsers_CurrentPresenterId",
                        column: x => x.CurrentPresenterId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Whiteboards_AspNetUsers_OwnerId",
                        column: x => x.OwnerId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Whiteboards_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WhiteboardChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WhiteboardId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhiteboardChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhiteboardChatMessages_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhiteboardChatMessages_Whiteboards_WhiteboardId",
                        column: x => x.WhiteboardId,
                        principalTable: "Whiteboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "WhiteboardInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WhiteboardId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    InvitedByUserId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhiteboardInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WhiteboardInvitations_AspNetUsers_InvitedByUserId",
                        column: x => x.InvitedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhiteboardInvitations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_WhiteboardInvitations_Whiteboards_WhiteboardId",
                        column: x => x.WhiteboardId,
                        principalTable: "Whiteboards",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhiteboardChatMessages_UserId",
                table: "WhiteboardChatMessages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WhiteboardChatMessages_WhiteboardId_CreatedAt",
                table: "WhiteboardChatMessages",
                columns: new[] { "WhiteboardId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WhiteboardInvitations_InvitedByUserId",
                table: "WhiteboardInvitations",
                column: "InvitedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_WhiteboardInvitations_UserId",
                table: "WhiteboardInvitations",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_WhiteboardInvitations_WhiteboardId",
                table: "WhiteboardInvitations",
                column: "WhiteboardId");

            migrationBuilder.CreateIndex(
                name: "IX_WhiteboardInvitations_WhiteboardId_UserId",
                table: "WhiteboardInvitations",
                columns: new[] { "WhiteboardId", "UserId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Whiteboards_CurrentPresenterId",
                table: "Whiteboards",
                column: "CurrentPresenterId");

            migrationBuilder.CreateIndex(
                name: "IX_Whiteboards_OwnerId",
                table: "Whiteboards",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_Whiteboards_ProjectId",
                table: "Whiteboards",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhiteboardChatMessages");

            migrationBuilder.DropTable(
                name: "WhiteboardInvitations");

            migrationBuilder.DropTable(
                name: "Whiteboards");
        }
    }
}
