using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EventOrderIsStored : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "axis_year",
                table: "event_date",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "in_year_rank",
                table: "event",
                type: "integer",
                nullable: true);

            // The axis years follow from the rows already held; the ranks need the build's order and come with its next load.
            migrationBuilder.Sql("""
                UPDATE event_date d
                SET axis_year = CASE
                    WHEN d.stated_year IS NOT NULL THEN CASE WHEN d.stated_year < 0 THEN d.stated_year + 1 ELSE d.stated_year END
                    WHEN d.year IS NOT NULL THEN d.year - c.last_year_before_the_common_era
                END
                FROM chronology c
                WHERE c.id = d.chronology_id;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "axis_year",
                table: "event_date");

            migrationBuilder.DropColumn(
                name: "in_year_rank",
                table: "event");
        }
    }
}
