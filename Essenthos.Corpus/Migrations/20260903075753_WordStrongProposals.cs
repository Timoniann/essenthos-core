using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class WordStrongProposals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "word_strong",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    word_id = table.Column<long>(type: "bigint", nullable: false),
                    number = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_strong", x => x.id);
                    table.CheckConstraint("ck_word_strong_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_word_strong_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_word_strong_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_word_strong_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_word_strong_word_word_id",
                        column: x => x.word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_word_strong_number1",
                table: "word_strong",
                column: "number");

            migrationBuilder.CreateIndex(
                name: "ix_word_strong_word_id",
                table: "word_strong",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_strong_word_id_number_method",
                table: "word_strong",
                columns: new[] { "word_id", "number", "method" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "word_strong");
        }
    }
}
