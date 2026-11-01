using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class APersonsSexIsWrittenWithWhereItCameFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "name_gender",
                table: "entity",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sex_source",
                table: "entity",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "name_gender",
                table: "entity");

            migrationBuilder.DropColumn(
                name: "sex_source",
                table: "entity");
        }
    }
}
