using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AProphetStandsWhereHeSpokeAndAKingByTheYearsAndTheNameHeReignedUnder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "prophet_field",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    realm = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    place_entity_id = table.Column<int>(type: "integer", nullable: true),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    end_verse = table.Column<int>(type: "integer", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_prophet_field", x => x.id);
                    table.CheckConstraint("ck_prophet_field_end_verse", "end_verse IS NULL OR end_verse > canonical_verse");
                    table.CheckConstraint("ck_prophet_field_kind", "kind IN ('prophesied', 'from', 'sent')");
                    table.CheckConstraint("ck_prophet_field_realm", "realm IN ('united', 'israel', 'judah', 'exile', 'return', 'egypt', 'cush', 'aram', 'assyria', 'babylon', 'persia')");
                    table.CheckConstraint("ck_prophet_field_source_not_empty", "length(btrim(source)) > 0");
                    table.ForeignKey(
                        name: "fk_prophet_field_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_prophet_field_entity_place_entity_id",
                        column: x => x.place_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "reign_length",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    years = table.Column<int>(type: "integer", nullable: true),
                    months = table.Column<int>(type: "integer", nullable: true),
                    days = table.Column<int>(type: "integer", nullable: true),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reign_length", x => x.id);
                    table.CheckConstraint("ck_reign_length_source_not_empty", "length(btrim(source)) > 0");
                    table.CheckConstraint("ck_reign_length_stated", "years IS NOT NULL OR months IS NOT NULL OR days IS NOT NULL");
                    table.ForeignKey(
                        name: "fk_reign_length_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "throne_name",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_throne_name", x => x.id);
                    table.CheckConstraint("ck_throne_name_not_empty", "length(btrim(name)) > 0");
                    table.CheckConstraint("ck_throne_name_source_not_empty", "length(btrim(source)) > 0");
                    table.ForeignKey(
                        name: "fk_throne_name_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_prophet_field_entity_id",
                table: "prophet_field",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_prophet_field_place_entity_id",
                table: "prophet_field",
                column: "place_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_reign_length_entity_id",
                table: "reign_length",
                column: "entity_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_throne_name_entity_id_language",
                table: "throne_name",
                columns: new[] { "entity_id", "language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "prophet_field");

            migrationBuilder.DropTable(
                name: "reign_length");

            migrationBuilder.DropTable(
                name: "throne_name");
        }
    }
}
