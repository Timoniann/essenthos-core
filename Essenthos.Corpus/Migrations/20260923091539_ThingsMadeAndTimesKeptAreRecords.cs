using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ThingsMadeAndTimesKeptAreRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "subtype",
                table: "entity",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "entity_passage",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: true),
                    end_chapter = table.Column<int>(type: "integer", nullable: false),
                    end_verse = table.Column<int>(type: "integer", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_passage", x => x.id);
                    table.CheckConstraint("ck_entity_passage_role", "role IN ('key', 'command')");
                    table.CheckConstraint("ck_entity_passage_runs_forward", "end_chapter > canonical_chapter OR (end_chapter = canonical_chapter AND (canonical_verse IS NULL OR end_verse IS NULL OR end_verse >= canonical_verse))");
                    table.CheckConstraint("ck_entity_passage_source_not_empty", "length(btrim(source)) > 0");
                    table.ForeignKey(
                        name: "fk_entity_passage_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "observance_time",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    cycle = table.Column<string>(type: "text", nullable: false),
                    month = table.Column<int>(type: "integer", nullable: true),
                    day = table.Column<int>(type: "integer", nullable: true),
                    last_day = table.Column<int>(type: "integer", nullable: true),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observance_time", x => x.id);
                    table.CheckConstraint("ck_observance_time_cycle", "cycle IN ('weekly', 'monthly', 'yearly', 'seventh-year', 'fiftieth-year')");
                    table.CheckConstraint("ck_observance_time_day", "day IS NULL OR day BETWEEN 1 AND 30");
                    table.CheckConstraint("ck_observance_time_last_day", "last_day IS NULL OR (day IS NOT NULL AND last_day > day AND last_day <= 30)");
                    table.CheckConstraint("ck_observance_time_month", "month IS NULL OR month BETWEEN 1 AND 12");
                    table.CheckConstraint("ck_observance_time_source_not_empty", "length(btrim(source)) > 0");
                    table.ForeignKey(
                        name: "fk_observance_time_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_passage_canonical_book_canonical_chapter_end_chapter",
                table: "entity_passage",
                columns: new[] { "canonical_book", "canonical_chapter", "end_chapter" });

            migrationBuilder.CreateIndex(
                name: "ix_entity_passage_entity_id",
                table: "entity_passage",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_observance_time_entity_id",
                table: "observance_time",
                column: "entity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_passage");

            migrationBuilder.DropTable(
                name: "observance_time");

            migrationBuilder.DropColumn(
                name: "subtype",
                table: "entity");
        }
    }
}
