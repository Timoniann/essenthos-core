using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <summary>
    /// The relationship table stops being one dataset's.
    ///
    /// It held BibleData's edge list and nothing else, so a row needed no provenance: whoever read
    /// it knew. The descriptor clauses this corpus reads from Scripture are relationships of the
    /// same shape and now reach the same table, and a table holding two witnesses with no per-row
    /// source can only present them as though they were one -- which is what
    /// <c>entity_verse.source</c> was added to stop happening to the verse lists.
    ///
    /// So the same three columns every other claim in this corpus carries, with the same four
    /// provenance constraints, and one more that is this table's own: everything but a witness's
    /// testimony names the verse it was read from. The column stays nullable because BibleData
    /// states 40 of its rows without a reference and there is nothing to be done about that but say
    /// so; inventing an address for them would be a citation a reader cannot follow dressed as one
    /// they can.
    /// </summary>
    public partial class TheRelationshipTableSaysWhoSaysSo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterTable(
                name: "entity_relationship",
                comment: "One entity standing in one relation to another. Two witnesses speak here and every row says which: BibleData's edge list under its own category and its own relation names, and the clauses this corpus read from Scripture under theirs. Nothing settles them into one row -- what a reader is shown is settled the way an annotation is, by claim standing and then confidence.");

            migrationBuilder.AddColumn<double>(
                name: "confidence",
                table: "entity_relationship",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "method",
                table: "entity_relationship",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "entity_relationship",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Every row the table holds before this is BibleData's, because until now the table was
            // BibleData's: the loader is the only writer of it and its 5,448 rows all come from
            // BibleData-PersonRelationship.csv. So the credit is stated rather than guessed, and it
            // is stated here rather than left to the loader -- the loader will not run again over a
            // database that already holds these rows, and a source column full of empty strings is
            // exactly the "credited to nobody" the column was added to stop.
            //
            // Before the constraints, and that ordering is the whole of it: an empty source fails
            // "source not empty" and an empty method fails "an inference carries a confidence", so
            // adding them first would refuse the migration on every database the corpus is in.
            migrationBuilder.Sql(
                """
                UPDATE entity_relationship
                SET method = 'stated-by-source',
                    source = 'BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0';
                """);

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_relationship_confidence_range",
                table: "entity_relationship",
                sql: "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_relationship_inferred_carries_confidence",
                table: "entity_relationship",
                sql: "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_relationship_read_names_a_verse",
                table: "entity_relationship",
                sql: "\"method\" = 'stated-by-source' OR (\"canonical_book\" IS NOT NULL AND \"canonical_chapter\" IS NOT NULL AND \"canonical_verse\" IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_relationship_source_not_empty",
                table: "entity_relationship",
                sql: "length(btrim(\"source\")) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_entity_relationship_stated_carries_no_confidence",
                table: "entity_relationship",
                sql: "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_relationship_confidence_range",
                table: "entity_relationship");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_relationship_inferred_carries_confidence",
                table: "entity_relationship");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_relationship_read_names_a_verse",
                table: "entity_relationship");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_relationship_source_not_empty",
                table: "entity_relationship");

            migrationBuilder.DropCheckConstraint(
                name: "ck_entity_relationship_stated_carries_no_confidence",
                table: "entity_relationship");

            migrationBuilder.DropColumn(
                name: "confidence",
                table: "entity_relationship");

            migrationBuilder.DropColumn(
                name: "method",
                table: "entity_relationship");

            migrationBuilder.DropColumn(
                name: "source",
                table: "entity_relationship");

            migrationBuilder.AlterTable(
                name: "entity_relationship",
                oldComment: "One entity standing in one relation to another. Two witnesses speak here and every row says which: BibleData's edge list under its own category and its own relation names, and the clauses this corpus read from Scripture under theirs. Nothing settles them into one row -- what a reader is shown is settled the way an annotation is, by claim standing and then confidence.");
        }
    }
}
