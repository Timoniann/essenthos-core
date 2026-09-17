using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class WorldHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "shulman_anno_mundi",
                table: "event");

            migrationBuilder.DropColumn(
                name: "ussher_anno_mundi",
                table: "event");

            migrationBuilder.DropColumn(
                name: "ussher_bce_year",
                table: "event");

            migrationBuilder.RenameColumn(
                name: "ussher_paragraph",
                table: "event",
                newName: "uri");

            migrationBuilder.AddColumn<string>(
                name: "realm",
                table: "period",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "region",
                table: "period",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "uri",
                table: "period",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "realm",
                table: "event",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "region",
                table: "event",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_realm",
                table: "event",
                column: "realm");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_event_realm",
                table: "event");

            migrationBuilder.DropColumn(
                name: "realm",
                table: "period");

            migrationBuilder.DropColumn(
                name: "region",
                table: "period");

            migrationBuilder.DropColumn(
                name: "uri",
                table: "period");

            migrationBuilder.DropColumn(
                name: "realm",
                table: "event");

            migrationBuilder.DropColumn(
                name: "region",
                table: "event");

            migrationBuilder.RenameColumn(
                name: "uri",
                table: "event",
                newName: "ussher_paragraph");

            migrationBuilder.AddColumn<int>(
                name: "shulman_anno_mundi",
                table: "event",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ussher_anno_mundi",
                table: "event",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ussher_bce_year",
                table: "event",
                type: "integer",
                nullable: true);
        }
    }
}
