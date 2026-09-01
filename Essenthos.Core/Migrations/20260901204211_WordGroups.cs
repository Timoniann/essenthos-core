using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class WordGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "word_group",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    parent_id = table.Column<long>(type: "bigint", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    features = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_group", x => x.id);
                    table.ForeignKey(
                        name: "fk_word_group_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_word_group_word_group_parent_id",
                        column: x => x.parent_id,
                        principalTable: "word_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "word_group_word",
                columns: table => new
                {
                    word_group_id = table.Column<long>(type: "bigint", nullable: false),
                    word_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_group_word", x => new { x.word_group_id, x.word_id });
                    table.ForeignKey(
                        name: "fk_word_group_word_word_group_word_group_id",
                        column: x => x.word_group_id,
                        principalTable: "word_group",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_word_group_word_word_word_id",
                        column: x => x.word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_word_group_parent_id",
                table: "word_group",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_group_text_id_kind",
                table: "word_group",
                columns: new[] { "text_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_word_group_word_word_id",
                table: "word_group_word",
                column: "word_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "word_group_word");

            migrationBuilder.DropTable(
                name: "word_group");
        }
    }
}
