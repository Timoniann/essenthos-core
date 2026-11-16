using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ACommentIsWrittenOnAPassage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commentary_entry",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    commentary = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    book = table.Column<int>(type: "integer", nullable: false),
                    first_chapter = table.Column<int>(type: "integer", nullable: true),
                    first_verse = table.Column<int>(type: "integer", nullable: true),
                    last_chapter = table.Column<int>(type: "integer", nullable: true),
                    last_verse = table.Column<int>(type: "integer", nullable: true),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commentary_entry", x => x.id);
                    table.CheckConstraint("ck_commentary_entry_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_commentary_entry_span", "(first_chapter IS NULL) = (last_chapter IS NULL) AND (first_verse IS NULL) = (last_verse IS NULL) AND (first_verse IS NULL OR first_chapter IS NOT NULL)");
                },
                comment: "What a commentary wrote, one row per comment over the span it was written on, in the shared frame: a comment on verses has all four ends, a chapter's introduction its chapter and no verse, a book's introduction neither. The body is the comment as JSON blocks of marked text.");

            migrationBuilder.CreateIndex(
                name: "ix_commentary_entry_commentary_book_first_chapter_first_verse",
                table: "commentary_entry",
                columns: new[] { "commentary", "book", "first_chapter", "first_verse" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commentary_entry");
        }
    }
}
