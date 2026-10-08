using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class TombstonesForTheChangeFeed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_favorite_text_account_id_text",
                table: "favorite_text");

            migrationBuilder.DropIndex(
                name: "ix_chapter_bookmark_account_id_book_chapter",
                table: "chapter_bookmark");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "favorite_text",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "device",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "chapter_bookmark",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "deleted_at",
                table: "bookmark",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_favorite_text_account_id_text",
                table: "favorite_text",
                columns: new[] { "account_id", "text" },
                unique: true,
                filter: "deleted_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_chapter_bookmark_account_id_book_chapter",
                table: "chapter_bookmark",
                columns: new[] { "account_id", "book", "chapter" },
                unique: true,
                filter: "deleted_at IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_favorite_text_account_id_text",
                table: "favorite_text");

            migrationBuilder.DropIndex(
                name: "ix_chapter_bookmark_account_id_book_chapter",
                table: "chapter_bookmark");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "favorite_text");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "device");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "chapter_bookmark");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "bookmark");

            migrationBuilder.CreateIndex(
                name: "ix_favorite_text_account_id_text",
                table: "favorite_text",
                columns: new[] { "account_id", "text" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chapter_bookmark_account_id_book_chapter",
                table: "chapter_bookmark",
                columns: new[] { "account_id", "book", "chapter" },
                unique: true);
        }
    }
}
