using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheEncyclopediaSpeaksInOurOwnWords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "distinguisher",
                table: "entity",
                type: "text",
                nullable: true,
                comment: "The imported one-line description, in English, in the words of whichever dataset supplied it. It is no longer what a reader is shown — entity_descriptor is — and it is not dead: it is the record as imported, it is what the generated clauses are measured against, and it is the only description an entity nothing has been generated for has. Nothing writes it but the dataset loaders.",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "entity_descriptor",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    relation = table.Column<string>(type: "text", nullable: false),
                    target_entity_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_descriptor", x => x.id);
                    table.CheckConstraint("ck_entity_descriptor_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_entity_descriptor_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_entity_descriptor_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_entity_descriptor_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_entity_descriptor_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_entity_descriptor_entity_target_entity_id",
                        column: x => x.target_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "One clause of what this corpus says an entity is, in its own voice: a relation, an entity it holds, and the verse it was read from. The line a reader sees is rendered from these per language, so every name in it is a link. It replaces what entity.distinguisher was shown for; that column stays, unchanged and unread by this layer.");

            migrationBuilder.CreateTable(
                name: "entity_name_form",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    grammatical_case = table.Column<string>(type: "text", nullable: false),
                    form = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_name_form", x => x.id);
                    table.CheckConstraint("ck_entity_name_form_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_entity_name_form_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_entity_name_form_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_entity_name_form_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_entity_name_form_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "An entity's name in a reader's language, in the grammatical case a phrase puts it in. Produced with the name and never computed from it: a stemmer guessing the genitive of a Hebrew proper name is wrong often and silently.");

            migrationBuilder.CreateTable(
                name: "entity_descriptor_claim",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_descriptor_id = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_descriptor_claim", x => x.id);
                    table.CheckConstraint("ck_entity_descriptor_claim_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_entity_descriptor_claim_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_entity_descriptor_claim_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_entity_descriptor_claim_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_entity_descriptor_claim_entity_descriptor_entity_descriptor",
                        column: x => x.entity_descriptor_id,
                        principalTable: "entity_descriptor",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_descriptor_entity_id",
                table: "entity_descriptor",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_descriptor_entity_id_ordinal",
                table: "entity_descriptor",
                columns: new[] { "entity_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_descriptor_target_entity_id",
                table: "entity_descriptor",
                column: "target_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_descriptor_claim_entity_descriptor_id",
                table: "entity_descriptor_claim",
                column: "entity_descriptor_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_descriptor_claim_entity_descriptor_id_method_source",
                table: "entity_descriptor_claim",
                columns: new[] { "entity_descriptor_id", "method", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_name_form_entity_id",
                table: "entity_name_form",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_name_form_entity_id_language_grammatical_case",
                table: "entity_name_form",
                columns: new[] { "entity_id", "language", "grammatical_case" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_descriptor_claim");

            migrationBuilder.DropTable(
                name: "entity_name_form");

            migrationBuilder.DropTable(
                name: "entity_descriptor");

            migrationBuilder.AlterColumn<string>(
                name: "distinguisher",
                table: "entity",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldComment: "The imported one-line description, in English, in the words of whichever dataset supplied it. It is no longer what a reader is shown — entity_descriptor is — and it is not dead: it is the record as imported, it is what the generated clauses are measured against, and it is the only description an entity nothing has been generated for has. Nothing writes it but the dataset loaders.");
        }
    }
}
