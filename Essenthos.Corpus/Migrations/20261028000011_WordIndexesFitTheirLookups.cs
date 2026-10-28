using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class WordIndexesFitTheirLookups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_word_strong_word_id",
                table: "word_strong");

            migrationBuilder.DropIndex(
                name: "ix_word_parsing_word_id",
                table: "word_parsing");

            migrationBuilder.DropIndex(
                name: "ix_word_entity_claim_word_entity_id",
                table: "word_entity_claim");

            migrationBuilder.DropIndex(
                name: "ix_word_entity_word_id",
                table: "word_entity");

            migrationBuilder.DropIndex(
                name: "ix_word_strong_number",
                table: "word");

            migrationBuilder.DropIndex(
                name: "ix_word_text_id_graphical_text",
                table: "word");

            migrationBuilder.DropIndex(
                name: "ix_strong_profile_strong_number",
                table: "strong_profile");

            migrationBuilder.RenameIndex(
                name: "ix_word_strong_number1",
                table: "word_strong",
                newName: "ix_word_strong_number");

            migrationBuilder.CreateIndex(
                name: "ix_word_text_id_graphical_text",
                table: "word",
                columns: new[] { "text_id", "graphical_text" },
                filter: "graphical_text IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_verse_reference_verse_id",
                table: "verse_reference",
                column: "verse_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_word_text_id_graphical_text",
                table: "word");

            migrationBuilder.DropIndex(
                name: "ix_verse_reference_verse_id",
                table: "verse_reference");

            migrationBuilder.RenameIndex(
                name: "ix_word_strong_number",
                table: "word_strong",
                newName: "ix_word_strong_number1");

            migrationBuilder.CreateIndex(
                name: "ix_word_strong_word_id",
                table: "word_strong",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_parsing_word_id",
                table: "word_parsing",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_claim_word_entity_id",
                table: "word_entity_claim",
                column: "word_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_word_id",
                table: "word_entity",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_strong_number",
                table: "word",
                column: "strong_number");

            migrationBuilder.CreateIndex(
                name: "ix_word_text_id_graphical_text",
                table: "word",
                columns: new[] { "text_id", "graphical_text" });

            migrationBuilder.CreateIndex(
                name: "ix_strong_profile_strong_number",
                table: "strong_profile",
                column: "strong_number",
                unique: true);
        }
    }
}
