using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class LinksToWikipedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_wikipedia",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    qid = table.Column<string>(type: "text", nullable: false),
                    matched_by = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_wikipedia", x => x.id);
                    table.CheckConstraint("ck_entity_wikipedia_confidence", "confidence BETWEEN 0 AND 1");
                    table.CheckConstraint("ck_entity_wikipedia_language", "language IN ('en', 'uk', 'de', 'es')");
                    table.CheckConstraint("ck_entity_wikipedia_matched_by", "matched_by IN ('owner', 'verse', 'kin', 'chapter', 'name', 'identification')");
                    table.CheckConstraint("ck_entity_wikipedia_qid", "qid ~ '^Q[0-9]+$'");
                    table.CheckConstraint("ck_entity_wikipedia_title", "length(title) > 0");
                    table.ForeignKey(
                        name: "fk_entity_wikipedia_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_wikipedia_entity_id_language",
                table: "entity_wikipedia",
                columns: new[] { "entity_id", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_wikipedia_qid",
                table: "entity_wikipedia",
                column: "qid");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_wikipedia");
        }
    }
}
