using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamWare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveCodexTier2Fields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CodexTokenExpiresAt",
                table: "AgentConfigurations");

            migrationBuilder.DropColumn(
                name: "CodexTokenLastRefreshed",
                table: "AgentConfigurations");

            migrationBuilder.DropColumn(
                name: "EncryptedCodexAuthData",
                table: "AgentConfigurations");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "EncryptedCodexAuthData",
                table: "AgentConfigurations",
                type: "TEXT",
                nullable: true);
        }
    }
}
