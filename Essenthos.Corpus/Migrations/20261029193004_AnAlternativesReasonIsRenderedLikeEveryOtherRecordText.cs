using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AnAlternativesReasonIsRenderedLikeEveryOtherRecordText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "reason_rendered_by",
                table: "entity_alternative");

            migrationBuilder.DropColumn(
                name: "reason_renderings",
                table: "entity_alternative");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
    }
}
