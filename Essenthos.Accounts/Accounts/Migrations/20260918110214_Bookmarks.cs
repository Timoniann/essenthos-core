using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Accounts.Migrations
{
    /// <inheritdoc />
    public partial class Bookmarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Notes became bookmarks: a colour and an optional comment. What a reader already wrote is
            // carried over as the comment of an amber bookmark, before the old table goes.
            migrationBuilder.CreateTable(
                name: "bookmark",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    text = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    book = table.Column<int>(type: "integer", nullable: false),
                    chapter = table.Column<int>(type: "integer", nullable: false),
                    verse = table.Column<int>(type: "integer", nullable: false),
                    end_chapter = table.Column<int>(type: "integer", nullable: false),
                    end_verse = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    comment = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_bookmark", x => x.id);
                    table.ForeignKey(
                        name: "fk_bookmark_account_account_id",
                        column: x => x.account_id,
                        principalTable: "account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_bookmark_account_id_book_chapter",
                table: "bookmark",
                columns: new[] { "account_id", "book", "chapter" });

            migrationBuilder.CreateIndex(
                name: "ix_bookmark_revision",
                table: "bookmark",
                column: "revision");

            migrationBuilder.Sql(
                "INSERT INTO bookmark (id, account_id, text, book, chapter, verse, end_chapter, end_verse, color, comment, created_at, updated_at, revision) " +
                "SELECT id, account_id, text, book, chapter, verse, end_chapter, end_verse, 'amber', body, created_at, updated_at, revision FROM note");

            migrationBuilder.DropTable(
                name: "note");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "note",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    account_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    book = table.Column<int>(type: "integer", nullable: false),
                    chapter = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    end_chapter = table.Column<int>(type: "integer", nullable: false),
                    end_verse = table.Column<int>(type: "integer", nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    text = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    verse = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_note", x => x.id);
                    table.ForeignKey(
                        name: "fk_note_account_account_id",
                        column: x => x.account_id,
                        principalTable: "account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_note_account_id_book_chapter",
                table: "note",
                columns: new[] { "account_id", "book", "chapter" });

            migrationBuilder.CreateIndex(
                name: "ix_note_revision",
                table: "note",
                column: "revision");

            // A bookmark without a comment has nothing to be a note of, and is not carried back.
            migrationBuilder.Sql(
                "INSERT INTO note (id, account_id, text, book, chapter, verse, end_chapter, end_verse, body, created_at, updated_at, revision) " +
                "SELECT id, account_id, text, book, chapter, verse, end_chapter, end_verse, comment, created_at, updated_at, revision FROM bookmark WHERE comment IS NOT NULL");

            migrationBuilder.DropTable(
                name: "bookmark");
        }
    }
}
