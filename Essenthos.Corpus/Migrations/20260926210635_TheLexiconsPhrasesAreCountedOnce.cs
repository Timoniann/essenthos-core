using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheLexiconsPhrasesAreCountedOnce : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_rendering",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    phrase = table.Column<string>(type: "text", nullable: false),
                    uses = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_rendering", x => x.id);
                    table.CheckConstraint("ck_strong_rendering_rank", "rank > 0");
                    table.CheckConstraint("ck_strong_rendering_uses", "uses > 0");
                    table.ForeignKey(
                        name: "fk_strong_rendering_texts_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "A translation's commonest phrases for one Strong number, counted from the links by the statement the entry page counts with. Derived and rebuilt on every load; it asserts nothing the links do not.");

            migrationBuilder.CreateIndex(
                name: "ix_strong_rendering_text_id_strong_number_rank",
                table: "strong_rendering",
                columns: new[] { "text_id", "strong_number", "rank" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_rendering");
        }
    }
}
