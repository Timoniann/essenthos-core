using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <inheritdoc />
    [DbContext(typeof(AppDbContext))]
    [Migration("20260910110000_CompanyAVerseDoesNotSpeakOf")]
    public partial class CompanyAVerseDoesNotSpeakOf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The companion-of clauses already loaded whose cited verse speaks of no company.
            //
            // The loader has refused these since essenthos-core:409879d -- in intention. It looked
            // for the King James words under the slug "kjv" where the corpus writes "KJV", found no
            // text, took that for a corpus without the text, and refused nothing. 115 of the 200
            // loaded companion-of clauses cite a verse with no word of accompaniment in it: list
            // readings, "Shallum, Amariah, and Joseph", which is what the loader's check is for.
            //
            // The words and the slug are written out here rather than read from the loader, because
            // what happened to this database is a fact about it, and retuning the list later must not
            // silently change what this migration meant when it ran.
            //
            // The relationships go first and by the same test, because they are read off these
            // clauses and their own loader writes only for an entity it holds no rows for: leaving
            // them would keep the claim on the page after the clause under it was gone.
            const string SpeaksOfNoCompany =
                """
                NOT EXISTS (
                    SELECT 1
                    FROM word w
                    JOIN text t ON t.id = w.text_id AND t.slug = 'KJV'
                    JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
                    WHERE r.canonical_book = {0}.canonical_book
                      AND r.canonical_chapter = {0}.canonical_chapter
                      AND r.canonical_verse = {0}.canonical_verse
                      AND lower(coalesce(w.normalised_text, w.text)) IN ('with', 'companion', 'companions', 'fellow',
                          'fellows', 'together', 'beside', 'accompanied', 'accompanying', 'along'))
                """;

            migrationBuilder.Sql(
                $"""
                DELETE FROM entity_relationship rel
                WHERE rel.type = 'companion-of'
                  AND rel.source LIKE 'read from Scripture%'
                  AND {string.Format(SpeaksOfNoCompany, "rel")};
                """);

            migrationBuilder.Sql(
                $"""
                DELETE FROM entity_descriptor d
                WHERE d.relation = 'companion-of'
                  AND {string.Format(SpeaksOfNoCompany, "d")};
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Nothing to put back. What was removed is in the generation pass's files under
            // Resources, and a loader that can now find the text would refuse it a second time.
        }
    }
}
