using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachPlaceReferenceSaysWhoStatedIt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "entity_verse",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Every row already here came from BibleData: it is the only loader that has ever
            // written this table, and the second places source joins its references onto the same
            // entities. Without this, the corpus already loaded would answer "nobody said" for the
            // half of the place layer that BibleData does state, and a page that reports which
            // dataset a reference came from would be wrong rather than merely silent.
            migrationBuilder.Sql(
                """
                UPDATE entity_verse
                SET source = 'BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0'
                WHERE source = ''
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "source",
                table: "entity_verse");
        }
    }
}
