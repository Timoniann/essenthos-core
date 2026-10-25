using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EveryProposedSiteOfAPlaceIsHeld : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "place_candidate",
                columns: table => new
                {
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    modern_id = table.Column<string>(type: "text", nullable: true),
                    site = table.Column<string>(type: "text", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: true),
                    radius_meters = table.Column<int>(type: "integer", nullable: true),
                    site_type = table.Column<string>(type: "text", nullable: true),
                    relation = table.Column<string>(type: "text", nullable: true),
                    through_open_bible_id = table.Column<string>(type: "text", nullable: true),
                    special = table.Column<string>(type: "text", nullable: true),
                    withheld = table.Column<string>(type: "text", nullable: true),
                    grounds = table.Column<List<string>>(type: "text[]", nullable: false),
                    coordinates_source = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_candidate", x => new { x.entity_id, x.rank });
                    table.CheckConstraint("ck_place_candidate_kind", "kind IN ('point', 'representative-point', 'center', 'settlement')");
                    table.CheckConstraint("ck_place_candidate_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_place_candidate_longitude", "longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_place_candidate_point", "(longitude IS NULL) = (latitude IS NULL)");
                    table.CheckConstraint("ck_place_candidate_relation", "relation IN ('near', 'along', 'on', 'in', 'around')");
                    table.CheckConstraint("ck_place_candidate_site_or_special", "(modern_id IS NULL) <> (special IS NULL)");
                    table.CheckConstraint("ck_place_candidate_special", "special IN ('not-a-place', 'not-a-proper-name', 'unknown-place', 'nonspecific-place', 'multiple-locations')");
                    table.CheckConstraint("ck_place_candidate_withheld", "withheld IN ('openstreetmap', 'commercial')");
                    table.ForeignKey(
                        name: "fk_place_candidate_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "place_candidate");
        }
    }
}
