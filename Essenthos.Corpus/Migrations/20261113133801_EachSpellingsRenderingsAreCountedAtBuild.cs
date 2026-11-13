using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachSpellingsRenderingsAreCountedAtBuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "word_rendering",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    form = table.Column<string>(type: "text", nullable: false),
                    witness_id = table.Column<int>(type: "integer", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: true),
                    lemma = table.Column<string>(type: "text", nullable: true),
                    occurrences = table.Column<int>(type: "integer", nullable: false),
                    links = table.Column<int>(type: "integer", nullable: false),
                    methods = table.Column<string>(type: "text", nullable: false),
                    shared = table.Column<string>(type: "text", nullable: true),
                    places = table.Column<int>(type: "integer", nullable: false),
                    samples = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_rendering", x => x.id);
                    table.CheckConstraint("ck_word_rendering_counts", "occurrences > 0 AND links > 0 AND places > 0 AND places <= occurrences");
                    table.ForeignKey(
                        name: "fk_word_rendering_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_word_rendering_text_witness_id",
                        column: x => x.witness_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "One original standing behind one spelling of a text in one edition: the words it stands behind, the links, their methods and the first places. Counted at build from the links (texts/<SLUG>/index/renderings); it asserts nothing the links do not.");

            migrationBuilder.CreateTable(
                name: "word_rendering_source",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_rendering_source", x => new { x.text_id, x.number });
                    table.ForeignKey(
                        name: "fk_word_rendering_source_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "The sources a text's word_rendering_witness rows name by number.");

            migrationBuilder.CreateTable(
                name: "word_rendering_witness",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    form = table.Column<string>(type: "text", nullable: false),
                    witness_id = table.Column<int>(type: "integer", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false),
                    reached = table.Column<int>(type: "integer", nullable: false),
                    links = table.Column<int>(type: "integer", nullable: false),
                    methods = table.Column<string>(type: "text", nullable: false),
                    shared = table.Column<string>(type: "text", nullable: true),
                    sources = table.Column<int[]>(type: "integer[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_rendering_witness", x => new { x.text_id, x.form, x.witness_id });
                    table.CheckConstraint("ck_word_rendering_witness_counts", "occurrences >= 0 AND reached >= 0 AND links >= 0");
                    table.ForeignKey(
                        name: "fk_word_rendering_witness_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_word_rendering_witness_text_witness_id",
                        column: x => x.witness_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "One spelling of a text as one edition is counted against it: its words in the books the edition holds, how many some link reaches, the links, their methods and sources. Counted at build beside word_rendering (texts/<SLUG>/index/rendered).");

            migrationBuilder.CreateTable(
                name: "word_rendering_place",
                columns: table => new
                {
                    rendering_id = table.Column<long>(type: "bigint", nullable: false),
                    places = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_rendering_place", x => x.rendering_id);
                    table.ForeignKey(
                        name: "fk_word_rendering_place_word_rendering_rendering_id",
                        column: x => x.rendering_id,
                        principalTable: "word_rendering",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Every place of a word_rendering row that has more than its samples, read when a reader asks for more.");

            migrationBuilder.CreateIndex(
                name: "ix_word_rendering_text_id_form_witness_id",
                table: "word_rendering",
                columns: new[] { "text_id", "form", "witness_id" });

            migrationBuilder.CreateIndex(
                name: "ix_word_rendering_witness_id",
                table: "word_rendering",
                column: "witness_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_rendering_witness_witness_id",
                table: "word_rendering_witness",
                column: "witness_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "word_rendering_place");

            migrationBuilder.DropTable(
                name: "word_rendering_source");

            migrationBuilder.DropTable(
                name: "word_rendering_witness");

            migrationBuilder.DropTable(
                name: "word_rendering");
        }
    }
}
