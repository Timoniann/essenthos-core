using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class CommandmentsAndTopicsHaveTablesOfTheirOwn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "commandment",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "text", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false),
                    mishneh_torah_number = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commandment", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "topic",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_topic", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commandment_reference",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    commandment_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    first_verse = table.Column<int>(type: "integer", nullable: false),
                    last_verse = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commandment_reference", x => x.id);
                    table.ForeignKey(
                        name: "fk_commandment_reference_commandment_commandment_id",
                        column: x => x.commandment_id,
                        principalTable: "commandment",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "topic_reference",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    topic_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    first_verse = table.Column<int>(type: "integer", nullable: true),
                    last_verse = table.Column<int>(type: "integer", nullable: true),
                    heading = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_topic_reference", x => x.id);
                    table.ForeignKey(
                        name: "fk_topic_reference_topic_topic_id",
                        column: x => x.topic_id,
                        principalTable: "topic",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commandment_kind_number",
                table: "commandment",
                columns: new[] { "kind", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_commandment_reference_canonical_book_canonical_chapter",
                table: "commandment_reference",
                columns: new[] { "canonical_book", "canonical_chapter" });

            migrationBuilder.CreateIndex(
                name: "ix_commandment_reference_commandment_id",
                table: "commandment_reference",
                column: "commandment_id");

            migrationBuilder.CreateIndex(
                name: "ix_topic_slug",
                table: "topic",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_topic_reference_canonical_book_canonical_chapter",
                table: "topic_reference",
                columns: new[] { "canonical_book", "canonical_chapter" });

            migrationBuilder.CreateIndex(
                name: "ix_topic_reference_topic_id",
                table: "topic_reference",
                column: "topic_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commandment_reference");

            migrationBuilder.DropTable(
                name: "topic_reference");

            migrationBuilder.DropTable(
                name: "commandment");

            migrationBuilder.DropTable(
                name: "topic");
        }
    }
}
