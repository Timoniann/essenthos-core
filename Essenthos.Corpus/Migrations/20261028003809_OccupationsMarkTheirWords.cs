using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class OccupationsMarkTheirWords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_occupation_category",
                table: "occupation");

            migrationBuilder.DropColumn(
                name: "provenance",
                table: "person_occupation");

            migrationBuilder.DropColumn(
                name: "publication_id",
                table: "person_occupation");

            migrationBuilder.DropColumn(
                name: "source_sha256",
                table: "occupation_witness");

            migrationBuilder.DropColumn(
                name: "source_text",
                table: "occupation_witness");

            migrationBuilder.DropColumn(
                name: "publication_sha256",
                table: "person_occupation");

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "person_occupation",
                type: "text",
                nullable: false,
                defaultValue: "Essenthos, the occupation readings reviewed on 2026-10-27");

            migrationBuilder.AddColumn<string[]>(
                name: "strong_numbers",
                table: "occupation",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);

            migrationBuilder.CreateTable(
                name: "occupation_word",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    person_occupation_id = table.Column<int>(type: "integer", nullable: false),
                    word_id = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation_word", x => x.id);
                    table.ForeignKey(
                        name: "fk_occupation_word_person_occupations_person_occupation_id",
                        column: x => x.person_occupation_id,
                        principalTable: "person_occupation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_occupation_word_words_word_id",
                        column: x => x.word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_person_occupation_source",
                table: "person_occupation",
                sql: "length(btrim(source)) > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_occupation_category",
                table: "occupation",
                sql: "category IN ('trade', 'office', 'military_role', 'religious_role', 'heavenly_role', 'designation')");

            migrationBuilder.CreateIndex(
                name: "ix_occupation_word_person_occupation_id_word_id",
                table: "occupation_word",
                columns: new[] { "person_occupation_id", "word_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_occupation_word_word_id",
                table: "occupation_word",
                column: "word_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "occupation_word");

            migrationBuilder.DropCheckConstraint(
                name: "ck_person_occupation_source",
                table: "person_occupation");

            migrationBuilder.DropCheckConstraint(
                name: "ck_occupation_category",
                table: "occupation");

            migrationBuilder.DropColumn(
                name: "strong_numbers",
                table: "occupation");

            migrationBuilder.DropColumn(
                name: "source",
                table: "person_occupation");

            migrationBuilder.AddColumn<string>(
                name: "publication_sha256",
                table: "person_occupation",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "provenance",
                table: "person_occupation",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "publication_id",
                table: "person_occupation",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_sha256",
                table: "occupation_witness",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "source_text",
                table: "occupation_witness",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddCheckConstraint(
                name: "ck_occupation_category",
                table: "occupation",
                sql: "category IN ('trade', 'office', 'military_role', 'religious_role', 'heavenly_role')");
        }
    }
}
