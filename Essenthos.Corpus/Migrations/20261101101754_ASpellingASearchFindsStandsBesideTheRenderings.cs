using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ASpellingASearchFindsStandsBesideTheRenderings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_search_spelling",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    form = table.Column<string>(type: "text", nullable: false),
                    folded = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_search_spelling", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_search_spelling_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "A spelling a text prints for an entity that entity_rendering does not keep and a search finds the entity by. Derived and rebuilt with entity_rendering; nothing else reads it.");

            migrationBuilder.CreateIndex(
                name: "ix_entity_search_spelling_entity_id_folded",
                table: "entity_search_spelling",
                columns: new[] { "entity_id", "folded" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_search_spelling_folded",
                table: "entity_search_spelling",
                column: "folded")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_search_spelling");
        }
    }
}
