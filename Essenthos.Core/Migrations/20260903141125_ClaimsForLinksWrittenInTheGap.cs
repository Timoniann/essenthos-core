using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    public partial class ClaimsForLinksWrittenInTheGap : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // One repair, for the links written between `LinkClaims` and PRB-0198 being found.
            //
            // The first migration backfilled every link that existed when it ran, and nothing was
            // taught to keep it up — so the Berean's 403,343 links, every aligner run since and
            // everything reloaded carried no claim, and the agreement measure sat at 4,664 counting
            // the migration rather than the corpus.
            //
            // A backfill was the wrong fix and still is: every loader now writes its claim in the
            // same transaction as the link, which is what stops this recurring, and an integrity
            // check reports any link nothing claims. This statement only repairs the gap those two
            // could not reach backwards into.
            migrationBuilder.Sql(
                """
                INSERT INTO link_claim (link_id, method, confidence, source, note)
                SELECT l.id, l.method, l.confidence, l.source, l.note
                FROM link l
                -- Not "has no claim at all". A link Clear Bible had already corroborated carried
                -- its claim and not its own, so that test skipped exactly the 99,007 links whose
                -- second claim was the point -- leaving a link whose source column says the Berean
                -- and whose only claim says Clear Bible.
                WHERE NOT EXISTS (
                    SELECT 1 FROM link_claim c
                    WHERE c.link_id = l.id AND c.method = l.method AND c.source = l.source)
                ON CONFLICT DO NOTHING
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
