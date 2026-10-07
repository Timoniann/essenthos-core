using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class WhereEachStrongNumberStandsBookByBook : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_book",
                columns: table => new
                {
                    witness_id = table.Column<int>(type: "integer", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    book = table.Column<int>(type: "integer", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_book", x => new { x.witness_id, x.strong_number, x.book });
                    table.CheckConstraint("ck_strong_book_occurrences", "occurrences > 0");
                    table.ForeignKey(
                        name: "fk_strong_book_texts_witness_id",
                        column: x => x.witness_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "How often a Strong number stands in one book of an edition of the original, by canonical ordinal. Derived from the words and rebuilt by the load's count of the lexicon's phrases.");

            migrationBuilder.CreateTable(
                name: "strong_book_reach",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    book = table.Column<int>(type: "integer", nullable: false),
                    reached = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_book_reach", x => new { x.text_id, x.strong_number, x.book });
                    table.CheckConstraint("ck_strong_book_reach_reached", "reached > 0");
                    table.ForeignKey(
                        name: "fk_strong_book_reach_texts_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "How many of a Strong number's words in one book of the edition a text is counted over the text's links render. Only books where it renders some; derived and rebuilt with strong_reach.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_book");

            migrationBuilder.DropTable(
                name: "strong_book_reach");
        }
    }
}
