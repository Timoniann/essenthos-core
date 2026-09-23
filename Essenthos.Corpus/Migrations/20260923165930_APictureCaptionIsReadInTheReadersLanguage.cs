using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class APictureCaptionIsReadInTheReadersLanguage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "entity_image_caption",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_image_id = table.Column<int>(type: "integer", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    caption = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_image_caption", x => x.id);
                    table.CheckConstraint("ck_entity_image_caption_text", "length(caption) > 0");
                    table.ForeignKey(
                        name: "fk_entity_image_caption_entity_image_entity_image_id",
                        column: x => x.entity_image_id,
                        principalTable: "entity_image",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_image_caption_entity_image_id_language",
                table: "entity_image_caption",
                columns: new[] { "entity_image_id", "language" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "entity_image_caption");
        }
    }
}
