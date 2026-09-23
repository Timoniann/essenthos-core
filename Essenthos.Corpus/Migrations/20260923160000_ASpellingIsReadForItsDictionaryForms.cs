using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ASpellingIsReadForItsDictionaryForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_word_normalised_text_lemmatised",
                table: "word",
                column: "normalised_text",
                filter: "lemma IS NOT NULL")
                .Annotation("Npgsql:IndexInclude", new[] { "text", "lemma", "text_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_word_normalised_text_lemmatised",
                table: "word");
        }
    }
}
