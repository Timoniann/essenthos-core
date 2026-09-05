using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheGentilicsStrongStates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "strong_gentilic",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    strong_number = table.Column<string>(type: "text", nullable: false),
                    origin_number = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    origin_entity_id = table.Column<int>(type: "integer", nullable: true),
                    statement = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_strong_gentilic", x => x.id);
                    table.ForeignKey(
                        name: "fk_strong_gentilic_entity_origin_entity_id",
                        column: x => x.origin_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_strong_gentilic_origin_entity_id",
                table: "strong_gentilic",
                column: "origin_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_strong_gentilic_origin_number",
                table: "strong_gentilic",
                column: "origin_number");

            migrationBuilder.CreateIndex(
                name: "ix_strong_gentilic_strong_number",
                table: "strong_gentilic",
                column: "strong_number",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "strong_gentilic");
        }
    }
}
