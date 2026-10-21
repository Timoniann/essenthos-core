using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AWhatAPersonIsKnownForIsReadInEveryLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_known_for",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    english = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_known_for", x => x.id);
                    table.CheckConstraint("ck_entity_known_for_ordinal", "ordinal >= 0");
                    table.CheckConstraint("ck_entity_known_for_text", "length(btrim(english)) > 0 AND length(btrim(source)) > 0");
                    table.ForeignKey(
                        name: "fk_entity_known_for_entities_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_known_for_reference",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    known_for_id = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    end_chapter = table.Column<int>(type: "integer", nullable: false),
                    end_verse = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_known_for_reference", x => x.id);
                    table.CheckConstraint("ck_entity_known_for_reference_runs_forward", "end_chapter > canonical_chapter OR (end_chapter = canonical_chapter AND end_verse >= canonical_verse)");
                    table.ForeignKey(
                        name: "fk_entity_known_for_reference_entity_known_for_known_for_id",
                        column: x => x.known_for_id,
                        principalTable: "entity_known_for",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_known_for_text",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    known_for_id = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_known_for_text", x => x.id);
                    table.CheckConstraint("ck_entity_known_for_text_text", "length(btrim(text)) > 0");
                    table.ForeignKey(
                        name: "fk_entity_known_for_text_entity_known_for_known_for_id",
                        column: x => x.known_for_id,
                        principalTable: "entity_known_for",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_known_for_entity_id_ordinal",
                table: "entity_known_for",
                columns: new[] { "entity_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_known_for_reference_known_for_id_position",
                table: "entity_known_for_reference",
                columns: new[] { "known_for_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_known_for_text_known_for_id_language",
                table: "entity_known_for_text",
                columns: new[] { "known_for_id", "language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_known_for_reference");

            migrationBuilder.DropTable(
                name: "entity_known_for_text");

            migrationBuilder.DropTable(
                name: "entity_known_for");
        }
    }
}
