using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachPairsVerseStrengthIsCountedAtBuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "verse_strength",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    reference_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    by_reading = table.Column<bool>(type: "boolean", nullable: false),
                    verses = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verse_strength", x => new { x.text_id, x.reference_id, x.canonical_book, x.canonical_chapter });
                    table.CheckConstraint("ck_verse_strength_listed", "verses <> '' OR by_reading");
                    table.ForeignKey(
                        name: "fk_verse_strength_text_reference_id",
                        column: x => x.reference_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_verse_strength_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "How strongly a text's verses of a canonical chapter are linked to a reference text's: per verse the links of the pair naming its words, the stated ones and the mean confidence; and whether only verse links a reading drew join them there. Derived from the served tables by the build's strength producer (build/products, strength/{text}).");

            migrationBuilder.CreateIndex(
                name: "ix_verse_strength_reference_id",
                table: "verse_strength",
                column: "reference_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "verse_strength");
        }
    }
}
