using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AVerseSendsTheReaderToOthers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cross_reference",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    set = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    book = table.Column<int>(type: "integer", nullable: false),
                    chapter = table.Column<int>(type: "integer", nullable: false),
                    verse = table.Column<int>(type: "integer", nullable: false),
                    to_book = table.Column<int>(type: "integer", nullable: false),
                    to_chapter = table.Column<int>(type: "integer", nullable: false),
                    to_verse = table.Column<int>(type: "integer", nullable: false),
                    to_end_book = table.Column<int>(type: "integer", nullable: true),
                    to_end_chapter = table.Column<int>(type: "integer", nullable: true),
                    to_end_verse = table.Column<int>(type: "integer", nullable: true),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    votes = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    passage = table.Column<int>(type: "integer", nullable: true),
                    matched_in = table.Column<string>(type: "text", nullable: true),
                    matched = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cross_reference", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_cross_reference_set_book_chapter_verse",
                table: "cross_reference",
                columns: new[] { "set", "book", "chapter", "verse" });

            migrationBuilder.CreateIndex(
                name: "ix_cross_reference_set_passage",
                table: "cross_reference",
                columns: new[] { "set", "passage" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cross_reference");
        }
    }
}
