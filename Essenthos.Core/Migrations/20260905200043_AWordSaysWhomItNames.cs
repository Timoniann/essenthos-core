using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AWordSaysWhomItNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "word_entity",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    word_id = table.Column<long>(type: "bigint", nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_entity", x => x.id);
                    table.CheckConstraint("ck_word_entity_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_word_entity_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_word_entity_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_word_entity_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_word_entity_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_word_entity_word_word_id",
                        column: x => x.word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "word_entity_claim",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    word_entity_id = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_entity_claim", x => x.id);
                    table.CheckConstraint("ck_word_entity_claim_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_word_entity_claim_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_word_entity_claim_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_word_entity_claim_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_word_entity_claim_word_entity_word_entity_id",
                        column: x => x.word_entity_id,
                        principalTable: "word_entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_entity_id",
                table: "word_entity",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_word_id",
                table: "word_entity",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_word_id_entity_id",
                table: "word_entity",
                columns: new[] { "word_id", "entity_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_claim_word_entity_id",
                table: "word_entity_claim",
                column: "word_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_entity_claim_word_entity_id_method_source",
                table: "word_entity_claim",
                columns: new[] { "word_entity_id", "method", "source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "word_entity_claim");

            migrationBuilder.DropTable(
                name: "word_entity");
        }
    }
}
