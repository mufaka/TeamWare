using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamWare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCodexAgentFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AgentBackend",
                table: "AgentConfigurations",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CodexTokenExpiresAt",
                table: "AgentConfigurations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CodexTokenLastRefreshed",
                table: "AgentConfigurations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedCodexApiKey",
                table: "AgentConfigurations",
                type: "TEXT",
                maxLength: 2000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedCodexAuthData",
                table: "AgentConfigurations",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AgentBackend",
                table: "AgentConfigurations");

            migrationBuilder.DropColumn(
                name: "CodexTokenExpiresAt",
                table: "AgentConfigurations");

            migrationBuilder.DropColumn(
                name: "CodexTokenLastRefreshed",
                table: "AgentConfigurations");

            migrationBuilder.DropColumn(
                name: "EncryptedCodexApiKey",
                table: "AgentConfigurations");

            migrationBuilder.DropColumn(
                name: "EncryptedCodexAuthData",
                table: "AgentConfigurations");
        }
    }
}
