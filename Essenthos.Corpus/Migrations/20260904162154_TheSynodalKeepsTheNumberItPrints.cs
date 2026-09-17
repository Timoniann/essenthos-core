using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheSynodalKeepsTheNumberItPrints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Where the Synodal and Ohienko's Ukrainian keep the address their own pages print. Both
            // files were renumbered to the King James by their publisher, and both say so in the
            // verse text -- "(118-1)" opening Psalm 119:1 -- which was matched and deleted and
            // written nowhere. 4,666 addresses over 4,604 verses of the two.
            //
            // Nothing is filled in here. The table arrives empty and the filler in the startup
            // pipeline writes it on the next start, guarded on these rows rather than on the text,
            // because both texts are already loaded everywhere the corpus is and the corpus loader
            // returns early for a text it has. So this is a schema change and not a reload: no text
            // is emptied, no link is touched, and the addresses appear once the API comes back up.
            migrationBuilder.CreateTable(
                name: "stated_verse_number",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    verse_id = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    chapter_number = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stated_verse_number", x => x.id);
                    table.ForeignKey(
                        name: "fk_stated_verse_number_verses_verse_id",
                        column: x => x.verse_id,
                        principalTable: "verse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_stated_verse_number_verse_id_position",
                table: "stated_verse_number",
                columns: new[] { "verse_id", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stated_verse_number");
        }
    }
}
