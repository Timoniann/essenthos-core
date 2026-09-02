using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class Encyclopedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_book_text_text_id",
                table: "book");

            migrationBuilder.DropForeignKey(
                name: "fk_chapter_text_text_id",
                table: "chapter");

            migrationBuilder.DropForeignKey(
                name: "fk_link_text_from_text_id",
                table: "link");

            migrationBuilder.DropForeignKey(
                name: "fk_link_text_to_text_id",
                table: "link");

            migrationBuilder.DropForeignKey(
                name: "fk_link_word_links_link_id",
                table: "link_word");

            migrationBuilder.DropForeignKey(
                name: "fk_link_word_word_word_id",
                table: "link_word");

            migrationBuilder.DropForeignKey(
                name: "fk_verse_link_verse_verse_links_verse_link_id",
                table: "verse_link_verse");

            migrationBuilder.CreateTable(
                name: "entity",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "text", nullable: false),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    distinguisher = table.Column<string>(type: "text", nullable: true),
                    sex = table.Column<string>(type: "text", nullable: true),
                    tribe = table.Column<string>(type: "text", nullable: true),
                    place_kind = table.Column<string>(type: "text", nullable: true),
                    modern_equivalent = table.Column<string>(type: "text", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    source_id = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "text", nullable: false),
                    open_bible_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "entity_name",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "text", nullable: false),
                    hebrew = table.Column<string>(type: "text", nullable: true),
                    hebrew_transliterated = table.Column<string>(type: "text", nullable: true),
                    greek = table.Column<string>(type: "text", nullable: true),
                    greek_transliterated = table.Column<string>(type: "text", nullable: true),
                    meaning = table.Column<string>(type: "text", nullable: true),
                    strong_number = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_name", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_name_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_relationship",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_entity_id = table.Column<int>(type: "integer", nullable: false),
                    to_entity_id = table.Column<int>(type: "integer", nullable: false),
                    type = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: true),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: true),
                    canonical_verse = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_relationship", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_relationship_entity_from_entity_id",
                        column: x => x.from_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_entity_relationship_entity_to_entity_id",
                        column: x => x.to_entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "entity_verse",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entity_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    label = table.Column<string>(type: "text", nullable: true),
                    disputed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_entity_verse", x => x.id);
                    table.ForeignKey(
                        name: "fk_entity_verse_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "event",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: true),
                    entity_id = table.Column<int>(type: "integer", nullable: true),
                    year_from_creation = table.Column<int>(type: "integer", nullable: true),
                    bce_year = table.Column<int>(type: "integer", nullable: true),
                    age_at_event = table.Column<int>(type: "integer", nullable: true),
                    calculation = table.Column<string>(type: "text", nullable: true),
                    canonical_book = table.Column<int>(type: "integer", nullable: true),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: true),
                    canonical_verse = table.Column<int>(type: "integer", nullable: true),
                    location = table.Column<string>(type: "text", nullable: true),
                    ussher_anno_mundi = table.Column<int>(type: "integer", nullable: true),
                    ussher_bce_year = table.Column<int>(type: "integer", nullable: true),
                    ussher_paragraph = table.Column<string>(type: "text", nullable: true),
                    shulman_anno_mundi = table.Column<int>(type: "integer", nullable: true),
                    notes = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_event", x => x.id);
                    table.ForeignKey(
                        name: "fk_event_entity_entity_id",
                        column: x => x.entity_id,
                        principalTable: "entity",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "ix_entity_kind",
                table: "entity",
                column: "kind");

            migrationBuilder.CreateIndex(
                name: "ix_entity_slug",
                table: "entity",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_source_id",
                table: "entity",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_name_entity_id",
                table: "entity_name",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_name_strong_number",
                table: "entity_name",
                column: "strong_number");

            migrationBuilder.CreateIndex(
                name: "ix_entity_relationship_from_entity_id",
                table: "entity_relationship",
                column: "from_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_relationship_to_entity_id",
                table: "entity_relationship",
                column: "to_entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_verse_canonical_book_canonical_chapter_canonical_ver",
                table: "entity_verse",
                columns: new[] { "canonical_book", "canonical_chapter", "canonical_verse" });

            migrationBuilder.CreateIndex(
                name: "ix_entity_verse_entity_id",
                table: "entity_verse",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_entity_id",
                table: "event",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_event_slug",
                table: "event",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_event_year_from_creation",
                table: "event",
                column: "year_from_creation");

            migrationBuilder.AddForeignKey(
                name: "fk_book_texts_text_id",
                table: "book",
                column: "text_id",
                principalTable: "text",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_chapter_texts_text_id",
                table: "chapter",
                column: "text_id",
                principalTable: "text",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_link_texts_from_text_id",
                table: "link",
                column: "from_text_id",
                principalTable: "text",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_link_texts_to_text_id",
                table: "link",
                column: "to_text_id",
                principalTable: "text",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_link_word_link_link_id",
                table: "link_word",
                column: "link_id",
                principalTable: "link",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_link_word_words_word_id",
                table: "link_word",
                column: "word_id",
                principalTable: "word",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_verse_link_verse_verse_link_verse_link_id",
                table: "verse_link_verse",
                column: "verse_link_id",
                principalTable: "verse_link",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_book_texts_text_id",
                table: "book");

            migrationBuilder.DropForeignKey(
                name: "fk_chapter_texts_text_id",
                table: "chapter");

            migrationBuilder.DropForeignKey(
                name: "fk_link_texts_from_text_id",
                table: "link");

            migrationBuilder.DropForeignKey(
                name: "fk_link_texts_to_text_id",
                table: "link");

            migrationBuilder.DropForeignKey(
                name: "fk_link_word_link_link_id",
                table: "link_word");

            migrationBuilder.DropForeignKey(
                name: "fk_link_word_words_word_id",
                table: "link_word");

            migrationBuilder.DropForeignKey(
                name: "fk_verse_link_verse_verse_link_verse_link_id",
                table: "verse_link_verse");

            migrationBuilder.DropTable(
                name: "entity_name");

            migrationBuilder.DropTable(
                name: "entity_relationship");

            migrationBuilder.DropTable(
                name: "entity_verse");

            migrationBuilder.DropTable(
                name: "event");

            migrationBuilder.DropTable(
                name: "entity");

            migrationBuilder.AddForeignKey(
                name: "fk_book_text_text_id",
                table: "book",
                column: "text_id",
                principalTable: "text",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_chapter_text_text_id",
                table: "chapter",
                column: "text_id",
                principalTable: "text",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_link_text_from_text_id",
                table: "link",
                column: "from_text_id",
                principalTable: "text",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_link_text_to_text_id",
                table: "link",
                column: "to_text_id",
                principalTable: "text",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_link_word_links_link_id",
                table: "link_word",
                column: "link_id",
                principalTable: "link",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_link_word_word_word_id",
                table: "link_word",
                column: "word_id",
                principalTable: "word",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "fk_verse_link_verse_verse_links_verse_link_id",
                table: "verse_link_verse",
                column: "verse_link_id",
                principalTable: "verse_link",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
