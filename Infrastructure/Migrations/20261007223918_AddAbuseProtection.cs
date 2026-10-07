using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAbuseProtection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanonicalEmail",
                table: "Users",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "EmailVerified",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SignupIpHash",
                table: "Users",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EmailVerificationTokens",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Used = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EmailVerificationTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EmailVerificationTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "UserId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VideoRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    IpHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoRequests", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Users_CanonicalEmail",
                table: "Users",
                column: "CanonicalEmail");

            migrationBuilder.CreateIndex(
                name: "IX_Users_SignupIpHash_CreatedDate",
                table: "Users",
                columns: new[] { "SignupIpHash", "CreatedDate" });

            migrationBuilder.CreateIndex(
                name: "IX_EmailVerificationTokens_UserId",
                table: "EmailVerificationTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_VideoRequests_CreatedAt",
                table: "VideoRequests",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_VideoRequests_IpHash_CreatedAt",
                table: "VideoRequests",
                columns: new[] { "IpHash", "CreatedAt" });

            // Accounts made before verification existed keep working.
            migrationBuilder.Sql(@"UPDATE ""Users"" SET ""EmailVerified"" = TRUE;");

            // The same rules as EmailCanonicalizer: drop a "+tag", and for Gmail drop dots too.
            migrationBuilder.Sql(@"
UPDATE ""Users"" SET ""CanonicalEmail"" = CASE
    WHEN split_part(lower(""Email""), '@', 2) IN ('gmail.com', 'googlemail.com')
        THEN replace(split_part(split_part(lower(""Email""), '@', 1), '+', 1), '.', '') || '@gmail.com'
    ELSE split_part(split_part(lower(""Email""), '@', 1), '+', 1) || '@' || split_part(lower(""Email""), '@', 2)
END;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EmailVerificationTokens");

            migrationBuilder.DropTable(
                name: "VideoRequests");

            migrationBuilder.DropIndex(
                name: "IX_Users_CanonicalEmail",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_Users_SignupIpHash_CreatedDate",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "CanonicalEmail",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "EmailVerified",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SignupIpHash",
                table: "Users");
        }
    }
}
