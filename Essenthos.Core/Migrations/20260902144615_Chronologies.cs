using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class Chronologies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chronology",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    authority = table.Column<string>(type: "text", nullable: true),
                    basis = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: true),
                    last_year_before_the_common_era = table.Column<int>(type: "integer", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chronology", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "event_date",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    event_id = table.Column<int>(type: "integer", nullable: false),
                    chronology_id = table.Column<int>(type: "integer", nullable: false),
                    year = table.Column<int>(type: "integer", nullable: true),
                    earliest_year = table.Column<int>(type: "integer", nullable: true),
                    latest_year = table.Column<int>(type: "integer", nullable: true),
                    calculation = table.Column<string>(type: "text", nullable: true),
                    citation = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event_date", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_date_chronology_chronology_id",
                        column: x => x.chronology_id,
                        principalTable: "chronology",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_event_date_event_event_id",
                        column: x => x.event_id,
                        principalTable: "event",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_chronology_slug",
                table: "chronology",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_date_chronology_id",
                table: "event_date",
                column: "chronology_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_date_event_id_chronology_id",
                table: "event_date",
                columns: new[] { "event_id", "chronology_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "event_date");

            migrationBuilder.DropTable(
                name: "chronology");
        }
    }
}
