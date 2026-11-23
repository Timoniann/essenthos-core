using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class StrongNumbersKnowTheirCounterpartsInTheOtherLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_counterpart",
                columns: table => new
                {
                    hebrew = table.Column<string>(type: "text", nullable: false),
                    greek = table.Column<string>(type: "text", nullable: false),
                    septuagint = table.Column<int>(type: "integer", nullable: false),
                    greek_words = table.Column<int>(type: "integer", nullable: false),
                    hebrew_linked = table.Column<int>(type: "integer", nullable: false),
                    greek_linked = table.Column<int>(type: "integer", nullable: false),
                    stated = table.Column<int>(type: "integer", nullable: false),
                    dictionary = table.Column<string>(type: "text", nullable: true),
                    statement = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_counterpart", x => new { x.hebrew, x.greek });
                    table.CheckConstraint("ck_strong_counterpart_counts", "septuagint >= 0 AND greek_words >= 0 AND stated BETWEEN 0 AND septuagint AND septuagint <= hebrew_linked AND greek_words <= greek_linked");
                    table.CheckConstraint("ck_strong_counterpart_numbers", "hebrew LIKE 'H%' AND greek LIKE 'G%'");
                    table.CheckConstraint("ck_strong_counterpart_said", "(dictionary IS NULL) = (statement IS NULL)");
                },
                comment: "A Hebrew Strong number and a Greek one that stand for the same word: how often the Septuagint renders the one by the other in its settled links to BHSA (Greek words numbered through GLAUx's lemma), and Strong's own derivation where his Greek dictionary says the word comes from the Hebrew. Made at build.");

            migrationBuilder.CreateIndex(
                name: "ix_strong_counterpart_greek",
                table: "strong_counterpart",
                column: "greek");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_counterpart");
        }
    }
}
