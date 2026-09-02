using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EntityNameStrongNumbersByLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "strong_number",
                table: "entity_name",
                newName: "hebrew_strong_number");

            migrationBuilder.RenameIndex(
                name: "ix_entity_name_strong_number",
                table: "entity_name",
                newName: "ix_entity_name_hebrew_strong_number");

            migrationBuilder.AddColumn<string>(
                name: "greek_strong_number",
                table: "entity_name",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_name_greek_strong_number",
                table: "entity_name",
                column: "greek_strong_number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_entity_name_greek_strong_number",
                table: "entity_name");

            migrationBuilder.DropColumn(
                name: "greek_strong_number",
                table: "entity_name");

            migrationBuilder.RenameColumn(
                name: "hebrew_strong_number",
                table: "entity_name",
                newName: "strong_number");

            migrationBuilder.RenameIndex(
                name: "ix_entity_name_hebrew_strong_number",
                table: "entity_name",
                newName: "ix_entity_name_strong_number");
        }
    }
}
