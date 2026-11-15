using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachRecordAndStrongPageIsOneStoredDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "page_document",
                columns: table => new
                {
                    kind = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    address = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<byte[]>(type: "bytea", nullable: false),
                    hash = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_page_document", x => new { x.kind, x.language, x.address });
                },
                comment: "What the server writes a record's or a Strong number's page from, per site language: the page's own facts and the API answers it carries, as gzip-compressed JSON. Pages by address. Made by the serving build (page-documents/{kind}/{language}).");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "page_document");
        }
    }
}
