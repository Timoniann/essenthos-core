using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class APaneHoldsEachTextsSideOfAChapterAheadOfTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "pane",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    body = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pane", x => new { x.text_id, x.canonical_book, x.canonical_chapter });
                    table.ForeignKey(
                        name: "fk_pane_texts_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Each text's side of the parallel view for one canonical chapter: the words with what the reader shows on them, the verses and notes, the records named, as gzip-compressed JSON the API sends as it is. Derived from the served tables by the build's pane producer (build/products, panes/{text}).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "pane");
        }
    }
}
