using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachTextCountsHowVariouslyItRendersANumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "phrases",
                table: "strong_reach",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "rendering_links",
                table: "strong_reach",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "renderings",
                table: "strong_reach",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_strong_reach_renderings",
                table: "strong_reach",
                sql: "phrases >= 0 AND rendering_links >= 0 AND (renderings IS NULL OR renderings <= phrases)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_strong_reach_renderings",
                table: "strong_reach");

            migrationBuilder.DropColumn(
                name: "phrases",
                table: "strong_reach");

            migrationBuilder.DropColumn(
                name: "rendering_links",
                table: "strong_reach");

            migrationBuilder.DropColumn(
                name: "renderings",
                table: "strong_reach");
        }
    }
}
