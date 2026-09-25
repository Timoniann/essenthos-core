using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AnEventKeepsWhatItsSourceStated : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "stated_year",
                table: "event_date",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "location_book",
                table: "event",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "location_chapter",
                table: "event",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "location_verse",
                table: "event",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "region_at_the_time",
                table: "event",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "stated_year",
                table: "event_date");

            migrationBuilder.DropColumn(
                name: "location_book",
                table: "event");

            migrationBuilder.DropColumn(
                name: "location_chapter",
                table: "event");

            migrationBuilder.DropColumn(
                name: "location_verse",
                table: "event");

            migrationBuilder.DropColumn(
                name: "region_at_the_time",
                table: "event");
        }
    }
}
