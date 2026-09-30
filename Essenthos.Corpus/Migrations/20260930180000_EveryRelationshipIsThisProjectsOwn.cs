using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260930180000_EveryRelationshipIsThisProjectsOwn")]
    public partial class EveryRelationshipIsThisProjectsOwn : Migration
    {
        /// <summary>What every relationship row the dataset stated begins its source with.</summary>
        private const string Dataset = "BibleData";

        private const string TwoWitnesses =
            "One entity standing in one relation to another. Two witnesses speak here and every row says which: "
            + "BibleData's edge list under its own category and its own relation names, and the clauses this corpus "
            + "read from Scripture under theirs. Nothing settles them into one row -- what a reader is shown is "
            + "settled the way an annotation is, by claim standing and then confidence.";

        private const string OursAlone =
            "One entity standing in one relation to another, as this project holds it: read from the verse the "
            + "row names by the model or the person its source names, in this project's own relation words. No "
            + "dataset's row is here.";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The dataset's relationship rows leave the table, the ones the owner had removed in his
            // console with the ones still shown to no reader: the encyclopedia is not loaded again on a
            // corpus that already holds it, so the rows a cold load no longer writes are taken out here.
            // A database migrated before anything is loaded holds none, and this deletes nothing.
            migrationBuilder.Sql(
                $"""
                 DELETE FROM entity_relationship WHERE source LIKE '{Dataset}%';
                 """);

            // The column marked a dataset's row the owner removed, and no row of ours was ever marked.
            migrationBuilder.DropColumn(
                name: "withdrawn",
                table: "entity_relationship");

            migrationBuilder.AlterTable(
                name: "entity_relationship",
                comment: OursAlone,
                oldComment: TwoWitnesses);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The rows are not brought back: they are the dataset's, and its file still holds them.
            migrationBuilder.AlterTable(
                name: "entity_relationship",
                comment: TwoWitnesses,
                oldComment: OursAlone);

            migrationBuilder.AddColumn<bool>(
                name: "withdrawn",
                table: "entity_relationship",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
