using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class APronounSaysWhichWordItRefersTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "word_referent",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    word_id = table.Column<long>(type: "bigint", nullable: false),
                    referent_word_id = table.Column<long>(type: "bigint", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word_referent", x => x.id);
                    table.CheckConstraint("ck_word_referent_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_word_referent_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_word_referent_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_word_referent_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_word_referent_word_referent_word_id",
                        column: x => x.referent_word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_word_referent_word_word_id",
                        column: x => x.word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_word_referent_referent_word_id",
                table: "word_referent",
                column: "referent_word_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_referent_word_id_referent_word_id_source",
                table: "word_referent",
                columns: new[] { "word_id", "referent_word_id", "source" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "word_referent");
        }
    }
}
