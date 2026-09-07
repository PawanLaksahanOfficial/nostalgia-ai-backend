using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoMetadataAndShareLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CaptionsPath",
                table: "UserMemories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "UserMemories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "DurationSeconds",
                table: "UserMemories",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FailureReason",
                table: "UserMemories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "FileSizeBytes",
                table: "UserMemories",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MusicTrackName",
                table: "UserMemories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ProcessingStartedAt",
                table: "UserMemories",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ProcessingStep",
                table: "UserMemories",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailPath",
                table: "UserMemories",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "MemoryShareLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserMemoryId = table.Column<int>(type: "integer", nullable: false),
                    CreatedByUserId = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Label = table.Column<string>(type: "text", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                    ViewCount = table.Column<int>(type: "integer", nullable: false),
                    LastViewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MemoryShareLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MemoryShareLinks_UserMemories_UserMemoryId",
                        column: x => x.UserMemoryId,
                        principalTable: "UserMemories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MemoryShareLinks_Token",
                table: "MemoryShareLinks",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MemoryShareLinks_UserMemoryId",
                table: "MemoryShareLinks",
                column: "UserMemoryId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MemoryShareLinks");

            migrationBuilder.DropColumn(
                name: "CaptionsPath",
                table: "UserMemories");

            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "UserMemories");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "UserMemories");

            migrationBuilder.DropColumn(
                name: "FailureReason",
                table: "UserMemories");

            migrationBuilder.DropColumn(
                name: "FileSizeBytes",
                table: "UserMemories");

            migrationBuilder.DropColumn(
                name: "MusicTrackName",
                table: "UserMemories");

            migrationBuilder.DropColumn(
                name: "ProcessingStartedAt",
                table: "UserMemories");

            migrationBuilder.DropColumn(
                name: "ProcessingStep",
                table: "UserMemories");

            migrationBuilder.DropColumn(
                name: "ThumbnailPath",
                table: "UserMemories");
        }
    }
}
