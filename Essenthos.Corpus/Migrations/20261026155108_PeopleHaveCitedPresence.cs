using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class PeopleHaveCitedPresence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "person_presence",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    person_id = table.Column<int>(type: "integer", nullable: false),
                    place_id = table.Column<int>(type: "integer", nullable: false),
                    category = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    addresses = table.Column<int[]>(type: "integer[]", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    evidence = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_person_presence", x => x.id);
                    table.CheckConstraint("ck_person_presence_category", "category IN ('present', 'buried')");
                    table.CheckConstraint("ck_person_presence_evidence", "cardinality(addresses) > 0 AND jsonb_typeof(evidence) = 'array' AND jsonb_array_length(evidence) > 0");
                    table.CheckConstraint("ck_person_presence_source", "length(btrim(source)) > 0");
                    table.ForeignKey(
                        name: "fk_person_presence_entity_person_id",
                        column: x => x.person_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_person_presence_entity_place_id",
                        column: x => x.place_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_person_presence_person_id_place_id_category",
                table: "person_presence",
                columns: new[] { "person_id", "place_id", "category" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_person_presence_place_id",
                table: "person_presence",
                column: "place_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "person_presence");
        }
    }
}
