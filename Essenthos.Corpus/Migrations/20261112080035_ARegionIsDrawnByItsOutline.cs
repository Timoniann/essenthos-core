using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ARegionIsDrawnByItsOutline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "outline_id",
                table: "place_candidate",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "place_outline",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    dataset = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    west = table.Column<double>(type: "double precision", nullable: false),
                    south = table.Column<double>(type: "double precision", nullable: false),
                    east = table.Column<double>(type: "double precision", nullable: false),
                    north = table.Column<double>(type: "double precision", nullable: false),
                    coarse = table.Column<string>(type: "text", nullable: false),
                    fine = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_outline", x => x.id);
                    table.CheckConstraint("ck_place_outline_box", "west BETWEEN -180 AND 180 AND east BETWEEN -180 AND 180 AND south BETWEEN -90 AND 90 AND north BETWEEN -90 AND 90 AND west <= east AND south <= north");
                    table.CheckConstraint("ck_place_outline_dataset", "dataset IN ('openbible', 'openstreetmap')");
                    table.CheckConstraint("ck_place_outline_kind", "kind IN ('rough-boundary', 'confidence-bands', 'probable-area', 'precise')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_place_candidate_outline_id",
                table: "place_candidate",
                column: "outline_id");

            migrationBuilder.AddForeignKey(
                name: "fk_place_candidate_place_outlines_outline_id",
                table: "place_candidate",
                column: "outline_id",
                principalTable: "place_outline",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_place_candidate_place_outlines_outline_id",
                table: "place_candidate");

            migrationBuilder.DropTable(
                name: "place_outline");

            migrationBuilder.DropIndex(
                name: "ix_place_candidate_outline_id",
                table: "place_candidate");

            migrationBuilder.DropColumn(
                name: "outline_id",
                table: "place_candidate");
        }
    }
}
