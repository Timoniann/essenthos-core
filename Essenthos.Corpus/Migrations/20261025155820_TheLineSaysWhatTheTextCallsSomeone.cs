using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheLineSaysWhatTheTextCallsSomeone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "called",
                table: "title_bearer",
                type: "jsonb",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "byname",
                table: "entity",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "called",
                table: "title_bearer");

            migrationBuilder.DropColumn(
                name: "byname",
                table: "entity");
        }
    }
}
