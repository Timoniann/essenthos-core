using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EntityImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_image",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    role = table.Column<string>(type: "text", nullable: false),
                    ordinal = table.Column<int>(type: "integer", nullable: false),
                    file = table.Column<string>(type: "text", nullable: false),
                    digest = table.Column<string>(type: "text", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    caption = table.Column<string>(type: "text", nullable: true),
                    credit = table.Column<string>(type: "text", nullable: false),
                    credit_url = table.Column<string>(type: "text", nullable: true),
                    licence = table.Column<string>(type: "text", nullable: false),
                    licence_url = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    focus_x = table.Column<double>(type: "double precision", nullable: true),
                    focus_y = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_image", x => x.id);
                    table.CheckConstraint("ck_entity_image_credit", "length(credit) > 0 AND length(licence) > 0");
                    table.CheckConstraint("ck_entity_image_focus", "(focus_x IS NULL OR focus_x BETWEEN 0 AND 1) AND (focus_y IS NULL OR focus_y BETWEEN 0 AND 1)");
                    table.CheckConstraint("ck_entity_image_kind", "kind IN ('public', 'generated')");
                    table.CheckConstraint("ck_entity_image_role", "role IN ('primary', 'gallery')");
                    table.CheckConstraint("ck_entity_image_size", "width > 0 AND height > 0");
                    table.ForeignKey(
                        name: "fk_entity_image_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_image_entity_id_file",
                table: "entity_image",
                columns: new[] { "entity_id", "file" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_image_one_primary_per_kind",
                table: "entity_image",
                columns: new[] { "entity_id", "kind" },
                unique: true,
                filter: "role = 'primary'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_image");
        }
    }
}
