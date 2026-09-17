using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ATitleSaysWhoseWordsItIs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Whose words an event's name is. Every row that already exists was titled by the
            // dataset it came from -- BibleData names its events, Wikidata names its items -- so
            // "source" is the true answer for all of them and not merely a placeholder default.
            //
            // It exists because the next source does not title anything. Ussher wrote 7,000
            // numbered paragraphs and no headings, and a name column that cannot be null will hold
            // something either way; the difference between quoting his opening sentence and
            // summarising it is the difference between reporting a chronologer and speaking for
            // him, and nothing in the row said which until this column.
            migrationBuilder.AddColumn<string>(
                name: "name_source",
                table: "event",
                type: "text",
                nullable: false,
                defaultValue: "source");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name_source",
                table: "event");
        }
    }
}
