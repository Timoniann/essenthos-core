using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheRunARowWasReadFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Which file of which generation pass a row was read from, so a re-ask can replace what
            // is loaded. Nothing else on a row can tell one pass from another: the re-ask that
            // widened Lot's description ran as claude-sonnet-5 on 2026-09-09, and so did the batch
            // it corrects, so the credit a reader sees is the same string on both and the loader
            // comparing them concluded it had already stored the answer (PRB-0449).
            //
            // Null on every row already here, and deliberately not backfilled. A blank answers for
            // no file, so the next load reads every described entity again from the files on disk
            // and writes the run as it goes -- which is exactly what has to happen once for the
            // re-ask to reach a corpus that already held the first answer. After that the column is
            // set and the load is a no-op again.

            migrationBuilder.AddColumn<string>(
                name: "run",
                table: "entity_name_form",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "run",
                table: "entity_descriptor",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "run",
                table: "entity_name_form");

            migrationBuilder.DropColumn(
                name: "run",
                table: "entity_descriptor");
        }
    }
}
