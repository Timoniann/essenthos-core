using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Persons whose sex, its source or the gender of their name changed on this run.</param>
/// <param name="Sexed">How many persons hold a sex in all.</param>
/// <param name="Silent">How many persons nothing gives a sex.</param>
internal sealed record PersonSexOutcome(int Written, int Sexed, int Silent, TimeSpan Elapsed)
{
    public override string ToString() =>
        Written == 0
            ? $"the sexes of {Sexed} persons are already written, {Silent} persons have none"
            : $"{Written} persons given their sex, {Sexed} hold one, {Silent} have none, in {Elapsed}";
}

/// <summary>
/// The sex of a person, written on the record so that nothing has to work it out when it is read. A
/// person's ruling comes first (<c>sexes</c> in OwnNames.json). Then the dataset's value, where it gave
/// one. Where it gave none: the gendered word of the relationships the person is the subject of, where
/// they agree, or else the grammar of the name the Greek prints.
///
/// <para>
/// Grammar speaks only of persons: a place or a title has no sex, whatever gender its name takes. The
/// gender of the name is kept beside the sex, because a record read by this project answers from its
/// own rows and not from the dataset's value (<see cref="OursOnlyRecords"/>), and because the two may
/// differ. A record of the dataset's whose name says otherwise keeps the dataset's value here unless
/// ruled. Idempotent.
/// </para>
/// </summary>
internal sealed class PersonSexLoader(AppDbContext db, ILogger<PersonSexLoader> logger)
{
    /// <summary>The witness whose morphology states the gender of the words that name a record.</summary>
    private const string GreekWitness = "NESTLE1904";

    public Task<PersonSexOutcome> Load(CancellationToken cancellationToken) =>
        Load(OwnNameLoader.Read().Sexes ?? [], cancellationToken);

    internal async Task<PersonSexOutcome> Load(IReadOnlyList<RuledSex> rulings, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var written = await db.Database.ExecuteSqlRawAsync(
            """
            WITH stated AS (
                SELECT entity_id AS id, relation FROM entity_descriptor
                UNION ALL
                SELECT from_entity_id, type FROM entity_relationship
            ), clauses AS (
                SELECT id,
                       bool_or(relation = ANY(@male)) AS man,
                       bool_or(relation = ANY(@female)) AS woman
                FROM stated
                GROUP BY id
            ), names AS (
                SELECT a.entity_id AS id, min(w.morphology->>'gender') AS gender,
                       count(DISTINCT w.morphology->>'gender') AS genders
                FROM word w
                JOIN text t ON t.id = w.text_id AND t.slug = @witness
                JOIN strong_entry s ON s.strong_number = w.strong_number
                JOIN word_entity a ON a.word_id = w.id
                WHERE (a.note IS NULL OR a.note NOT LIKE 'through %')
                  AND w.morphology->>'gender' IN ('masculine', 'feminine')
                  AND lower(left(s.lemma, 1)) <> left(s.lemma, 1)
                GROUP BY a.entity_id
            ), said AS (
                SELECT e.id,
                       CASE WHEN c.man AND NOT c.woman THEN 'male'
                            WHEN c.woman AND NOT c.man THEN 'female' END AS by_relationship,
                       CASE WHEN n.genders = 1 THEN
                           CASE n.gender WHEN 'feminine' THEN 'female' ELSE 'male' END END AS by_name
                FROM entity e
                LEFT JOIN clauses c ON c.id = e.id
                LEFT JOIN names n ON n.id = e.id
                WHERE e.kind = @person
            ), wanted AS (
                SELECT r.id, r.by_name AS name_gender,
                       CASE WHEN ruled.sex IS NOT NULL THEN ruled.sex
                            WHEN e.sex IS NOT NULL AND e.sex_source IS NULL THEN e.sex
                            ELSE coalesce(r.by_relationship, r.by_name) END AS sex,
                       CASE WHEN ruled.sex IS NOT NULL THEN @ruling
                            WHEN e.sex IS NOT NULL AND e.sex_source IS NULL THEN NULL
                            WHEN r.by_relationship IS NOT NULL THEN @relationship
                            WHEN r.by_name IS NOT NULL THEN @grammar END AS sex_source
                FROM said r
                JOIN entity e ON e.id = r.id
                LEFT JOIN unnest(@ruled_slugs, @ruled_source_ids, @ruled_sexes) AS ruled(slug, source_id, sex)
                       ON ruled.slug = e.slug AND ruled.source_id = e.source_id
            )
            UPDATE entity e
            SET sex = w.sex, sex_source = w.sex_source, name_gender = w.name_gender
            FROM wanted w
            WHERE w.id = e.id
              AND (e.sex IS DISTINCT FROM w.sex
                   OR e.sex_source IS DISTINCT FROM w.sex_source
                   OR e.name_gender IS DISTINCT FROM w.name_gender)
            """,
            [
                new NpgsqlParameter("male", OursOnlyRecords.Male.ToArray()),
                new NpgsqlParameter("female", OursOnlyRecords.Female.ToArray()),
                new NpgsqlParameter("witness", GreekWitness),
                new NpgsqlParameter("person", EnumSpelling.Of(EntityKind.Person)),
                new NpgsqlParameter("relationship", SexSources.Relationship),
                new NpgsqlParameter("grammar", SexSources.Grammar),
                new NpgsqlParameter("ruling", SexSources.Ruling),
                new NpgsqlParameter("ruled_slugs", rulings.Select(r => r.Entity).ToArray()),
                new NpgsqlParameter("ruled_source_ids", rulings.Select(r => r.SourceId).ToArray()),
                new NpgsqlParameter("ruled_sexes", rulings.Select(r => r.Sex).ToArray()),
            ],
            cancellationToken);

        var persons = db.Entities.Where(e => e.Kind == EntityKind.Person);
        var sexed = await persons.CountAsync(e => e.Sex != null, cancellationToken);
        var silent = await persons.CountAsync(e => e.Sex == null, cancellationToken);
        var outcome = new PersonSexOutcome(written, sexed, silent, clock.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }
}
