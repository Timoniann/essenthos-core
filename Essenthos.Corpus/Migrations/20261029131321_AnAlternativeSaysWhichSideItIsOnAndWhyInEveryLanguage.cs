using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AnAlternativeSaysWhichSideItIsOnAndWhyInEveryLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "back",
                table: "entity_alternative",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "reason_rendered_by",
                table: "entity_alternative",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "reason_renderings",
                table: "entity_alternative",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "back",
                table: "entity_alternative");

            migrationBuilder.DropColumn(
                name: "reason_rendered_by",
                table: "entity_alternative");

            migrationBuilder.DropColumn(
                name: "reason_renderings",
                table: "entity_alternative");
        }
    }
}
