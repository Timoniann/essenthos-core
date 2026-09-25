using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheUnionVersionSaysWhichCharactersItIsPrintedIn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "script",
                table: "text",
                type: "text",
                nullable: true);

            // The texts a corpus already holds; a fresh load takes the script from the definition.
            migrationBuilder.Sql("""
                UPDATE text SET script = 'Hant' WHERE upper(slug) = 'CUV';
                UPDATE text SET script = 'Hans' WHERE upper(slug) = 'CUVS';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "script",
                table: "text");
        }
    }
}
