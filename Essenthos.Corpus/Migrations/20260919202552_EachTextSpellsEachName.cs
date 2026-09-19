using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachTextSpellsEachName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_rendering",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    form = table.Column<string>(type: "text", nullable: false),
                    folded = table.Column<string>(type: "text", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false),
                    heading = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_rendering", x => x.id);
                    table.CheckConstraint("ck_entity_rendering_occurrences", "occurrences > 0");
                    table.ForeignKey(
                        name: "fk_entity_rendering_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_entity_rendering_texts_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "How one text spells an entity's name, counted from the words word_entity says name it there. Derived and rebuilt with those annotations; it asserts nothing they do not.");

            migrationBuilder.CreateIndex(
                name: "ix_entity_name_form_form",
                table: "entity_name_form",
                column: "form")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_entity_rendering_entity_id_text_id_form",
                table: "entity_rendering",
                columns: new[] { "entity_id", "text_id", "form" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_rendering_folded",
                table: "entity_rendering",
                column: "folded")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "ix_entity_rendering_text_id",
                table: "entity_rendering",
                column: "text_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_rendering");

            migrationBuilder.DropIndex(
                name: "ix_entity_name_form_form",
                table: "entity_name_form");
        }
    }
}
