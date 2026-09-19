using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TitlesAndTheirBearers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "title_bearer",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    title_entity_id = table.Column<int>(type: "integer", nullable: false),
                    bearer_entity_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_title_bearer", x => x.id);
                    table.ForeignKey(
                        name: "fk_title_bearer_entity_bearer_entity_id",
                        column: x => x.bearer_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_title_bearer_entity_title_entity_id",
                        column: x => x.title_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_title_bearer_bearer_entity_id",
                table: "title_bearer",
                column: "bearer_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_title_bearer_title_entity_id_bearer_entity_id",
                table: "title_bearer",
                columns: new[] { "title_entity_id", "bearer_entity_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "title_bearer");
        }
    }
}
