using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ANameCanBeAnotherRecordsAndSaysSo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "aspect_of_entity_id",
                table: "entity_name",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_name_aspect_of_entity_id",
                table: "entity_name",
                column: "aspect_of_entity_id");

            migrationBuilder.AddForeignKey(
                name: "fk_entity_name_entity_aspect_of_entity_id",
                table: "entity_name",
                column: "aspect_of_entity_id",
                principalTable: "entity",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_entity_name_entity_aspect_of_entity_id",
                table: "entity_name");

            migrationBuilder.DropIndex(
                name: "ix_entity_name_aspect_of_entity_id",
                table: "entity_name");

            migrationBuilder.DropColumn(
                name: "aspect_of_entity_id",
                table: "entity_name");
        }
    }
}
