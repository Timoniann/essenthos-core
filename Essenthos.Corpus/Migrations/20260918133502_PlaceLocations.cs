using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class PlaceLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "place_location",
                columns: table => new
                {
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    longitude = table.Column<double>(type: "double precision", nullable: false),
                    latitude = table.Column<double>(type: "double precision", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    modern_id = table.Column<string>(type: "text", nullable: false),
                    coordinates_source = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_location", x => x.entity_id);
                    table.CheckConstraint("ck_place_location_kind", "kind IN ('point', 'representative-point', 'center', 'settlement')");
                    table.CheckConstraint("ck_place_location_latitude", "latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_place_location_longitude", "longitude BETWEEN -180 AND 180");
                    table.ForeignKey(
                        name: "fk_place_location_entity_entity_id",
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
                name: "place_location");
        }
    }
}
