using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ReleaseFactsAreReadNotCounted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "release_fact",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    stamp = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_release_fact", x => x.key);
                },
                comment: "Constants of the release the API would otherwise count at every start (each text's counts and links, each dataset's rows, the Chapter Context's weights), as JSON by key, with the stamp of the code that counted them. Made by the serving build (release-facts).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "release_fact");
        }
    }
}
