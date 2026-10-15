using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AChildrensCountAndPlaceAsAVerseStatesThem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_kin_statement",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    family = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    sons = table.Column<int>(type: "integer", nullable: true),
                    daughters = table.Column<int>(type: "integer", nullable: true),
                    children = table.Column<int>(type: "integer", nullable: true),
                    grandsons = table.Column<int>(type: "integer", nullable: true),
                    more_sons = table.Column<bool>(type: "boolean", nullable: false),
                    more_daughters = table.Column<bool>(type: "boolean", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: true),
                    parent_entity_id = table.Column<int>(type: "integer", nullable: true),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    citation = table.Column<string>(type: "text", nullable: true),
                    witness = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    original = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_kin_statement", x => x.id);
                    table.CheckConstraint("ck_entity_kin_statement_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_entity_kin_statement_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_entity_kin_statement_kind", "\"kind\" IN ('count', 'more', 'order')");
                    table.CheckConstraint("ck_entity_kin_statement_order_names_a_parent", "\"kind\" <> 'order' OR (\"ordinal\" >= 1 AND \"parent_entity_id\" IS NOT NULL)");
                    table.CheckConstraint("ck_entity_kin_statement_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_entity_kin_statement_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_entity_kin_statement_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_entity_kin_statement_entity_parent_entity_id",
                        column: x => x.parent_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "What a verse says of a person's children beyond naming them: how many, that there were more than it names, or a child's place among them, each with the verse and the words of the original it rests on.");

            migrationBuilder.CreateIndex(
                name: "ix_entity_kin_statement_entity_id",
                table: "entity_kin_statement",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_kin_statement_parent_entity_id",
                table: "entity_kin_statement",
                column: "parent_entity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_kin_statement");
        }
    }
}
