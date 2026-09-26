using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AKingsDaysHoldTheProphetsTheTextSetsInThem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reign_statement",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    ruler_entity_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    counted_from = table.Column<string>(type: "text", nullable: true),
                    through_entity_id = table.Column<int>(type: "integer", nullable: true),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    end_verse = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reign_statement", x => x.id);
                    table.CheckConstraint("ck_reign_statement_counted_from", "counted_from IS NULL OR counted_from IN ('reign', 'captivity', 'death')");
                    table.CheckConstraint("ck_reign_statement_end_verse", "end_verse IS NULL OR end_verse > canonical_verse");
                    table.CheckConstraint("ck_reign_statement_kind", "kind IN ('superscription', 'dated', 'narrative', 'record', 'concerning')");
                    table.CheckConstraint("ck_reign_statement_role", "role IN ('prophet', 'nation', 'accession')");
                    table.CheckConstraint("ck_reign_statement_source_not_empty", "length(btrim(source)) > 0");
                    table.CheckConstraint("ck_reign_statement_year", "year IS NULL OR year >= 1");
                    table.ForeignKey(
                        name: "fk_reign_statement_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reign_statement_entity_ruler_entity_id",
                        column: x => x.ruler_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_reign_statement_entity_through_entity_id",
                        column: x => x.through_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ruler_reign",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    realm = table.Column<string>(type: "text", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    period_id = table.Column<int>(type: "integer", nullable: true),
                    drawn_under = table.Column<string>(type: "text", nullable: true),
                    shared = table.Column<bool>(type: "boolean", nullable: false),
                    fallback = table.Column<bool>(type: "boolean", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ruler_reign", x => x.id);
                    table.CheckConstraint("ck_ruler_reign_drawn_under", "drawn_under IS NULL OR drawn_under IN ('united', 'israel', 'judah', 'egypt', 'cush', 'aram', 'assyria', 'babylon', 'persia')");
                    table.CheckConstraint("ck_ruler_reign_realm", "realm IN ('united', 'israel', 'judah', 'egypt', 'cush', 'aram', 'assyria', 'babylon', 'persia')");
                    table.CheckConstraint("ck_ruler_reign_source_not_empty", "length(btrim(source)) > 0");
                    table.ForeignKey(
                        name: "fk_ruler_reign_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_ruler_reign_period_period_id",
                        column: x => x.period_id,
                        principalTable: "period",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reign_statement_entity_id",
                table: "reign_statement",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_reign_statement_ruler_entity_id",
                table: "reign_statement",
                column: "ruler_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_reign_statement_through_entity_id",
                table: "reign_statement",
                column: "through_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_ruler_reign_entity_id_period_id",
                table: "ruler_reign",
                columns: new[] { "entity_id", "period_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ruler_reign_period_id",
                table: "ruler_reign",
                column: "period_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reign_statement");

            migrationBuilder.DropTable(
                name: "ruler_reign");
        }
    }
}
