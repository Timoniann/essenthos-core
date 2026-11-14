using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class EachFamilyIsOneStoredDocument : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "family_component",
                columns: table => new
                {
                    component = table.Column<int>(type: "integer", nullable: false),
                    prose = table.Column<string>(type: "text", nullable: false),
                    names = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false)
                        .Annotation("Npgsql:Compression:", "lz4"),
                    hash = table.Column<string>(type: "text", nullable: false),
                    people = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_component", x => new { x.component, x.prose, x.names });
                },
                comment: "Each connected family of family_member as the JSON document a tree is drawn from, per pair of languages, sent as it is. Made by the serving build (family-components/{prose}).");

            migrationBuilder.CreateTable(
                name: "family_member",
                columns: table => new
                {
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    component = table.Column<int>(type: "integer", nullable: false),
                    sex = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_member", x => x.entity_id);
                    table.ForeignKey(
                        name: "fk_family_member_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Every person and the connected family of family_tie they are in; somebody with no tie is a family of one. Loaded from records/derived/family-ties.");

            migrationBuilder.CreateTable(
                name: "family_tie",
                columns: table => new
                {
                    from_entity_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    to_entity_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: true),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: true),
                    canonical_verse = table.Column<int>(type: "integer", nullable: true),
                    component = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_family_tie", x => new { x.from_entity_id, x.type, x.to_entity_id });
                    table.ForeignKey(
                        name: "fk_family_tie_entity_from_entity_id",
                        column: x => x.from_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_family_tie_entity_to_entity_id",
                        column: x => x.to_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                },
                comment: "Every family tie a tree draws (parent, spouse, brother or sister, as entity_relationship holds it, no deity), with the connected family it is in. Loaded from records/derived/family-ties.");

            migrationBuilder.CreateTable(
                name: "record_line",
                columns: table => new
                {
                    prose = table.Column<string>(type: "text", nullable: false),
                    names = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    line = table.Column<string>(type: "text", nullable: true),
                    quoted = table.Column<bool>(type: "boolean", nullable: false),
                    local_line = table.Column<string>(type: "text", nullable: true),
                    descriptor_line = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_line", x => new { x.prose, x.names, x.slug });
                },
                comment: "The one plain line under a record's name for a language of words and a language of names, as the reader chooses it, with the two it was chosen from. Records by slug. Made by the serving build (record-cards/{prose}).");

            migrationBuilder.CreateTable(
                name: "record_name",
                columns: table => new
                {
                    names = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    local_name = table.Column<string>(type: "text", nullable: true),
                    distinguisher = table.Column<string>(type: "text", nullable: true),
                    sex = table.Column<string>(type: "text", nullable: true),
                    also_called = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_name", x => new { x.names, x.slug });
                },
                comment: "What each record is called in one language of names, as a card shows it: the heading, the local name, the source's line, the sex, the name it is also called. Records by slug. Made by the serving build (record-cards/{prose}).");

            migrationBuilder.CreateIndex(
                name: "ix_family_member_component",
                table: "family_member",
                column: "component");

            migrationBuilder.CreateIndex(
                name: "ix_family_member_slug",
                table: "family_member",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_family_tie_component",
                table: "family_tie",
                column: "component");

            migrationBuilder.CreateIndex(
                name: "ix_family_tie_to_entity_id",
                table: "family_tie",
                column: "to_entity_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "family_component");

            migrationBuilder.DropTable(
                name: "family_member");

            migrationBuilder.DropTable(
                name: "family_tie");

            migrationBuilder.DropTable(
                name: "record_line");

            migrationBuilder.DropTable(
                name: "record_name");
        }
    }
}
