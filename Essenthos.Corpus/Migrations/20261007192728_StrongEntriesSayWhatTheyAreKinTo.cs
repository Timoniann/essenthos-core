using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class StrongEntriesSayWhatTheyAreKinTo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_relation",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_number = table.Column<string>(type: "text", nullable: false),
                    to_number = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    hedged = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    statement = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_relation", x => x.id);
                    table.CheckConstraint("ck_strong_relation_kind", "kind IN ('same-as', 'from', 'same-root-as', 'form-of', 'variant', 'contracted-from', 'corresponds-to', 'loan-from', 'patronymic', 'patrial', 'patronymic-or-patrial', 'compare', 'primitive', 'root', 'unclassified')");
                    table.CheckConstraint("ck_strong_relation_source", "length(statement) > 0 AND length(source) > 0");
                    table.CheckConstraint("ck_strong_relation_to_number", "(to_number IS NULL) = (kind = 'primitive')");
                },
                comment: "What one Strong entry's etymology says about another, with the clause it was read from and whose reading it is. Keyed on numbers: a claim about two words of the language.");

            migrationBuilder.CreateIndex(
                name: "ix_strong_relation_from_number",
                table: "strong_relation",
                column: "from_number");

            migrationBuilder.CreateIndex(
                name: "ix_strong_relation_source_from_number_position",
                table: "strong_relation",
                columns: new[] { "source", "from_number", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_strong_relation_to_number",
                table: "strong_relation",
                column: "to_number");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_relation");
        }
    }
}
