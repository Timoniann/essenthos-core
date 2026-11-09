using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class WhichTextsALinkJoinsIsReadFromAnIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_link_to_text_id_from_text_id_id",
                table: "link",
                columns: new[] { "to_text_id", "from_text_id", "id" });

            migrationBuilder.DropIndex(
                name: "ix_link_to_text_id",
                table: "link");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_link_to_text_id",
                table: "link",
                column: "to_text_id");

            migrationBuilder.DropIndex(
                name: "ix_link_to_text_id_from_text_id_id",
                table: "link");
        }
    }
}
