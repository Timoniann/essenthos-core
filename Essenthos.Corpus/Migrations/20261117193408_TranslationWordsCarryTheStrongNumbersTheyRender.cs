using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TranslationWordsCarryTheStrongNumbersTheyRender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "word_rendered_strong",
                columns: table => new
                {
                    verse_id = table.Column<int>(type: "integer", nullable: false),
                    word_id = table.Column<long>(type: "bigint", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_rendered_strong", x => new { x.verse_id, x.word_id, x.strong_number });
                },
                comment: "A Strong number a list of verses marks on a word: its own, and the number of each word of an edition of an original that a renders or equals link joins it to, on every word of a translation and on a word of an original that carries none. Written at build from the words and the links; it asserts nothing they do not.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "word_rendered_strong");
        }
    }
}
