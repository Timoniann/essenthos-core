using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheChapterContextReadsATranslationsExceptionsFromAnIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_word_entity_word_id_exception",
                table: "word_entity",
                column: "word_id",
                filter: "origin IN ('added', 'differs')")
                .Annotation("Npgsql:IndexInclude", new[] { "entity_id", "origin" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_word_entity_word_id_exception",
                table: "word_entity");
        }
    }
}
