using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ARecordWrittenTwiceIsFoldedIntoOne : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "merged_record",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    distinguisher = table.Column<string>(type: "text", nullable: true),
                    record_source_id = table.Column<string>(type: "text", nullable: false),
                    record_source = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    reason = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_merged_record", x => x.id);
                    table.CheckConstraint("ck_merged_record_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_merged_record_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_merged_record_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_merged_record_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_merged_record_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_merged_record_entity_id",
                table: "merged_record",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_merged_record_slug",
                table: "merged_record",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "merged_record");
        }
    }
}
