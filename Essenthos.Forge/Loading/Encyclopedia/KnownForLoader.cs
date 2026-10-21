using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">People whose clauses were written or rewritten: all of them on a cold corpus, none after.</param>
/// <param name="Clauses">How many clauses those people now hold.</param>
/// <param name="Removed">People the file no longer names, whose clauses were taken away.</param>
/// <param name="Missing">People the file names and the encyclopedia does not hold.</param>
internal sealed record KnownForOutcome(int Written, int Clauses, int Removed, int Missing, TimeSpan Elapsed)
{
    public override string ToString() =>
        Written == 0 && Removed == 0 && Missing == 0
            ? "what people are known for is already there"
            : $"what {Written} people are known for written in {Clauses} clauses, {Removed} people's taken away, " +
              $"{Missing} for people the encyclopedia does not hold, in {Elapsed}";
}

/// <summary>
/// What a person is known for — <em>built the ark and was saved from the Flood with his family</em>
/// — clause by clause, each with the verses that say it, in English and in each reader's language,
/// read from <c>KnownFor.json</c>.
///
/// <para>
/// Idempotent and atomic: the rows are this file's alone, a person is rewritten only where what the
/// file says of them differs from what is held, a person the file no longer names loses theirs, and
/// all of it is one transaction, so a failed write leaves what was there.
/// </para>
/// </summary>
internal sealed class KnownForLoader(AppDbContext db, ILogger<KnownForLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.KnownFor.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public Task<KnownForOutcome> Load(CancellationToken cancellationToken = default) =>
        Load(Read(), cancellationToken);

    internal async Task<KnownForOutcome> Load(KnownForFile file, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var wanted = file.Records.ToDictionary(r => r.Slug, r => Clauses(r, file.Source), StringComparer.Ordinal);
        var slugs = wanted.Keys.ToList();
        var entities = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug })
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
        var held = (await db.EntityKnownFor
                .Include(k => k.Texts)
                .Include(k => k.References)
                .ToListAsync(cancellationToken))
            .GroupBy(k => k.EntityId)
            .ToDictionary(g => g.Key, g => g.OrderBy(k => k.Ordinal).ToList());

        var missing = 0;
        foreach (var slug in slugs.Where(slug => !entities.ContainsKey(slug)))
        {
            logger.LogWarning(
                "KnownFor.json says what \"{Slug}\" is known for, and the encyclopedia holds no record of that slug. " +
                "Either it has not been loaded yet or the record was renamed, and the file has to follow it", slug);
            missing++;
        }

        var stale = new List<EntityKnownFor>();
        var fresh = new List<EntityKnownFor>();
        var written = 0;
        var clauses = 0;
        foreach (var (slug, entityId) in entities)
        {
            var clausesWanted = wanted[slug];
            var holding = held.GetValueOrDefault(entityId) ?? [];
            if (holding.Select(Key).SequenceEqual(clausesWanted.Select(Key), StringComparer.Ordinal))
            {
                continue;
            }

            stale.AddRange(holding);
            foreach (var clause in clausesWanted)
            {
                clause.EntityId = entityId;
                fresh.Add(clause);
            }

            written++;
            clauses += clausesWanted.Count;
        }

        var named = entities.Values.ToHashSet();
        var removed = 0;
        foreach (var (entityId, holding) in held.Where(pair => !named.Contains(pair.Key)))
        {
            stale.AddRange(holding);
            removed++;
        }

        if (stale.Count > 0 || fresh.Count > 0)
        {
            var own = db.Database.CurrentTransaction is null
                ? await db.Database.BeginTransactionAsync(cancellationToken)
                : null;
            try
            {
                // Two saves, because a person's new clauses take the ordinals their old ones hold.
                db.EntityKnownFor.RemoveRange(stale);
                await db.SaveChangesAsync(cancellationToken);
                db.EntityKnownFor.AddRange(fresh);
                await db.SaveChangesAsync(cancellationToken);
                if (own is not null)
                {
                    await own.CommitAsync(cancellationToken);
                }
            }
            finally
            {
                if (own is not null)
                {
                    await own.DisposeAsync();
                }
            }
        }

        var outcome = new KnownForOutcome(written, clauses, removed, missing, started.Elapsed);
        logger.LogInformation("What people are known for: {Outcome}", outcome);
        return outcome;
    }

    private static List<EntityKnownFor> Clauses(KnownForRecord record, string source)
    {
        if (record.Clauses.Count == 0)
        {
            throw new InvalidDataException(
                $"KnownFor.json names \"{record.Slug}\" and says nothing of them. Give the record at least one clause " +
                "or take it out of the file.");
        }

        return
        [
            .. record.Clauses.Select((clause, ordinal) => new EntityKnownFor
            {
                Ordinal = ordinal,
                English = Required(clause.English, record.Slug, "english"),
                Source = source,
                Texts =
                [
                    .. clause.Languages().Select(one => new EntityKnownForText { Language = one.Language, Text = one.Text }),
                ],
                References = [.. References(record.Slug, clause)],
            }),
        ];
    }

    private static IEnumerable<EntityKnownForReference> References(string slug, KnownForClause clause)
    {
        if (clause.References.Count == 0)
        {
            throw new InvalidDataException(
                $"A clause of \"{slug}\" in KnownFor.json cites no verse: \"{clause.English}\". Every clause rests on " +
                "the verses that say it.");
        }

        return clause.References.Select((written, position) =>
        {
            var span = ScriptureSpan.TryParse(written);
            if (span is not { FromVerse: { } verse, ToVerse: { } last, Nth: null })
            {
                throw new InvalidDataException(
                    $"\"{written}\", cited by \"{slug}\" in KnownFor.json, is not a run of verses. Write a book code, " +
                    "then a verse or a run of them: GEN 5:24, GEN 6:13-22 or GEN 6:13-8:19.");
            }

            return new EntityKnownForReference
            {
                Position = position,
                CanonicalBook = span.Book,
                CanonicalChapter = span.FromChapter,
                CanonicalVerse = verse,
                EndChapter = span.ToChapter,
                EndVerse = last,
            };
        });
    }

    private static string Required(string? text, string slug, string field) =>
        string.IsNullOrWhiteSpace(text)
            ? throw new InvalidDataException($"A clause of \"{slug}\" in KnownFor.json has no {field}.")
            : text;

    private static string Key(EntityKnownFor clause) =>
        string.Join(
            '|',
            clause.Ordinal,
            clause.English,
            clause.Source,
            string.Join(';', clause.Texts.OrderBy(t => t.Language, StringComparer.Ordinal).Select(t => $"{t.Language}={t.Text}")),
            string.Join(';', clause.References.OrderBy(r => r.Position)
                .Select(r => $"{r.CanonicalBook}.{r.CanonicalChapter}.{r.CanonicalVerse}-{r.EndChapter}.{r.EndVerse}")));

    internal static KnownForFile Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<KnownForFile>(stream, Shape)
               ?? throw new InvalidDataException($"{Resource} is empty. It should hold a source and the records.");
    }
}

/// <param name="Source">Who wrote and rendered the clauses and when, which every row carries.</param>
internal sealed record KnownForFile(string Source, IReadOnlyList<KnownForRecord> Records);

/// <summary>One person, by slug, and what they are known for, in the order it is told.</summary>
internal sealed record KnownForRecord(string Slug, IReadOnlyList<KnownForClause> Clauses);

/// <summary>
/// One clause, which reads after <em>known for</em>, in English and rendered, keyed by the language's
/// three-letter code, and the runs of verses that say it, as the record files write a span.
/// </summary>
internal sealed record KnownForClause(
    string English,
    string? Ukr,
    string? Deu,
    string? Spa,
    IReadOnlyList<string> References)
{
    public IEnumerable<(string Language, string Text)> Languages() =>
        new (string Language, string? Text)[] { ("ukr", Ukr), ("deu", Deu), ("spa", Spa) }
            .Where(one => !string.IsNullOrWhiteSpace(one.Text))
            .Select(one => (one.Language, one.Text!));
}
