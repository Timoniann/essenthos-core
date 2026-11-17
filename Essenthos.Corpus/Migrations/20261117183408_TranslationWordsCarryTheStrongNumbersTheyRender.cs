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
                comment: "A Strong number a word carries, or renders through a rendering link to the edition of an original its text is counted over (texts/<SLUG>/index/strong): what a list of verses marks for a number. Written at build; it asserts nothing the words and the links do not.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "word_rendered_strong");
        }
    }
}
