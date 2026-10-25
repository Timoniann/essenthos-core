using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AThingMayBePicturedFromItsModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_image_kind",
                table: "entity_image");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_image_kind",
                table: "entity_image",
                sql: "kind IN ('public', 'generated', 'rendered')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_image_kind",
                table: "entity_image");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_image_kind",
                table: "entity_image",
                sql: "kind IN ('public', 'generated')");
        }
    }
}
