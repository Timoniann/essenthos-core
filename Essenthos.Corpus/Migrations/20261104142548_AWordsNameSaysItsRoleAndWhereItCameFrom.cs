using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AWordsNameSaysItsRoleAndWhereItCameFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "origin",
                table: "word_entity",
                type: "text",
                nullable: true,
                comment: "Where the name comes from: settled, projected, added, differs, or base-reading (the base text's record beside a differs row on the same word, never the word's own name). Null where the corpus load wrote the row.");

            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "word_entity",
                type: "text",
                nullable: true,
                comment: "How the word stands for the record: name, people, title or thing. Null where the corpus load wrote the row.");

            migrationBuilder.AddCheckConstraint(
                name: "ck_word_entity_origin",
                table: "word_entity",
                sql: "origin IS NULL OR origin IN ('settled', 'projected', 'added', 'differs', 'base-reading')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_word_entity_role",
                table: "word_entity",
                sql: "role IS NULL OR role IN ('name', 'people', 'title', 'thing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_word_entity_origin",
                table: "word_entity");

            migrationBuilder.DropCheckConstraint(
                name: "ck_word_entity_role",
                table: "word_entity");

            migrationBuilder.DropColumn(
                name: "origin",
                table: "word_entity");

            migrationBuilder.DropColumn(
                name: "role",
                table: "word_entity");
        }
    }
}
