using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ThePrintedFormOfAWord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "graphical_text",
                table: "word",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_word_text_id_graphical_text",
                table: "word",
                columns: new[] { "text_id", "graphical_text" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_word_text_id_graphical_text",
                table: "word");

            migrationBuilder.DropColumn(
                name: "graphical_text",
                table: "word");
        }
    }
}
