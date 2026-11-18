using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class FoldedFormsCompareByteByByte : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "form",
                table: "word_rendering_witness",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "form",
                table: "word_rendering",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "normalised_text",
                table: "word",
                type: "text",
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "graphical_text",
                table: "word",
                type: "text",
                nullable: true,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "form",
                table: "text_form",
                type: "text",
                nullable: false,
                collation: "C",
                oldClrType: typeof(string),
                oldType: "text");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "form",
                table: "word_rendering_witness",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "form",
                table: "word_rendering",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "normalised_text",
                table: "word",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "graphical_text",
                table: "word",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true,
                oldCollation: "C");

            migrationBuilder.AlterColumn<string>(
                name: "form",
                table: "text_form",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldCollation: "C");
        }
    }
}
