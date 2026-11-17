using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <summary>
    /// Every link word carries its word's verse, and an index keyed by verse then word holds the row's link and side, so the
    /// links of a chapter's words are one range of adjacent pages: word ids are hashes of keys, and by word id the same rows lie
    /// all over the index. A writer that leaves the verse out has it filled from the word, so the database keeps it true
    /// whoever writes the words; the mapping load writes it itself with the triggers off.
    /// </summary>
    public partial class LinkWordsCarryTheirWordsVerse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "verse_id",
                table: "link_word",
                type: "integer",
                nullable: true);

            migrationBuilder.Sql(
                """
                UPDATE link_word lw SET verse_id = w.verse_id FROM word w WHERE w.id = lw.word_id;
                ALTER TABLE link_word ALTER COLUMN verse_id SET NOT NULL;

                CREATE FUNCTION link_word_takes_its_words_verse() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    SELECT w.verse_id INTO NEW.verse_id FROM word w WHERE w.id = NEW.word_id;
                    RETURN NEW;
                END $$;

                CREATE TRIGGER link_word_takes_its_words_verse BEFORE INSERT ON link_word
                FOR EACH ROW WHEN (NEW.verse_id IS NULL) EXECUTE FUNCTION link_word_takes_its_words_verse();
                """);

            migrationBuilder.CreateIndex(
                name: "ix_link_word_verse_id_word_id",
                table: "link_word",
                columns: new[] { "verse_id", "word_id" })
                .Annotation("Npgsql:IndexInclude", new[] { "link_id", "side" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_link_word_verse_id_word_id",
                table: "link_word");

            migrationBuilder.Sql(
                """
                DROP TRIGGER link_word_takes_its_words_verse ON link_word;
                DROP FUNCTION link_word_takes_its_words_verse();
                """);

            migrationBuilder.DropColumn(
                name: "verse_id",
                table: "link_word");
        }
    }
}
