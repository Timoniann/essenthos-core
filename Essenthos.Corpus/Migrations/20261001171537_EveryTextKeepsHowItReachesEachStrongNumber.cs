using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <summary>
    /// The two tables the load counts each text's reach of each Strong number into. They are created
    /// empty, so the migration reads and locks nothing a reader holds; the entry page counts as it is
    /// asked until the load has filled them.
    /// </summary>
    public partial class EveryTextKeepsHowItReachesEachStrongNumber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_reach",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    witness_id = table.Column<int>(type: "integer", nullable: false),
                    occurrences = table.Column<int>(type: "integer", nullable: false),
                    reached = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_reach", x => new { x.text_id, x.strong_number });
                    table.CheckConstraint("ck_strong_reach_occurrences", "occurrences > 0");
                    table.CheckConstraint("ck_strong_reach_reached", "reached >= 0 AND reached <= occurrences");
                    table.ForeignKey(
                        name: "fk_strong_reach_texts_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_strong_reach_texts_witness_id",
                        column: x => x.witness_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "How often a Strong number stands in the edition a text is counted over and how many of those places the text's links render, counted by the statements the entry page counts with. Derived and rebuilt on every load; it asserts nothing the links do not.");

            migrationBuilder.CreateTable(
                name: "strong_reach_method",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    links = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_reach_method", x => new { x.text_id, x.strong_number, x.method });
                    table.CheckConstraint("ck_strong_reach_method_links", "links > 0");
                    table.ForeignKey(
                        name: "fk_strong_reach_method_strong_reach_text_id_strong_number",
                        columns: x => new { x.text_id, x.strong_number },
                        principalTable: "strong_reach",
                        principalColumns: new[] { "text_id", "strong_number" },
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "The links a strong_reach row counts, by the method that made them.");

            migrationBuilder.CreateIndex(
                name: "ix_strong_reach_witness_id",
                table: "strong_reach",
                column: "witness_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_reach_method");

            migrationBuilder.DropTable(
                name: "strong_reach");
        }
    }
}
