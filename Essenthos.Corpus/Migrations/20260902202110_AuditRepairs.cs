using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AuditRepairs : Migration
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

            migrationBuilder.AddColumn<long>(
                name: "mother_group_id",
                table: "word_group",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "mother_word_id",
                table: "word_group",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "elided",
                table: "word",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "greek_strong_number",
                table: "entity_name",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_word_group_mother_group_id",
                table: "word_group",
                column: "mother_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_group_mother_word_id",
                table: "word_group",
                column: "mother_word_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_name_greek_strong_number",
                table: "entity_name",
                column: "greek_strong_number");

            migrationBuilder.AddForeignKey(
                name: "fk_word_group_word_group_mother_group_id",
                table: "word_group",
                column: "mother_group_id",
                principalTable: "word_group",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_word_group_word_mother_word_id",
                table: "word_group",
                column: "mother_word_id",
                principalTable: "word",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_word_group_word_group_mother_group_id",
                table: "word_group");

            migrationBuilder.DropForeignKey(
                name: "fk_word_group_word_mother_word_id",
                table: "word_group");

            migrationBuilder.DropIndex(
                name: "ix_word_group_mother_group_id",
                table: "word_group");

            migrationBuilder.DropIndex(
                name: "ix_word_group_mother_word_id",
                table: "word_group");

            migrationBuilder.DropIndex(
                name: "ix_entity_name_greek_strong_number",
                table: "entity_name");

            migrationBuilder.DropColumn(
                name: "mother_group_id",
                table: "word_group");

            migrationBuilder.DropColumn(
                name: "mother_word_id",
                table: "word_group");

            migrationBuilder.DropColumn(
                name: "elided",
                table: "word");

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
