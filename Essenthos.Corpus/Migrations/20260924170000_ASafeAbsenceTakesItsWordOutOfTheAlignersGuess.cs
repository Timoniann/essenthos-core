using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ASafeAbsenceTakesItsWordOutOfTheAlignersGuess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evidentia_withdrawal",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    review_id = table.Column<long>(type: "bigint", nullable: false),
                    word_id = table.Column<long>(type: "bigint", nullable: false),
                    link_id = table.Column<long>(type: "bigint", nullable: true),
                    from_text_id = table.Column<int>(type: "integer", nullable: false),
                    to_text_id = table.Column<int>(type: "integer", nullable: false),
                    relation = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    from_word_ids = table.Column<long[]>(type: "bigint[]", nullable: false),
                    to_word_ids = table.Column<long[]>(type: "bigint[]", nullable: false),
                    claim_sources = table.Column<string[]>(type: "text[]", nullable: false),
                    claim_confidences = table.Column<double?[]>(type: "double precision[]", nullable: false),
                    claim_notes = table.Column<string[]>(type: "text[]", nullable: false),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evidentia_withdrawal", x => x.id);
                    table.CheckConstraint("ck_evidentia_withdrawal_claims_aligned", "cardinality(\"claim_sources\") = cardinality(\"claim_confidences\") AND cardinality(\"claim_sources\") = cardinality(\"claim_notes\")");
                    table.ForeignKey(
                        name: "fk_evidentia_withdrawal_evidentia_review_review_id",
                        column: x => x.review_id,
                        principalTable: "evidentia_review",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_evidentia_withdrawal_links_link_id",
                        column: x => x.link_id,
                        principalTable: "link",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_evidentia_withdrawal_words_word_id",
                        column: x => x.word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_withdrawal_link_id",
                table: "evidentia_withdrawal",
                column: "link_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_withdrawal_review_id",
                table: "evidentia_withdrawal",
                column: "review_id");

            migrationBuilder.CreateIndex(
                name: "ix_evidentia_withdrawal_word_id",
                table: "evidentia_withdrawal",
                column: "word_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evidentia_withdrawal");
        }
    }
}
