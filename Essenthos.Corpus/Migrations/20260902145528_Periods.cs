using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class Periods : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "period",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: true),
                    level = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    entity_id = table.Column<int>(type: "integer", nullable: true),
                    start_event_id = table.Column<int>(type: "integer", nullable: true),
                    end_event_id = table.Column<int>(type: "integer", nullable: true),
                    start_year = table.Column<int>(type: "integer", nullable: true),
                    end_year = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_period", x => x.id);
                    table.ForeignKey(
                        name: "fk_period_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_period_event_end_event_id",
                        column: x => x.end_event_id,
                        principalTable: "event",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_period_event_start_event_id",
                        column: x => x.start_event_id,
                        principalTable: "event",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "fk_period_period_parent_id",
                        column: x => x.parent_id,
                        principalTable: "period",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_period_end_event_id",
                table: "period",
                column: "end_event_id");

            migrationBuilder.CreateIndex(
                name: "ix_period_entity_id",
                table: "period",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_period_level",
                table: "period",
                column: "level");

            migrationBuilder.CreateIndex(
                name: "ix_period_parent_id",
                table: "period",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_period_slug",
                table: "period",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_period_start_event_id",
                table: "period",
                column: "start_event_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "period");
        }
    }
}
