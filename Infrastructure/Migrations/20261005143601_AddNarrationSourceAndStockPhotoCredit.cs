using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddNarrationSourceAndStockPhotoCredit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "NarrationSource",
                table: "UserMemories",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StockPhotoCredit",
                table: "UserMemories",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NarrationSource",
                table: "UserMemories");

            migrationBuilder.DropColumn(
                name: "StockPhotoCredit",
                table: "UserMemories");
        }
    }
}
