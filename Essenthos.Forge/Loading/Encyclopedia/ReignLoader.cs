using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Rulers">Rulers written, each with the periods he is drawn by.</param>
/// <param name="Statements">Verses setting somebody in a ruler's days.</param>
/// <param name="Missing">
/// Rows naming a record, a period or a verse the corpus does not hold, which is a corpus not yet
/// loaded or a slug the file has to follow.
/// </param>
internal sealed record ReignOutcome(bool AlreadyLoaded, int Rulers, int Statements, int Missing, TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the kings and the prophets of their days are already there"
            : $"{Rulers} rulers and {Statements} verses setting somebody in their days, in {Elapsed}"
              + (Missing > 0 ? $"; {Missing} rows name a record, period or verse the corpus does not hold" : "");
}

/// <summary>
/// The kings of the united kingdom, of Israel and of Judah, the rulers of the nations the text
/// brings into their reigns, and the prophets the text places in their days — each placement with
/// the verse that states it.
///
/// <para>
/// **The years are not here.** Every reign is a period the chronologies already date, and this
/// says only whose it is and under which kingdom it is drawn, so switching reckoning moves the
/// kings exactly as it moves the rest of the timeline and no reckoning is given years it does not
/// hold.
/// </para>
///
/// <para>
/// Idempotent: what the file asks for is compared with what is there, and only a difference
/// rewrites it — all of it, since the rows are this file's and nothing else writes them.
/// </para>
/// </summary>
internal sealed partial class ReignLoader(AppDbContext db, ILogger<ReignLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.ReignRecords.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<ReignOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var decision = Records();

        var slugs = decision.Rulers.Select(r => r.Slug)
            .Concat(decision.Statements.SelectMany(s => new[] { s.Person, s.Ruler, s.Through }))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var entities = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);

        var periodSlugs = decision.Rulers.SelectMany(r => r.Reigns).Select(r => r.Period).ToList();
        var periods = await db.Periods
            .Where(p => periodSlugs.Contains(p.Slug))
            .ToDictionaryAsync(p => p.Slug, p => (p.Id, p.EntityId), StringComparer.Ordinal, cancellationToken);

        var missing = 0;
        var reigns = new List<RulerReign>();
        foreach (var (ruler, position) in decision.Rulers.Select((ruler, at) => (ruler, at)))
        {
            if (!entities.TryGetValue(ruler.Slug, out var entityId))
            {
                logger.LogWarning(
                    "ReignRecords.json names the ruler \"{Slug}\" and the encyclopedia holds no record of that " +
                    "slug. Either the encyclopedia has not been loaded yet or the record was renamed, and the " +
                    "file has to follow it", ruler.Slug);
                missing++;
                continue;
            }

            if (ruler.Reigns.Count == 0)
            {
                reigns.Add(Reign(ruler, position, entityId, null, null, decision.Source));
                continue;
            }

            foreach (var reign in ruler.Reigns)
            {
                if (!periods.TryGetValue(reign.Period, out var period) || period.EntityId != entityId)
                {
                    logger.LogWarning(
                        "ReignRecords.json draws {Slug} by the period \"{Period}\", which the timeline does not " +
                        "hold as his. Either the chronology has not been loaded or the period was renamed",
                        ruler.Slug, reign.Period);
                    missing++;
                    continue;
                }

                reigns.Add(Reign(ruler, position, entityId, reign, period.Id, decision.Source));
            }
        }

        var statements = new List<ReignStatement>();
        foreach (var statement in decision.Statements)
        {
            if (!entities.TryGetValue(statement.Person, out var person)
                || !entities.TryGetValue(statement.Ruler, out var ruler)
                || Verses(statement.Verse) is not { } verse
                || (statement.Through is { } through && !entities.ContainsKey(through)))
            {
                logger.LogWarning(
                    "ReignRecords.json sets {Person} in the days of {Ruler} at {Verse}, and either a record is " +
                    "not in the encyclopedia or the verse is not written as ISA 1:1 or JER 1:1-3",
                    statement.Person, statement.Ruler, statement.Verse);
                missing++;
                continue;
            }

            statements.Add(new ReignStatement
            {
                EntityId = person,
                RulerEntityId = ruler,
                Role = statement.Role,
                Kind = statement.Kind,
                Year = statement.Year,
                CountedFrom = statement.Count,
                ThroughEntityId = statement.Through is { } by ? entities[by] : null,
                CanonicalBook = verse.Book,
                CanonicalChapter = verse.Chapter,
                CanonicalVerse = verse.Verse,
                EndVerse = verse.EndVerse,
                Source = decision.Source,
            });
        }

        var standing = await db.RulerReigns.ToListAsync(cancellationToken);
        var stated = await db.ReignStatements.ToListAsync(cancellationToken);
        if (standing.Select(Key).ToHashSet().SetEquals(reigns.Select(Key))
            && stated.Select(Key).ToHashSet().SetEquals(statements.Select(Key))
            && standing.Count == reigns.Count
            && stated.Count == statements.Count)
        {
            return Finished(new ReignOutcome(true, 0, 0, missing, started.Elapsed));
        }

        db.RulerReigns.RemoveRange(standing);
        db.ReignStatements.RemoveRange(stated);
        db.RulerReigns.AddRange(reigns);
        db.ReignStatements.AddRange(statements);
        await db.SaveChangesAsync(cancellationToken);

        return Finished(new ReignOutcome(
            false, reigns.Select(r => r.EntityId).Distinct().Count(), statements.Count, missing, started.Elapsed));
    }

    private ReignOutcome Finished(ReignOutcome outcome)
    {
        logger.LogInformation("The kings and the prophets of their days: {Outcome}", outcome);
        return outcome;
    }

    internal static ReignDecision Records()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new FileNotFoundException(
                               $"The embedded resource \"{Resource}\" is not in this assembly. It is added by " +
                               "the EmbeddedResource item in Essenthos.Forge.csproj; if the file was moved or " +
                               "renamed, that item and this name have to move with it.",
                               Resource);

        return JsonSerializer.Deserialize<ReignDecision>(stream, Shape)
               ?? throw new InvalidDataException($"The embedded resource \"{Resource}\" is empty.");
    }

    /// <summary>
    /// A reference as the file writes it, <c>ISA 1:1</c> or <c>JER 1:1-3</c>; null where it is not one.
    /// </summary>
    internal static (int Book, int Chapter, int Verse, int? EndVerse)? Verses(string reference)
    {
        if (Reference().Match(reference) is not { Success: true } match
            || BookReferences.ResolveOrdinal(match.Groups["book"].Value) is not { } book)
        {
            return null;
        }

        var verse = int.Parse(match.Groups["verse"].Value);
        int? end = match.Groups["end"].Success ? int.Parse(match.Groups["end"].Value) : null;
        return end is { } last && last <= verse
            ? null
            : (book, int.Parse(match.Groups["chapter"].Value), verse, end);
    }

    [GeneratedRegex(@"^(?<book>\S+) (?<chapter>\d+):(?<verse>\d+)(?:-(?<end>\d+))?$")]
    private static partial Regex Reference();

    private static RulerReign Reign(
        RulerRecord ruler, int position, int entityId, ReignRecord? reign, int? periodId, string source) =>
        new()
        {
            EntityId = entityId,
            PeriodId = periodId,
            Realm = ruler.Realm,
            Position = position,
            DrawnUnder = reign?.Over,
            Shared = reign?.Shared ?? false,
            Fallback = reign?.Fallback ?? false,
            Source = source,
        };

    private static object Key(RulerReign r) =>
        (r.EntityId, r.Realm, r.Position, r.PeriodId, r.DrawnUnder, r.Shared, r.Fallback);

    private static object Key(ReignStatement s) =>
        (s.EntityId, s.RulerEntityId, s.Role, s.Kind, s.Year, s.CountedFrom, s.ThroughEntityId,
            s.CanonicalBook, s.CanonicalChapter, s.CanonicalVerse, s.EndVerse);
}

/// <param name="Over">The kingdom this period is drawn under, where it is not the ruler's own.</param>
/// <param name="Shared">A co-regency, a rival reign or a disputed one.</param>
/// <param name="Fallback">Drawn only where a reckoning dates none of the ruler's other periods.</param>
internal sealed record ReignRecord(string Period, string? Over, bool Shared, bool Fallback);

/// <param name="Reigns">Empty for a ruler no reckoning dates, who is placed by the reigns he is set in.</param>
internal sealed record RulerRecord(string Slug, string Realm, IReadOnlyList<ReignRecord> Reigns);

/// <param name="Person">The prophet, the ruler of the nation, or the king who began to reign.</param>
/// <param name="Year">The ruler's year the verse gives, where it gives one.</param>
/// <param name="Through">The son the word came to, for a father whose page lists it.</param>
internal sealed record ReignStatementRecord(
    string Person,
    string Ruler,
    string Role,
    string Kind,
    string Verse,
    int? Year,
    string? Count,
    string? Through);

internal sealed record ReignDecision(
    string DecidedBy,
    string Policy,
    string Method,
    string Source,
    IReadOnlyList<RulerRecord> Rulers,
    IReadOnlyList<ReignStatementRecord> Statements);
