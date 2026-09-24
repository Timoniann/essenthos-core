using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheLandsHaveTheirPeriods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "period_authority",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    periodo_id = table.Column<string>(type: "text", nullable: false),
                    attribution = table.Column<string>(type: "text", nullable: false),
                    citation = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: true),
                    creators = table.Column<string>(type: "text", nullable: true),
                    year_published = table.Column<int>(type: "integer", nullable: true),
                    locator = table.Column<string>(type: "text", nullable: true),
                    uri = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period_authority", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "period_definition",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    periodo_id = table.Column<string>(type: "text", nullable: false),
                    authority_id = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    language_tag = table.Column<string>(type: "text", nullable: true),
                    labels = table.Column<string>(type: "jsonb", nullable: true),
                    region = table.Column<string>(type: "text", nullable: false),
                    coverage = table.Column<string>(type: "jsonb", nullable: true),
                    coverage_description = table.Column<string>(type: "text", nullable: true),
                    start_label = table.Column<string>(type: "text", nullable: true),
                    start_earliest = table.Column<int>(type: "integer", nullable: false),
                    start_latest = table.Column<int>(type: "integer", nullable: false),
                    stop_label = table.Column<string>(type: "text", nullable: true),
                    stop_earliest = table.Column<int>(type: "integer", nullable: false),
                    stop_latest = table.Column<int>(type: "integer", nullable: false),
                    broader = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    editorial_note = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period_definition", x => x.id);
                    table.ForeignKey(
                        name: "fk_period_definition_period_authority_authority_id",
                        column: x => x.authority_id,
                        principalTable: "period_authority",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_period_authority_periodo_id",
                table: "period_authority",
                column: "periodo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_period_definition_authority_id",
                table: "period_definition",
                column: "authority_id");

            migrationBuilder.CreateIndex(
                name: "ix_period_definition_periodo_id",
                table: "period_definition",
                column: "periodo_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_period_definition_region",
                table: "period_definition",
                column: "region");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "period_definition");

            migrationBuilder.DropTable(
                name: "period_authority");
        }
    }
}
