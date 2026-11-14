using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachPhrasesPlacesAreListedAtBuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_rendering_place",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    witness_id = table.Column<int>(type: "integer", nullable: false),
                    phrases = table.Column<string[]>(type: "text[]", nullable: false),
                    words = table.Column<long[]>(type: "bigint[]", nullable: false),
                    phrase_of = table.Column<int[]>(type: "integer[]", nullable: false),
                    book_of = table.Column<short[]>(type: "smallint[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_rendering_place", x => new { x.text_id, x.strong_number, x.witness_id });
                    table.CheckConstraint("ck_strong_rendering_place_lengths", "cardinality(words) > 0 AND cardinality(phrase_of) = cardinality(words) AND cardinality(book_of) = cardinality(words)");
                    table.ForeignKey(
                        name: "fk_strong_rendering_place_texts_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_strong_rendering_place_texts_witness_id",
                        column: x => x.witness_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Every word of one edition carrying a Strong number that a text renders, with the phrase it is rendered by, in the order the number's places are listed: the places behind strong_rendering. Counted at build (texts/<SLUG>/index/strong); it asserts nothing the links do not.");

            migrationBuilder.CreateIndex(
                name: "ix_strong_rendering_place_witness_id",
                table: "strong_rendering_place",
                column: "witness_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_rendering_place");
        }
    }
}
