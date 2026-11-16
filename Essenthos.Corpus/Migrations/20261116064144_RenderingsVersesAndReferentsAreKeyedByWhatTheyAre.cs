using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class RenderingsVersesAndReferentsAreKeyedByWhatTheyAre : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ordinal",
                table: "word_referent",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // A word's referents were written in the order the source lists them, so the old ids give the place among them.
            migrationBuilder.Sql("""
                UPDATE word_referent w SET ordinal = r.place
                FROM (SELECT id, row_number() OVER (PARTITION BY word_id, source ORDER BY id) - 1 AS place FROM word_referent) r
                WHERE r.id = w.id
                """);

            migrationBuilder.DropPrimaryKey(
                name: "pk_word_referent",
                table: "word_referent");

            migrationBuilder.DropIndex(
                name: "ix_word_referent_word_id_referent_word_id_source",
                table: "word_referent");

            migrationBuilder.DropPrimaryKey(
                name: "pk_entity_verse",
                table: "entity_verse");

            migrationBuilder.DropIndex(
                name: "ix_entity_verse_entity_id",
                table: "entity_verse");

            migrationBuilder.DropIndex(
                name: "ix_entity_verse_entity_id_canonical_book_canonical_chapter_can",
                table: "entity_verse");

            migrationBuilder.DropPrimaryKey(
                name: "pk_entity_search_spelling",
                table: "entity_search_spelling");

            migrationBuilder.DropIndex(
                name: "ix_entity_search_spelling_entity_id_folded",
                table: "entity_search_spelling");

            migrationBuilder.DropPrimaryKey(
                name: "pk_entity_rendering",
                table: "entity_rendering");

            migrationBuilder.DropIndex(
                name: "ix_entity_rendering_entity_id_text_id_form",
                table: "entity_rendering");

            migrationBuilder.DropColumn(
                name: "id",
                table: "word_referent");

            migrationBuilder.DropColumn(
                name: "id",
                table: "entity_verse");

            migrationBuilder.DropColumn(
                name: "id",
                table: "entity_search_spelling");

            migrationBuilder.DropColumn(
                name: "id",
                table: "entity_rendering");

            migrationBuilder.AddPrimaryKey(
                name: "pk_word_referent",
                table: "word_referent",
                columns: new[] { "word_id", "referent_word_id", "source" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_entity_verse",
                table: "entity_verse",
                columns: new[] { "entity_id", "canonical_book", "canonical_chapter", "canonical_verse", "source" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_entity_search_spelling",
                table: "entity_search_spelling",
                columns: new[] { "entity_id", "folded" });

            migrationBuilder.AddPrimaryKey(
                name: "pk_entity_rendering",
                table: "entity_rendering",
                columns: new[] { "entity_id", "text_id", "form" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "pk_word_referent",
                table: "word_referent");

            migrationBuilder.DropPrimaryKey(
                name: "pk_entity_verse",
                table: "entity_verse");

            migrationBuilder.DropPrimaryKey(
                name: "pk_entity_search_spelling",
                table: "entity_search_spelling");

            migrationBuilder.DropPrimaryKey(
                name: "pk_entity_rendering",
                table: "entity_rendering");

            migrationBuilder.DropColumn(
                name: "ordinal",
                table: "word_referent");

            migrationBuilder.AddColumn<long>(
                name: "id",
                table: "word_referent",
                type: "bigint",
                nullable: false,
                defaultValue: 0L)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "entity_verse",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "entity_search_spelling",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddColumn<int>(
                name: "id",
                table: "entity_rendering",
                type: "integer",
                nullable: false,
                defaultValue: 0)
                .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn);

            migrationBuilder.AddPrimaryKey(
                name: "pk_word_referent",
                table: "word_referent",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_entity_verse",
                table: "entity_verse",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_entity_search_spelling",
                table: "entity_search_spelling",
                column: "id");

            migrationBuilder.AddPrimaryKey(
                name: "pk_entity_rendering",
                table: "entity_rendering",
                column: "id");

            migrationBuilder.CreateIndex(
                name: "ix_word_referent_word_id_referent_word_id_source",
                table: "word_referent",
                columns: new[] { "word_id", "referent_word_id", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_verse_entity_id",
                table: "entity_verse",
                column: "entity_id");

            migrationBuilder.CreateIndex(
                name: "ix_entity_verse_entity_id_canonical_book_canonical_chapter_can",
                table: "entity_verse",
                columns: new[] { "entity_id", "canonical_book", "canonical_chapter", "canonical_verse", "source" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_search_spelling_entity_id_folded",
                table: "entity_search_spelling",
                columns: new[] { "entity_id", "folded" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_entity_rendering_entity_id_text_id_form",
                table: "entity_rendering",
                columns: new[] { "entity_id", "text_id", "form" },
                unique: true);
        }
    }
}
