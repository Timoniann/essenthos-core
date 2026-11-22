using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class RecordDescriptorsAreStoredAtBuild : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "record_descriptor",
                columns: table => new
                {
                    prose = table.Column<string>(type: "text", nullable: false),
                    names = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: true)
                        .Annotation("Npgsql:Compression:", "lz4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_descriptor", x => new { x.prose, x.names, x.slug });
                },
                comment: "What the corpus says each record is in a language of words and a language of names, as the whole descriptor JSON a record page, a list of practitioners or a place's distinguisher reads; no body where nothing is said. Records by slug. Made by the serving build (record-descriptors/{prose}).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "record_descriptor");
        }
    }
}
