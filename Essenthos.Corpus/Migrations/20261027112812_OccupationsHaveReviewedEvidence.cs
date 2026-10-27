using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class OccupationsHaveReviewedEvidence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "occupation",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation", x => x.id);
                    table.CheckConstraint("ck_occupation_category", "category IN ('trade', 'office', 'military_role', 'religious_role')");
                });

            migrationBuilder.CreateTable(
                name: "occupation_label",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    occupation_id = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    masculine = table.Column<string>(type: "text", nullable: false),
                    feminine = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation_label", x => x.id);
                    table.CheckConstraint("ck_occupation_label_language", "language IN ('eng', 'ukr', 'deu', 'spa')");
                    table.ForeignKey(
                        name: "fk_occupation_label_occupation_occupation_id",
                        column: x => x.occupation_id,
                        principalTable: "occupation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "person_occupation",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    person_id = table.Column<int>(type: "integer", nullable: false),
                    occupation_id = table.Column<int>(type: "integer", nullable: false),
                    publication_id = table.Column<string>(type: "text", nullable: false),
                    publication_sha256 = table.Column<string>(type: "text", nullable: false),
                    provenance = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_occupation", x => x.id);
                    table.ForeignKey(
                        name: "fk_person_occupation_entity_person_id",
                        column: x => x.person_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_person_occupation_occupation_occupation_id",
                        column: x => x.occupation_id,
                        principalTable: "occupation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "occupation_witness",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    person_occupation_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    quote = table.Column<string>(type: "text", nullable: false),
                    source_text = table.Column<string>(type: "text", nullable: false),
                    source_sha256 = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_occupation_witness", x => x.id);
                    table.CheckConstraint("ck_occupation_witness_address", "canonical_book BETWEEN 1 AND 93 AND canonical_chapter > 0 AND canonical_verse >= 0");
                    table.CheckConstraint("ck_occupation_witness_kind", "kind IN ('profession', 'identity')");
                    table.ForeignKey(
                        name: "fk_occupation_witness_person_occupations_person_occupation_id",
                        column: x => x.person_occupation_id,
                        principalTable: "person_occupation",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_occupation_category",
                table: "occupation",
                column: "category");

            migrationBuilder.CreateIndex(
                name: "ix_occupation_slug",
                table: "occupation",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_occupation_label_occupation_id_language",
                table: "occupation_label",
                columns: new[] { "occupation_id", "language" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_occupation_witness_person_occupation_id",
                table: "occupation_witness",
                column: "person_occupation_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_occupation_occupation_id",
                table: "person_occupation",
                column: "occupation_id");

            migrationBuilder.CreateIndex(
                name: "ix_person_occupation_person_id_occupation_id",
                table: "person_occupation",
                columns: new[] { "person_id", "occupation_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "occupation_label");

            migrationBuilder.DropTable(
                name: "occupation_witness");

            migrationBuilder.DropTable(
                name: "person_occupation");

            migrationBuilder.DropTable(
                name: "occupation");
        }
    }
}
