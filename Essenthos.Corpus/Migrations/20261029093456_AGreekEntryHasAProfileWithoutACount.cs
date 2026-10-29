using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class AGreekEntryHasAProfileWithoutACount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_strong_profile_language",
                table: "strong_profile");

            migrationBuilder.AlterTable(
                name: "strong_profile",
                comment: "A compiler's part of speech, gender, occurrence count and first verse for a Hebrew Strong entry. His analysis, not Strong's and not this corpus's: verify compares the count with BHSA's. A Greek entry's part of speech and gender are a lexicon's, which counts nothing.",
                oldComment: "A compiler's part of speech, gender, occurrence count and first verse for a Hebrew Strong entry. His analysis, not Strong's and not this corpus's: verify compares the count with BHSA's.");

            migrationBuilder.AlterColumn<int>(
                name: "occurrences",
                table: "strong_profile",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddCheckConstraint(
                name: "ck_strong_profile_counted",
                table: "strong_profile",
                sql: "(occurrences IS NULL) = (language = 'grc')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_strong_profile_language",
                table: "strong_profile",
                sql: "language IN ('hbo', 'arc', 'grc')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_strong_profile_counted",
                table: "strong_profile");

            migrationBuilder.DropCheckConstraint(
                name: "ck_strong_profile_language",
                table: "strong_profile");

            migrationBuilder.AlterTable(
                name: "strong_profile",
                comment: "A compiler's part of speech, gender, occurrence count and first verse for a Hebrew Strong entry. His analysis, not Strong's and not this corpus's: verify compares the count with BHSA's.",
                oldComment: "A compiler's part of speech, gender, occurrence count and first verse for a Hebrew Strong entry. His analysis, not Strong's and not this corpus's: verify compares the count with BHSA's. A Greek entry's part of speech and gender are a lexicon's, which counts nothing.");

            migrationBuilder.AlterColumn<int>(
                name: "occurrences",
                table: "strong_profile",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_strong_profile_language",
                table: "strong_profile",
                sql: "language IN ('hbo', 'arc')");
        }
    }
}
