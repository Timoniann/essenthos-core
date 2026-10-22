using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class SearchMatchesFormsOfAWord : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "text_form",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    form = table.Column<string>(type: "text", nullable: false),
                    stem = table.Column<string>(type: "text", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_text_form", x => new { x.text_id, x.form });
                    table.CheckConstraint("ck_text_form_occurrences", "occurrences > 0");
                    table.ForeignKey(
                        name: "fk_text_form_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Every word form a text prints, as a search compares it, with its stem and how often the text prints it: a typed word is matched through its stem's forms, and a missing one is answered with a near spelling. Derived from word; rebuilt by the load's search-forms step.");

            migrationBuilder.CreateIndex(
                name: "ix_text_form_form_trigrams",
                table: "text_form",
                column: "form")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_text_form_text_id_stem",
                table: "text_form",
                columns: new[] { "text_id", "stem" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "text_form");
        }
    }
}
