using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheCoverageShareCarriesItsCounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The numerator and denominator of the coverage share, recorded beside it.
            //
            // A ratio alone cannot be reproduced or compared, and "which words did you count" has
            // three defensible answers in this corpus: words reaching any link at all, words
            // reaching a text that is not a translation, and words reaching an original-language
            // text. Two measurements a day apart differed by four points with no way to tell which
            // question either had asked, which is the whole of the difficulty.
            //
            // Existing rows keep zeros. A run recorded before the counts existed did not count
            // anything, and writing a number for it from today's corpus would be inventing history
            // -- the share it stored stays, and a reader can see it has no counts under it.
            migrationBuilder.AddColumn<int>(
                name: "rendered_words",
                table: "verification_run",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "words",
                table: "verification_run",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "rendered_words",
                table: "verification_run");

            migrationBuilder.DropColumn(
                name: "words",
                table: "verification_run");
        }
    }
}
