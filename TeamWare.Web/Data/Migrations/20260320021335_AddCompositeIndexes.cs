using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TeamWare.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCompositeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TaskItems_ProjectId_Status",
                table: "TaskItems",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead" });

            migrationBuilder.CreateIndex(
                name: "IX_InboxItems_UserId_Status",
                table: "InboxItems",
                columns: new[] { "UserId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityLogEntries_ProjectId_CreatedAt",
                table: "ActivityLogEntries",
                columns: new[] { "ProjectId", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TaskItems_ProjectId_Status",
                table: "TaskItems");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_UserId_IsRead",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_InboxItems_UserId_Status",
                table: "InboxItems");

            migrationBuilder.DropIndex(
                name: "IX_ActivityLogEntries_ProjectId_CreatedAt",
                table: "ActivityLogEntries");
        }
    }
}
