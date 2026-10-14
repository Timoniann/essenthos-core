using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class CarryMarkSaysHowFarTheLinksWereCarriedOver : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "carry_mark",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    link_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_carry_mark", x => x.id);
                    table.CheckConstraint("ck_carry_mark_one_row", "id = 1");
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "carry_mark");
        }
    }
}
