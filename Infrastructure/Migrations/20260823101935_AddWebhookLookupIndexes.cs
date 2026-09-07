using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWebhookLookupIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Users_StripeCustomerId",
                table: "Users",
                column: "StripeCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_StripeSubscriptionId",
                table: "Users",
                column: "StripeSubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_UserMemories_Status",
                table: "UserMemories",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_StripeCustomerId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_StripeSubscriptionId",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_UserMemories_Status",
                table: "UserMemories");
        }
    }
}
