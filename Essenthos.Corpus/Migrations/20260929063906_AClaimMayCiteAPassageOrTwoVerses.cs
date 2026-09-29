using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AClaimMayCiteAPassageOrTwoVerses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "citation",
                table: "entity_relationship",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "citation",
                table: "entity_descriptor",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "citation",
                table: "entity_relationship");

            migrationBuilder.DropColumn(
                name: "citation",
                table: "entity_descriptor");
        }
    }
}
