using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class TheTwoDivineNamesAreToldApart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The encyclopedia's two entities named YHVH, told apart in the loaded corpus.
            //
            // The loader now writes these, but loading the encyclopedia is idempotent on "are there
            // any entities at all" -- which is right, because rebuilding it would cascade away the
            // events, names, relationships and verse references hanging off every entity to correct
            // two strings. So the loader keeps a newly loaded corpus right and this brings the one
            // already here.
            //
            // Addressed by source id rather than by slug: the slug is ours and is disambiguated with
            // a number, while person:YHVH_1 and person:YHVH_2 are the keys the loader matches on.
            migrationBuilder.Sql(
                """
                UPDATE entity
                SET distinguisher = 'the God of Israel',
                    notes = trim(concat(
                        'The dataset gives this entity and the Father the same name and tells them '
                        || 'apart by a sample of their titles — for this one, "Holy, Holy, Holy '
                        || '(ISA 6:3) and too many others to fit here" — which does not say which of '
                        || 'the two it is. This is the one the divine name belongs to, named through '
                        || 'the whole canon. It is also the entity Jesus is folded into, and the New '
                        || 'Testament namings that plainly mean him were moved to his own entry.',
                        ' ', notes))
                WHERE source_id = 'person:YHVH_1';

                UPDATE entity
                SET distinguisher = 'the Father, whom the New Testament names (MAT 5:16)'
                WHERE source_id = 'person:YHVH_2'
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible. The attribute this replaces is a sample of one entity's own titles and
            // says nothing about which of the two entities it belongs to; it is kept in the notes.
        }
    }
}
