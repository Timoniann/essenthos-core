using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AKingCarriesTheTextsJudgmentTheAgeItGivesAndTheCarryingsAway : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reign_event",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    realm = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    timeline_event_id = table.Column<int>(type: "integer", nullable: true),
                    ruler_entity_id = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    end_verse = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reign_event", x => x.id);
                    table.CheckConstraint("ck_reign_event_end_verse", "end_verse IS NULL OR end_verse > canonical_verse");
                    table.CheckConstraint("ck_reign_event_kind", "kind IN ('exile', 'return')");
                    table.CheckConstraint("ck_reign_event_realm", "realm IN ('israel', 'judah')");
                    table.CheckConstraint("ck_reign_event_slug_not_empty", "length(btrim(slug)) > 0");
                    table.CheckConstraint("ck_reign_event_source_not_empty", "length(btrim(source)) > 0");
                    table.CheckConstraint("ck_reign_event_year", "year IS NULL OR year >= 1");
                    table.ForeignKey(
                        name: "fk_reign_event_entity_ruler_entity_id",
                        column: x => x.ruler_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reign_event_event_timeline_event_id",
                        column: x => x.timeline_event_id,
                        principalTable: "event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ruler_verdict",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    mark = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ruler_verdict", x => x.id);
                    table.CheckConstraint("ck_ruler_verdict_mark", "mark IN ('right', 'evil', 'mixed')");
                    table.CheckConstraint("ck_ruler_verdict_source_not_empty", "length(btrim(source)) > 0");
                    table.ForeignKey(
                        name: "fk_ruler_verdict_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "stated_age",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    years = table.Column<int>(type: "integer", nullable: false),
                    about = table.Column<bool>(type: "boolean", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stated_age", x => x.id);
                    table.CheckConstraint("ck_stated_age_kind", "kind IN ('accession', 'death')");
                    table.CheckConstraint("ck_stated_age_source_not_empty", "length(btrim(source)) > 0");
                    table.CheckConstraint("ck_stated_age_years", "years >= 1");
                    table.ForeignKey(
                        name: "fk_stated_age_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ruler_verdict_witness",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    verdict_id = table.Column<int>(type: "integer", nullable: false),
                    witness = table.Column<string>(type: "text", nullable: false),
                    mark = table.Column<string>(type: "text", nullable: false),
                    basis = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ruler_verdict_witness", x => x.id);
                    table.CheckConstraint("ck_ruler_verdict_witness_basis", "basis IN ('text', 'reading')");
                    table.CheckConstraint("ck_ruler_verdict_witness_mark", "mark IN ('right', 'evil', 'mixed')");
                    table.CheckConstraint("ck_ruler_verdict_witness_witness", "witness IN ('samuel', 'kings', 'chronicles')");
                    table.ForeignKey(
                        name: "fk_ruler_verdict_witness_ruler_verdict_verdict_id",
                        column: x => x.verdict_id,
                        principalTable: "ruler_verdict",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ruler_verdict_passage",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    witness_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    end_verse = table.Column<int>(type: "integer", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ruler_verdict_passage", x => x.id);
                    table.CheckConstraint("ck_ruler_verdict_passage_end_verse", "end_verse IS NULL OR end_verse > canonical_verse");
                    table.ForeignKey(
                        name: "fk_ruler_verdict_passage_ruler_verdict_witnesses_witness_id",
                        column: x => x.witness_id,
                        principalTable: "ruler_verdict_witness",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reign_event_ruler_entity_id",
                table: "reign_event",
                column: "ruler_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_reign_event_slug",
                table: "reign_event",
                column: "slug");

            migrationBuilder.CreateIndex(
                name: "ix_reign_event_timeline_event_id",
                table: "reign_event",
                column: "timeline_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_ruler_verdict_entity_id",
                table: "ruler_verdict",
                column: "entity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ruler_verdict_passage_witness_id",
                table: "ruler_verdict_passage",
                column: "witness_id");

            migrationBuilder.CreateIndex(
                name: "ix_ruler_verdict_witness_verdict_id_witness",
                table: "ruler_verdict_witness",
                columns: new[] { "verdict_id", "witness" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stated_age_entity_id",
                table: "stated_age",
                column: "entity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reign_event");

            migrationBuilder.DropTable(
                name: "ruler_verdict_passage");

            migrationBuilder.DropTable(
                name: "stated_age");

            migrationBuilder.DropTable(
                name: "ruler_verdict_witness");

            migrationBuilder.DropTable(
                name: "ruler_verdict");
        }
    }
}
