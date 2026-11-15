using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachChaptersContextIsStoredAtBuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "context_chapter",
                columns: table => new
                {
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false)
                        .Annotation("Npgsql:Compression:", "lz4"),
                    hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_context_chapter", x => new { x.canonical_book, x.canonical_chapter });
                },
                comment: "What a canonical chapter's context is made of whatever the reader's languages: the records it names and how they reach it, its events, periods, measures, commandments, topics and family trees, as one JSON document. Made by the serving build (context/chapters).");

            migrationBuilder.CreateTable(
                name: "context_line",
                columns: table => new
                {
                    prose = table.Column<string>(type: "text", nullable: false),
                    names = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false)
                        .Annotation("Npgsql:Compression:", "lz4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_context_line", x => new { x.prose, x.names, x.slug });
                },
                comment: "One record as a chapter's context shows it in a language of words and a language of names: its local name, our own line, and the line's claims with the names they point at, as one JSON document. Records by slug. Made by the serving build (context/lines/{prose}).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "context_chapter");

            migrationBuilder.DropTable(
                name: "context_line");
        }
    }
}
