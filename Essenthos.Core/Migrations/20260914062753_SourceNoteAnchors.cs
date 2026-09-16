using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class SourceNoteAnchors : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "anchor_word_id",
                table: "verse_note",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_verse_note_anchor_word_id",
                table: "verse_note",
                column: "anchor_word_id");

            migrationBuilder.AddForeignKey(
                name: "fk_verse_note_words_anchor_word_id",
                table: "verse_note",
                column: "anchor_word_id",
                principalTable: "word",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_verse_note_words_anchor_word_id",
                table: "verse_note");

            migrationBuilder.DropIndex(
                name: "ix_verse_note_anchor_word_id",
                table: "verse_note");

            migrationBuilder.DropColumn(
                name: "anchor_word_id",
                table: "verse_note");
        }
    }
}
