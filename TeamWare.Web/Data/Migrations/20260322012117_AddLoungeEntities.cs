using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamWare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLoungeEntities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LoungeMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    Content = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsEdited = table.Column<bool>(type: "INTEGER", nullable: false),
                    EditedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsPinned = table.Column<bool>(type: "INTEGER", nullable: false),
                    PinnedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    PinnedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    CreatedTaskId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoungeMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoungeMessages_AspNetUsers_PinnedByUserId",
                        column: x => x.PinnedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_LoungeMessages_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoungeMessages_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_LoungeMessages_TaskItems_CreatedTaskId",
                        column: x => x.CreatedTaskId,
                        principalTable: "TaskItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "LoungeReactions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LoungeMessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ReactionType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoungeReactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoungeReactions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoungeReactions_LoungeMessages_LoungeMessageId",
                        column: x => x.LoungeMessageId,
                        principalTable: "LoungeMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LoungeReadPositions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: true),
                    LastReadMessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LoungeReadPositions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LoungeReadPositions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoungeReadPositions_LoungeMessages_LastReadMessageId",
                        column: x => x.LastReadMessageId,
                        principalTable: "LoungeMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_LoungeReadPositions_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LoungeMessage_ProjectId_CreatedAt",
                table: "LoungeMessages",
                columns: new[] { "ProjectId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LoungeMessage_UserId",
                table: "LoungeMessages",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LoungeMessages_CreatedTaskId",
                table: "LoungeMessages",
                column: "CreatedTaskId");

            migrationBuilder.CreateIndex(
                name: "IX_LoungeMessages_PinnedByUserId",
                table: "LoungeMessages",
                column: "PinnedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_LoungeReaction_MessageId",
                table: "LoungeReactions",
                column: "LoungeMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_LoungeReaction_MessageId_UserId_Type",
                table: "LoungeReactions",
                columns: new[] { "LoungeMessageId", "UserId", "ReactionType" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoungeReactions_UserId",
                table: "LoungeReactions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_LoungeReadPosition_UserId_ProjectId",
                table: "LoungeReadPositions",
                columns: new[] { "UserId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LoungeReadPositions_LastReadMessageId",
                table: "LoungeReadPositions",
                column: "LastReadMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_LoungeReadPositions_ProjectId",
                table: "LoungeReadPositions",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LoungeReactions");

            migrationBuilder.DropTable(
                name: "LoungeReadPositions");

            migrationBuilder.DropTable(
                name: "LoungeMessages");
        }
    }
}
