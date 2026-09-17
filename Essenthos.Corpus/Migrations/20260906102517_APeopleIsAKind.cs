using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class APeopleIsAKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "people_entity_id",
                table: "strong_gentilic",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "origin_entity_id",
                table: "entity",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_strong_gentilic_people_entity_id",
                table: "strong_gentilic",
                column: "people_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_origin_entity_id",
                table: "entity",
                column: "origin_entity_id");

            migrationBuilder.AddForeignKey(
                name: "fk_entity_entity_origin_entity_id",
                table: "entity",
                column: "origin_entity_id",
                principalTable: "entity",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_strong_gentilic_entity_people_entity_id",
                table: "strong_gentilic",
                column: "people_entity_id",
                principalTable: "entity",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_entity_entity_origin_entity_id",
                table: "entity");

            migrationBuilder.DropForeignKey(
                name: "fk_strong_gentilic_entity_people_entity_id",
                table: "strong_gentilic");

            migrationBuilder.DropIndex(
                name: "ix_strong_gentilic_people_entity_id",
                table: "strong_gentilic");

            migrationBuilder.DropIndex(
                name: "ix_entity_origin_entity_id",
                table: "entity");

            migrationBuilder.DropColumn(
                name: "people_entity_id",
                table: "strong_gentilic");

            migrationBuilder.DropColumn(
                name: "origin_entity_id",
                table: "entity");
        }
    }
}
