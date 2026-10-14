using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class DivineRecordsSayNothingOfTheDataset : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The notes the loader wrote on Jesus, the God of Israel and the Father told the reader how
            // BibleData held them and what this corpus moved. A note is read on the record's page, and
            // the loader no longer writes them; loading the encyclopedia is idempotent on "are there
            // any entities at all", so this takes them off the corpus already here. A note that held
            // the dataset's own words after ours keeps those.
            migrationBuilder.Sql(
                """
                WITH said(source_id, sentence) AS (VALUES
                    ('essenthos:jesus', $$BibleData holds the God of Israel and Jesus as one entity and the Father as another. This corpus separates them. New Testament references that name Jesus plainly were moved here; those that say only God or Lord stayed on the divine name and are marked disputed, because which of the two they mean is a reading of the text and not a fact about the dataset. The titles came with the references that carry them, so a name is here where the namings are.$$),
                    ('person:YHVH_1', $$The dataset gives this entity and the Father the same name and tells them apart by a sample of their titles — for this one, "Holy, Holy, Holy (ISA 6:3) and too many others to fit here" — which does not say which of the two it is. This is the one the divine name belongs to, named through the whole canon. It is also the entity Jesus is folded into, and the New Testament namings that plainly mean him were moved to his own entry.$$),
                    ('person:YHVH_2', $$The dataset names this entity YHVH, the same as the God of Israel, and tells the two apart by a sample of their titles. The name here is the one its own labels give it; the dataset's is kept in this note, because a name it chose is a thing it said.$$))
                UPDATE entity e
                SET notes = NULLIF(btrim(substr(e.notes, length(said.sentence) + 1)), '')
                FROM said
                WHERE e.source_id = said.source_id
                  AND starts_with(e.notes, said.sentence);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible: the notes said nothing a reader should be shown again.
        }
    }
}
