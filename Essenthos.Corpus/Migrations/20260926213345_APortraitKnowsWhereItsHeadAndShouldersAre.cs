using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class APortraitKnowsWhereItsHeadAndShouldersAre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "bust_height",
                table: "entity_image",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "bust_width",
                table: "entity_image",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "bust_x",
                table: "entity_image",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "bust_y",
                table: "entity_image",
                type: "double precision",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "bust_height",
                table: "entity_image");

            migrationBuilder.DropColumn(
                name: "bust_width",
                table: "entity_image");

            migrationBuilder.DropColumn(
                name: "bust_x",
                table: "entity_image");

            migrationBuilder.DropColumn(
                name: "bust_y",
                table: "entity_image");
        }
    }
}
