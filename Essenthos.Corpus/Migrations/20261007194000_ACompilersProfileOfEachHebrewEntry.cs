using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ACompilersProfileOfEachHebrewEntry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_profile",
                columns: table => new
                {
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    part_of_speech = table.Column<string>(type: "text", nullable: true),
                    gender = table.Column<string>(type: "text", nullable: true),
                    occurrences = table.Column<int>(type: "integer", nullable: false),
                    first_book = table.Column<int>(type: "integer", nullable: true),
                    first_chapter = table.Column<int>(type: "integer", nullable: true),
                    first_verse = table.Column<int>(type: "integer", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_profile", x => x.strong_number);
                    table.CheckConstraint("ck_strong_profile_first", "(first_book IS NULL) = (first_chapter IS NULL) AND (first_book IS NULL) = (first_verse IS NULL)");
                    table.CheckConstraint("ck_strong_profile_language", "language IN ('hbo', 'arc')");
                    table.CheckConstraint("ck_strong_profile_occurrences", "occurrences >= 0");
                    table.CheckConstraint("ck_strong_profile_source", "length(source) > 0");
                },
                comment: "A compiler's part of speech, gender, occurrence count and first verse for a Hebrew Strong entry. His analysis, not Strong's and not this corpus's: verify compares the count with BHSA's.");

            migrationBuilder.CreateIndex(
                name: "ix_strong_profile_strong_number",
                table: "strong_profile",
                column: "strong_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_profile");
        }
    }
}
