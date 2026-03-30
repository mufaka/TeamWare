using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamWare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAgentConfiguration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AgentConfigurations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    PollingIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    Model = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    AutoApproveTools = table.Column<bool>(type: "INTEGER", nullable: true),
                    DryRun = table.Column<bool>(type: "INTEGER", nullable: true),
                    TaskTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    SystemPrompt = table.Column<string>(type: "TEXT", maxLength: 10000, nullable: true),
                    RepositoryUrl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    RepositoryBranch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EncryptedRepositoryAccessToken = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentConfigurations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentConfigurations_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentMcpServers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgentConfigurationId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    EncryptedAuthHeader = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Command = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Args = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    EncryptedEnv = table.Column<string>(type: "TEXT", maxLength: 8000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentMcpServers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentMcpServers_AgentConfigurations_AgentConfigurationId",
                        column: x => x.AgentConfigurationId,
                        principalTable: "AgentConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AgentRepositories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AgentConfigurationId = table.Column<int>(type: "INTEGER", nullable: false),
                    ProjectName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    Branch = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    EncryptedAccessToken = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    DisplayOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AgentRepositories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AgentRepositories_AgentConfigurations_AgentConfigurationId",
                        column: x => x.AgentConfigurationId,
                        principalTable: "AgentConfigurations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AgentConfigurations_UserId",
                table: "AgentConfigurations",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AgentMcpServers_AgentConfigurationId",
                table: "AgentMcpServers",
                column: "AgentConfigurationId");

            migrationBuilder.CreateIndex(
                name: "IX_AgentRepositories_AgentConfigurationId_ProjectName",
                table: "AgentRepositories",
                columns: new[] { "AgentConfigurationId", "ProjectName" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AgentMcpServers");

            migrationBuilder.DropTable(
                name: "AgentRepositories");

            migrationBuilder.DropTable(
                name: "AgentConfigurations");
        }
    }
}
