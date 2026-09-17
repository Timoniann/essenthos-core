using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class VerseLabel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_verse_text_id_book_id_chapter_number_number",
                table: "verse");

            migrationBuilder.AddColumn<string>(
                name: "label",
                table: "verse",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "ix_verse_text_id_book_id_chapter_number_number_label",
                table: "verse",
                columns: new[] { "text_id", "book_id", "chapter_number", "number", "label" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_verse_text_id_book_id_chapter_number_number_label",
                table: "verse");

            migrationBuilder.DropColumn(
                name: "label",
                table: "verse");

            migrationBuilder.CreateIndex(
                name: "ix_verse_text_id_book_id_chapter_number_number",
                table: "verse",
                columns: new[] { "text_id", "book_id", "chapter_number", "number" },
                unique: true);
        }
    }
}
