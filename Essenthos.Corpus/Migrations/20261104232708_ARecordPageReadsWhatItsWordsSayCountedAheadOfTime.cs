using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ARecordPageReadsWhatItsWordsSayCountedAheadOfTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "record_position",
                columns: table => new
                {
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    book = table.Column<int>(type: "integer", nullable: false),
                    chapter = table.Column<int>(type: "integer", nullable: false),
                    verse = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_position", x => new { x.entity_id, x.text_id, x.book, x.chapter, x.verse });
                    table.ForeignKey(
                        name: "fk_record_position_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_record_position_texts_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Where a record's name first stands in a verse of a text (the least position of the words word_entity names it by there), for the text a record page orders a verse's names by. Derived from the served tables by the build's positions producer (build/products, record-positions/{text}).");

            migrationBuilder.CreateTable(
                name: "record_spelling",
                columns: table => new
                {
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    form = table.Column<string>(type: "text", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_spelling", x => new { x.entity_id, x.text_id, x.strong_number, x.form });
                    table.CheckConstraint("ck_record_spelling_occurrences", "occurrences > 0");
                    table.ForeignKey(
                        name: "fk_record_spelling_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_record_spelling_texts_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "How often a text prints a form for a record under a Strong number (the word's own, the original word's it was carried from, or its note's), from the words word_entity names the record by. Derived from the served tables by the build's spellings producer (build/products, record-spellings/{text}).");

            migrationBuilder.CreateIndex(
                name: "ix_record_position_text_id",
                table: "record_position",
                column: "text_id");

            migrationBuilder.CreateIndex(
                name: "ix_record_spelling_text_id",
                table: "record_spelling",
                column: "text_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "record_position");

            migrationBuilder.DropTable(
                name: "record_spelling");
        }
    }
}
