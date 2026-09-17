using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class JesusKeepsHisOwnRelations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The relations that belong to Jesus of Nazareth, moved off the divine name.
            //
            // BibleData holds the God of Israel and Jesus as one entity. This corpus separates
            // them, and the separation reached the verse references and not the relationships --
            // so the twelve apostles were apostles of the God of Israel, Mary was his bearer, and
            // the encyclopedia said "YHVH brother of James" at Matthew 13:55.
            //
            // The loader now writes these correctly, but loading the encyclopedia is idempotent on
            // "are there any entities at all", so this brings the corpus already here.
            //
            // Both directions of a tie are separate rows with different words, and both move, or
            // the tie comes apart -- apostle against master, disciple against rabbi, bearer against
            // "born by", patron against client. The New Testament test is not redundant beside the
            // list: "master" is the reverse of "apostle" twelve times in Matthew and the reverse of
            // "servant" nine times from Genesis to Jeremiah, and Moses is not a servant of Jesus.
            migrationBuilder.Sql(
                """
                UPDATE entity_relationship r
                SET from_entity_id = (SELECT id FROM entity WHERE source_id = 'essenthos:jesus')
                WHERE r.from_entity_id = (SELECT id FROM entity WHERE source_id = 'person:YHVH_1')
                  AND r.canonical_book >= 40
                  AND lower(r.type) IN ('apostle','master','disciple','rabbi','brother','bearer',
                                        'born by','patron','client');

                UPDATE entity_relationship r
                SET to_entity_id = (SELECT id FROM entity WHERE source_id = 'essenthos:jesus')
                WHERE r.to_entity_id = (SELECT id FROM entity WHERE source_id = 'person:YHVH_1')
                  AND r.canonical_book >= 40
                  AND lower(r.type) IN ('apostle','master','disciple','rabbi','brother','bearer',
                                        'born by','patron','client')
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Not reversible, and putting the apostles back under the divine name is not a state
            // worth being able to return to.
        }
    }
}
