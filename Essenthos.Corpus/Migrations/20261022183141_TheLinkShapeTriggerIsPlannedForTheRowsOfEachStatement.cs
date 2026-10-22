using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Essenthos.Core.Migrations
{
    /// <summary>
    /// The trigger that keeps a link's fingerprint the shape of its words plans its statements for the
    /// rows each statement wrote. As static SQL, PL/pgSQL planned them once per session, for the rows of
    /// the first statement it saw: after a writer had copied a hundred thousand link words, every
    /// one-row insert in the same session read the whole of <c>link_word</c>, about two seconds each,
    /// and replaying the EVIDENTIA ledger into a corpus being built would have taken days. Planned per
    /// statement, a one-row insert takes two milliseconds whatever came before it.
    /// </summary>
    public partial class TheLinkShapeTriggerIsPlannedForTheRowsOfEachStatement : Migration
    {
        private const string Shape =
            "md5(coalesce(string_agg(CASE WHEN lw.side = 'from' THEN 'f' ELSE 't' END || lw.word_id, ' ' " +
            "ORDER BY lw.side, lw.word_id), ''))::uuid";

        private const string Emptied = $"CASE WHEN count(lw.word_id) = 0 THEN NULL ELSE {Shape} END";

        private const string Added =
            $"""
             UPDATE link l SET fingerprint = shaped.fingerprint
             FROM (SELECT lw.link_id, {Shape} AS fingerprint
                   FROM link_word lw
                   WHERE lw.link_id IN (SELECT link_id FROM added)
                   GROUP BY lw.link_id) shaped
             WHERE l.id = shaped.link_id AND l.fingerprint IS DISTINCT FROM shaped.fingerprint
             """;

        private const string Removed =
            $"""
             UPDATE link l SET fingerprint = shaped.fingerprint
             FROM (SELECT t.id AS link_id, {Emptied} AS fingerprint
                   FROM link t
                   LEFT JOIN link_word lw ON lw.link_id = t.id
                   WHERE t.id IN (SELECT link_id FROM removed)
                   GROUP BY t.id) shaped
             WHERE l.id = shaped.link_id AND l.fingerprint IS DISTINCT FROM shaped.fingerprint
             """;

        private const string Changed =
            $"""
             UPDATE link l SET fingerprint = shaped.fingerprint
             FROM (SELECT t.id AS link_id, {Emptied} AS fingerprint
                   FROM link t
                   LEFT JOIN link_word lw ON lw.link_id = t.id
                   WHERE t.id IN (SELECT link_id FROM removed UNION SELECT link_id FROM added)
                   GROUP BY t.id) shaped
             WHERE l.id = shaped.link_id AND l.fingerprint IS DISTINCT FROM shaped.fingerprint
             """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                $"""
                 CREATE OR REPLACE FUNCTION link_shape_follows_its_words() RETURNS trigger
                 LANGUAGE plpgsql AS $$
                 BEGIN
                     IF TG_OP = 'INSERT' THEN
                         EXECUTE $sql${Added}$sql$;
                     ELSIF TG_OP = 'DELETE' THEN
                         EXECUTE $sql${Removed}$sql$;
                     ELSE
                         EXECUTE $sql${Changed}$sql$;
                     END IF;
                     RETURN NULL;
                 END $$;
                 """);

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder) =>
            migrationBuilder.Sql(
                $"""
                 CREATE OR REPLACE FUNCTION link_shape_follows_its_words() RETURNS trigger
                 LANGUAGE plpgsql AS $$
                 BEGIN
                     IF TG_OP = 'INSERT' THEN
                         {Added};
                     ELSIF TG_OP = 'DELETE' THEN
                         {Removed};
                     ELSE
                         {Changed};
                     END IF;
                     RETURN NULL;
                 END $$;
                 """);
    }
}
