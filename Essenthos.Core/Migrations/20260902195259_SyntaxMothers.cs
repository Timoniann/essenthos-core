using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class SyntaxMothers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "ix_word_group_mother_group_id",
                table: "word_group",
                column: "mother_group_id");

            migrationBuilder.CreateIndex(
                name: "ix_word_group_mother_word_id",
                table: "word_group",
                column: "mother_word_id");

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

            migrationBuilder.DropColumn(
                name: "mother_group_id",
                table: "word_group");

            migrationBuilder.DropColumn(
                name: "mother_word_id",
                table: "word_group");
        }
    }
}
