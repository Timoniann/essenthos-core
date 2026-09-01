using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class SingularTableNames : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_verification_runs",
                table: "verification_runs");

            migrationBuilder.DropPrimaryKey(
                name: "pk_strong_entries",
                table: "strong_entries");

            migrationBuilder.RenameTable(
                name: "verification_runs",
                newName: "verification_run");

            migrationBuilder.RenameTable(
                name: "strong_entries",
                newName: "strong_entry");

            migrationBuilder.RenameIndex(
                name: "ix_strong_entries_strong_number",
                table: "strong_entry",
                newName: "ix_strong_entry_strong_number");

            migrationBuilder.AddPrimaryKey(
                name: "pk_verification_run",
                table: "verification_run",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_strong_entry",
                table: "strong_entry",
                column: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_verification_run",
                table: "verification_run");

            migrationBuilder.DropPrimaryKey(
                name: "pk_strong_entry",
                table: "strong_entry");

            migrationBuilder.RenameTable(
                name: "verification_run",
                newName: "verification_runs");

            migrationBuilder.RenameTable(
                name: "strong_entry",
                newName: "strong_entries");

            migrationBuilder.RenameIndex(
                name: "ix_strong_entry_strong_number",
                table: "strong_entries",
                newName: "ix_strong_entries_strong_number");

            migrationBuilder.AddPrimaryKey(
                name: "pk_verification_runs",
                table: "verification_runs",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_strong_entries",
                table: "strong_entries",
                column: "id");
        }
    }
}
