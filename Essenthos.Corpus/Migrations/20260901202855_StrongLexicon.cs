using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class StrongLexicon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_entries",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    lemma = table.Column<string>(type: "text", nullable: true),
                    transliteration = table.Column<string>(type: "text", nullable: true),
                    pronunciation = table.Column<string>(type: "text", nullable: true),
                    definition = table.Column<string>(type: "text", nullable: true),
                    derivation = table.Column<string>(type: "text", nullable: true),
                    kjv_definition = table.Column<string>(type: "text", nullable: true),
                    morphology = table.Column<string>(type: "text", nullable: true),
                    detailed_definition = table.Column<string>(type: "text", nullable: true),
                    see_also = table.Column<string>(type: "text", nullable: true),
                    source_language = table.Column<string>(type: "text", nullable: true),
                    twot_reference = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_entries", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_word_strong_number",
                table: "word",
                column: "strong_number");

            migrationBuilder.CreateIndex(
                name: "ix_strong_entries_strong_number",
                table: "strong_entries",
                column: "strong_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_entries");

            migrationBuilder.DropIndex(
                name: "ix_word_strong_number",
                table: "word");
        }
    }
}
