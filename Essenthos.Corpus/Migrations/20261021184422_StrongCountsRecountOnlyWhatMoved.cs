using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class StrongCountsRecountOnlyWhatMoved : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_count_mark",
                columns: table => new
                {
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    phrases = table.Column<Guid>(type: "uuid", nullable: true),
                    books = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_count_mark", x => x.text_id);
                    table.ForeignKey(
                        name: "fk_strong_count_mark_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "A digest of what a text's Strong counts were last counted from and of the rows they left, so the load counts again only the texts where either moved. Derived; deleting a row only costs a recount.");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_count_mark");
        }
    }
}
