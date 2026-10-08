using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class PlacesSayWhichPlaceTheyAreAnotherNameFor : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "another_name_for_entity_id",
                table: "entity",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_another_name_for_entity_id",
                table: "entity",
                column: "another_name_for_entity_id");

            migrationBuilder.AddForeignKey(
                name: "fk_entity_entity_another_name_for_entity_id",
                table: "entity",
                column: "another_name_for_entity_id",
                principalTable: "entity",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_entity_entity_another_name_for_entity_id",
                table: "entity");

            migrationBuilder.DropIndex(
                name: "ix_entity_another_name_for_entity_id",
                table: "entity");

            migrationBuilder.DropColumn(
                name: "another_name_for_entity_id",
                table: "entity");
        }
    }
}
