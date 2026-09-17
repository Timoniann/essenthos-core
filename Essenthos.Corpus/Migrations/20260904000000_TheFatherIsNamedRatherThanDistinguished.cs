using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheFatherIsNamedRatherThanDistinguished : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The second of the dataset's two entities named YHVH, given a name of its own.
            //
            // Both are called YHVH there, told apart by a sample of their titles, so a search for
            // the name returned two rows a reader had nothing to choose between. A distinguisher
            // under a repeated name is a weaker instrument than a name. What the dataset says this
            // one is, it says in its own labels -- Father in heaven, Father, Abba Father, Holy
            // Father, Righteous Father -- and all 352 of its namings are in New Testament books.
            //
            // The dataset's own name is kept in the notes: a name it chose is a thing it said, and
            // this replaces it in the reader rather than denying it.
            migrationBuilder.Sql(
                """
                UPDATE entity
                SET name = 'God the Father',
                    distinguisher = 'as the New Testament names him (MAT 5:16)',
                    notes = trim(concat(
                        'The dataset names this entity YHVH, the same as the God of Israel, and '
                        || 'tells the two apart by a sample of their titles. The name here is the '
                        || 'one its own labels give it; the dataset''s is kept in this note, '
                        || 'because a name it chose is a thing it said.',
                        ' ', notes))
                WHERE source_id = 'person:YHVH_2'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible. Two rows with one name and no way to choose between them is not a
            // state worth being able to return to.
        }
    }
}
