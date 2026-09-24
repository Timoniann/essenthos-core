using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AnEvidentiaRunKeepsTheWordsItSaysHaveNoCounterpart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_evidentia_decision_abstention_has_reason",
                table: "evidentia_decision");

            migrationBuilder.AlterColumn<long>(
                name: "source_word_id",
                table: "evidentia_decision",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<string>(
                name: "absence",
                table: "evidentia_decision",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "anchor_source_word_id",
                table: "evidentia_decision",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "anchor_target_word_id",
                table: "evidentia_decision",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidentia_decision_absence_names_its_side",
                table: "evidentia_decision",
                sql: "\"absence\" IS NULL OR (\"kind\" IS NOT NULL AND \"confidence\" IS NOT NULL AND \"abstention\" IS NULL AND CASE \"absence\" WHEN 'expands' THEN \"source_word_id\" IS NOT NULL AND \"target_word_id\" IS NULL WHEN 'omits' THEN \"source_word_id\" IS NULL AND \"target_word_id\" IS NOT NULL ELSE FALSE END)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidentia_decision_abstention_has_reason",
                table: "evidentia_decision",
                sql: "\"target_word_id\" IS NOT NULL OR \"absence\" = 'expands' OR (\"abstention\" IS NOT NULL AND \"kind\" IS NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidentia_decision_only_an_omission_lacks_its_source",
                table: "evidentia_decision",
                sql: "\"source_word_id\" IS NOT NULL OR \"absence\" = 'omits'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_evidentia_decision_absence_names_its_side",
                table: "evidentia_decision");

            migrationBuilder.DropCheckConstraint(
                name: "ck_evidentia_decision_abstention_has_reason",
                table: "evidentia_decision");

            migrationBuilder.DropCheckConstraint(
                name: "ck_evidentia_decision_only_an_omission_lacks_its_source",
                table: "evidentia_decision");

            // An unrendered word of the original has no source word, and the column is required again.
            migrationBuilder.Sql("DELETE FROM evidentia_decision WHERE source_word_id IS NULL");

            migrationBuilder.DropColumn(
                name: "absence",
                table: "evidentia_decision");

            migrationBuilder.DropColumn(
                name: "anchor_source_word_id",
                table: "evidentia_decision");

            migrationBuilder.DropColumn(
                name: "anchor_target_word_id",
                table: "evidentia_decision");

            migrationBuilder.AlterColumn<long>(
                name: "source_word_id",
                table: "evidentia_decision",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_evidentia_decision_abstention_has_reason",
                table: "evidentia_decision",
                sql: "\"target_word_id\" IS NOT NULL OR (\"abstention\" IS NOT NULL AND \"kind\" IS NULL)");
        }
    }
}
