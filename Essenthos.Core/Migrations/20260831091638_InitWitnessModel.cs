using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class InitWitnessModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "text",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    name_native = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    language = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    versification = table.Column<string>(type: "text", nullable: false),
                    published_year = table.Column<int>(type: "integer", nullable: true),
                    source_url = table.Column<string>(type: "text", nullable: true),
                    rights_holder = table.Column<string>(type: "text", nullable: true),
                    licence = table.Column<string>(type: "text", nullable: true),
                    licence_url = table.Column<string>(type: "text", nullable: true),
                    redistribution = table.Column<string>(type: "text", nullable: false),
                    textual_family = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_text", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "book",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_ordinal = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    name_native = table.Column<string>(type: "text", nullable: true),
                    abbreviation = table.Column<string>(type: "text", nullable: true),
                    slug = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_book", x => x.id);
                    table.ForeignKey(
                        name: "fk_book_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "link",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_text_id = table.Column<int>(type: "integer", nullable: false),
                    to_text_id = table.Column<int>(type: "integer", nullable: false),
                    relation = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_link", x => x.id);
                    table.CheckConstraint("ck_link_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_link_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_link_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_link_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_link_text_from_text_id",
                        column: x => x.from_text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_link_text_to_text_id",
                        column: x => x.to_text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "text_relation",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_text_id = table.Column<int>(type: "integer", nullable: false),
                    to_text_id = table.Column<int>(type: "integer", nullable: false),
                    relation = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_text_relation", x => x.id);
                    table.CheckConstraint("ck_text_relation_distinct_texts", "\"from_text_id\" <> \"to_text_id\"");
                    table.ForeignKey(
                        name: "fk_text_relation_text_from_text_id",
                        column: x => x.from_text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_text_relation_text_to_text_id",
                        column: x => x.to_text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verse_link",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    from_text_id = table.Column<int>(type: "integer", nullable: false),
                    to_text_id = table.Column<int>(type: "integer", nullable: false),
                    relation = table.Column<string>(type: "text", nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verse_link", x => x.id);
                    table.CheckConstraint("ck_verse_link_confidence_range", "\"confidence\" IS NULL OR (\"confidence\" >= 0 AND \"confidence\" <= 1)");
                    table.CheckConstraint("ck_verse_link_inferred_carries_confidence", "\"method\" IN ('stated-by-source', 'manual') OR \"confidence\" IS NOT NULL");
                    table.CheckConstraint("ck_verse_link_source_not_empty", "length(btrim(\"source\")) > 0");
                    table.CheckConstraint("ck_verse_link_stated_carries_no_confidence", "\"method\" <> 'stated-by-source' OR \"confidence\" IS NULL");
                    table.ForeignKey(
                        name: "fk_verse_link_text_from_text_id",
                        column: x => x.from_text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_verse_link_text_to_text_id",
                        column: x => x.to_text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "chapter",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chapter", x => x.id);
                    table.ForeignKey(
                        name: "fk_chapter_book_book_id",
                        column: x => x.book_id,
                        principalTable: "book",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_chapter_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verse",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    book_id = table.Column<int>(type: "integer", nullable: false),
                    chapter_id = table.Column<int>(type: "integer", nullable: false),
                    chapter_number = table.Column<int>(type: "integer", nullable: false),
                    number = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verse", x => x.id);
                    table.ForeignKey(
                        name: "fk_verse_book_book_id",
                        column: x => x.book_id,
                        principalTable: "book",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_verse_chapter_chapter_id",
                        column: x => x.chapter_id,
                        principalTable: "chapter",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_verse_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verse_link_verse",
                columns: table => new
                {
                    verse_link_id = table.Column<int>(type: "integer", nullable: false),
                    verse_id = table.Column<int>(type: "integer", nullable: false),
                    side = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verse_link_verse", x => new { x.verse_link_id, x.verse_id, x.side });
                    table.ForeignKey(
                        name: "fk_verse_link_verse_verse_links_verse_link_id",
                        column: x => x.verse_link_id,
                        principalTable: "verse_link",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_verse_link_verse_verse_verse_id",
                        column: x => x.verse_id,
                        principalTable: "verse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "verse_reference",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    verse_id = table.Column<int>(type: "integer", nullable: false),
                    canonical_book = table.Column<int>(type: "integer", nullable: false),
                    canonical_chapter = table.Column<int>(type: "integer", nullable: false),
                    canonical_verse = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_verse_reference", x => x.id);
                    table.ForeignKey(
                        name: "fk_verse_reference_verse_verse_id",
                        column: x => x.verse_id,
                        principalTable: "verse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "word",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    text_id = table.Column<int>(type: "integer", nullable: false),
                    verse_id = table.Column<int>(type: "integer", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    trailer = table.Column<string>(type: "text", nullable: false),
                    lemma = table.Column<string>(type: "text", nullable: true),
                    strong_number = table.Column<string>(type: "text", nullable: true),
                    gloss = table.Column<string>(type: "text", nullable: true),
                    morphology = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    normalised_text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_word", x => x.id);
                    table.ForeignKey(
                        name: "fk_word_text_text_id",
                        column: x => x.text_id,
                        principalTable: "text",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_word_verse_verse_id",
                        column: x => x.verse_id,
                        principalTable: "verse",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "link_word",
                columns: table => new
                {
                    link_id = table.Column<long>(type: "bigint", nullable: false),
                    word_id = table.Column<long>(type: "bigint", nullable: false),
                    side = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_link_word", x => new { x.link_id, x.word_id, x.side });
                    table.ForeignKey(
                        name: "fk_link_word_links_link_id",
                        column: x => x.link_id,
                        principalTable: "link",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_link_word_word_word_id",
                        column: x => x.word_id,
                        principalTable: "word",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_book_text_id_canonical_ordinal",
                table: "book",
                columns: new[] { "text_id", "canonical_ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_book_text_id_position",
                table: "book",
                columns: new[] { "text_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_book_text_id_slug",
                table: "book",
                columns: new[] { "text_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chapter_book_id_number",
                table: "chapter",
                columns: new[] { "book_id", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_chapter_text_id",
                table: "chapter",
                column: "text_id");

            migrationBuilder.CreateIndex(
                name: "ix_link_from_text_id_to_text_id",
                table: "link",
                columns: new[] { "from_text_id", "to_text_id" });

            migrationBuilder.CreateIndex(
                name: "ix_link_to_text_id",
                table: "link",
                column: "to_text_id");

            migrationBuilder.CreateIndex(
                name: "ix_link_word_word_id",
                table: "link_word",
                column: "word_id");

            migrationBuilder.CreateIndex(
                name: "ix_text_slug",
                table: "text",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_text_relation_from_text_id_to_text_id",
                table: "text_relation",
                columns: new[] { "from_text_id", "to_text_id" });

            migrationBuilder.CreateIndex(
                name: "ix_text_relation_to_text_id",
                table: "text_relation",
                column: "to_text_id");

            migrationBuilder.CreateIndex(
                name: "ix_verse_book_id",
                table: "verse",
                column: "book_id");

            migrationBuilder.CreateIndex(
                name: "ix_verse_chapter_id",
                table: "verse",
                column: "chapter_id");

            migrationBuilder.CreateIndex(
                name: "ix_verse_text_id_book_id_chapter_number_number",
                table: "verse",
                columns: new[] { "text_id", "book_id", "chapter_number", "number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_verse_link_from_text_id_to_text_id",
                table: "verse_link",
                columns: new[] { "from_text_id", "to_text_id" });

            migrationBuilder.CreateIndex(
                name: "ix_verse_link_to_text_id",
                table: "verse_link",
                column: "to_text_id");

            migrationBuilder.CreateIndex(
                name: "ix_verse_link_verse_verse_id",
                table: "verse_link_verse",
                column: "verse_id");

            migrationBuilder.CreateIndex(
                name: "ix_verse_reference_canonical_book_canonical_chapter_canonical_",
                table: "verse_reference",
                columns: new[] { "canonical_book", "canonical_chapter", "canonical_verse" });

            migrationBuilder.CreateIndex(
                name: "ix_verse_reference_one_primary_per_verse",
                table: "verse_reference",
                column: "verse_id",
                unique: true,
                filter: "\"is_primary\"");

            migrationBuilder.CreateIndex(
                name: "ix_word_text_id_strong_number",
                table: "word",
                columns: new[] { "text_id", "strong_number" });

            migrationBuilder.CreateIndex(
                name: "ix_word_verse_id_position",
                table: "word",
                columns: new[] { "verse_id", "position" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "link_word");

            migrationBuilder.DropTable(
                name: "text_relation");

            migrationBuilder.DropTable(
                name: "verse_link_verse");

            migrationBuilder.DropTable(
                name: "verse_reference");

            migrationBuilder.DropTable(
                name: "link");

            migrationBuilder.DropTable(
                name: "word");

            migrationBuilder.DropTable(
                name: "verse_link");

            migrationBuilder.DropTable(
                name: "verse");

            migrationBuilder.DropTable(
                name: "chapter");

            migrationBuilder.DropTable(
                name: "book");

            migrationBuilder.DropTable(
                name: "text");
        }
    }
}
