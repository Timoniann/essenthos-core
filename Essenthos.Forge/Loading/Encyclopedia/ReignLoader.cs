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
/// <param name="Lengths">Kings given the length of reign the text states.</param>
/// <param name="Fields">Verses saying where a prophet spoke, came from or was sent.</param>
/// <param name="Verdicts">Kings given the mark the text's judgment of them comes to.</param>
/// <param name="Ages">Ages the text gives.</param>
/// <param name="Events">Verses dating a carrying away or the return by a ruler's year.</param>
/// <param name="Missing">
/// Rows naming a record, a period or a verse the corpus does not hold, which is a corpus not yet
/// loaded or a slug the file has to follow.
/// </param>
internal sealed record ReignOutcome(
    bool AlreadyLoaded,
    int Rulers,
    int Statements,
    int Lengths,
    int Fields,
    int Verdicts,
    int Ages,
    int Events,
    int Missing,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the kings and the prophets of their days are already there"
            : $"{Rulers} rulers, {Statements} verses setting somebody in their days, {Lengths} lengths of " +
              $"reign, {Fields} verses placing a prophet, {Verdicts} kings marked right, evil or mixed, " +
              $"{Ages} ages and {Events} verses dating a carrying away or the return, in {Elapsed}"
              + (Missing > 0 ? $"; {Missing} rows name a record, period or verse the corpus does not hold" : "");
}

/// <summary>
/// The kings of the united kingdom, of Israel and of Judah, the rulers of the nations the text
/// brings into their reigns, and the prophets the text places in their days — each placement with
/// the verse that states it — and beside them how long the text says each king reigned, the name he
/// reigned under where his record is headed by another, and where each prophet spoke. With them
/// goes what the rulings say of the kings: the mark each is drawn with and the verses it is quoted
/// from, the ages the text gives, and the carryings away and the return by the rulers' years.
///
/// <para>
/// **The years are not here.** Every reign is a period the chronologies already date, and this
/// says only whose it is and under which kingdom it is drawn, so switching reckoning moves the
/// kings exactly as it moves the rest of the timeline and no reckoning is given years it does not
/// hold.
/// </para>
///
/// <para>
/// Idempotent: what the two files ask for is compared with what is there, and only a difference
/// rewrites it — all of it, since the rows are these files' and nothing else writes them.
/// </para>
/// </summary>
internal sealed partial class ReignLoader(AppDbContext db, ILogger<ReignLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.ReignRecords.json";

    private const string RulingsResource = "Essenthos.Core.Loading.Encyclopedia.ReignRulings.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public async Task<ReignOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var decision = Records();
        var rulings = Rulings();
        await CloseWhatTheDatasetLeftOpen(cancellationToken);

        var slugs = decision.Rulers.Select(r => r.Slug)
            .Concat(decision.Statements.SelectMany(s => new[] { s.Person, s.Ruler, s.Through }))
            .Concat(decision.Prophets.SelectMany(p => p.Fields.Select(f => f.Place).Prepend(p.Slug)))
            .Concat(rulings.Verdicts.Select(v => v.King))
            .Concat(rulings.Ages.Select(a => a.Person))
            .Concat(rulings.Events.SelectMany(e => e.Datings.Select(d => d.Ruler)))
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

        var lengths = new List<ReignLength>();
        foreach (var ruler in decision.Rulers)
        {
            if (ruler.Reigned is not { } reigned || !entities.TryGetValue(ruler.Slug, out var king))
            {
                continue;
            }

            if (Verses(reigned.Verse) is not { } verse)
            {
                logger.LogWarning(
                    "ReignRecords.json gives {Slug} a length of reign at {Verse}, which is not written as 1KI 16:8",
                    ruler.Slug, reigned.Verse);
                missing++;
                continue;
            }

            lengths.Add(new ReignLength
            {
                EntityId = king,
                Years = reigned.Years,
                Months = reigned.Months,
                Days = reigned.Days,
                CanonicalBook = verse.Book,
                CanonicalChapter = verse.Chapter,
                CanonicalVerse = verse.Verse,
                Source = decision.Source,
            });
        }

        var thrones = new List<ThroneName>();
        foreach (var ruler in decision.Rulers)
        {
            if (ruler.Throne is not { } throne || !entities.TryGetValue(ruler.Slug, out var king))
            {
                continue;
            }

            if (Verses(throne.Verse) is not { } verse)
            {
                logger.LogWarning(
                    "ReignRecords.json gives {Slug} a throne name at {Verse}, which is not written as 2KI 23:34",
                    ruler.Slug, throne.Verse);
                missing++;
                continue;
            }

            thrones.AddRange(throne.Names.Select(name => new ThroneName
            {
                EntityId = king,
                Language = name.Key,
                Name = name.Value,
                CanonicalBook = verse.Book,
                CanonicalChapter = verse.Chapter,
                CanonicalVerse = verse.Verse,
                Source = decision.Source,
            }));
        }

        var fields = new List<ProphetField>();
        foreach (var prophet in decision.Prophets)
        {
            foreach (var (field, position) in prophet.Fields.Select((field, at) => (field, at)))
            {
                if (!entities.TryGetValue(prophet.Slug, out var person)
                    || Verses(field.Verse) is not { } verse
                    || (field.Place is { } place && !entities.ContainsKey(place)))
                {
                    logger.LogWarning(
                        "ReignRecords.json places {Prophet} in {Realm} at {Verse}, and either a record is not in " +
                        "the encyclopedia or the verse is not written as AMO 7:13 or AMO 7:13-15",
                        prophet.Slug, field.Realm, field.Verse);
                    missing++;
                    continue;
                }

                fields.Add(new ProphetField
                {
                    EntityId = person,
                    Realm = field.Realm,
                    Kind = field.Kind,
                    PlaceEntityId = field.Place is { } at ? entities[at] : null,
                    CanonicalBook = verse.Book,
                    CanonicalChapter = verse.Chapter,
                    CanonicalVerse = verse.Verse,
                    EndVerse = verse.EndVerse,
                    Position = position,
                    Source = decision.Source,
                });
            }
        }

        var verdicts = new List<RulerVerdict>();
        foreach (var verdict in rulings.Verdicts)
        {
            if (!entities.TryGetValue(verdict.King, out var king)
                || verdict.Witnesses.Any(witness => witness.Verses.Any(verse => Verses(verse) is null)))
            {
                logger.LogWarning(
                    "ReignRulings.json marks {King}, and either the encyclopedia holds no record of that slug or " +
                    "one of the verses the mark is quoted from is not written as 1KI 15:11 or 1KI 11:4-6",
                    verdict.King);
                missing++;
                continue;
            }

            verdicts.Add(new RulerVerdict
            {
                EntityId = king,
                Mark = verdict.Mark,
                Source = rulings.Source,
                Witnesses =
                [
                    .. verdict.Witnesses.Select((witness, position) => new RulerVerdictWitness
                    {
                        Witness = witness.Book,
                        Mark = witness.Mark,
                        Basis = witness.Basis,
                        Position = position,
                        Passages = [.. witness.Verses.Select((verse, at) => Passage(Verses(verse)!.Value, at))],
                    }),
                ],
            });
        }

        var ages = new List<StatedAge>();
        foreach (var (age, position) in rulings.Ages.Select((age, at) => (age, at)))
        {
            if (!entities.TryGetValue(age.Person, out var person) || Verses(age.Verse) is not { } verse)
            {
                logger.LogWarning(
                    "ReignRulings.json gives {Person} an age at {Verse}, and either the encyclopedia holds no " +
                    "record of that slug or the verse is not written as 2KI 18:2",
                    age.Person, age.Verse);
                missing++;
                continue;
            }

            ages.Add(new StatedAge
            {
                EntityId = person,
                Kind = age.Kind,
                Years = age.Years,
                About = age.About,
                CanonicalBook = verse.Book,
                CanonicalChapter = verse.Chapter,
                CanonicalVerse = verse.Verse,
                Position = position,
                Source = rulings.Source,
            });
        }

        var dated = rulings.Events.Select(e => e.Event).OfType<string>().ToList();
        var timeline = await db.Events
            .Where(e => dated.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
        var events = new List<ReignEvent>();
        foreach (var (happened, position) in rulings.Events.Select((happened, at) => (happened, at)))
        {
            int? onTheTimeline = null;
            if (happened.Event is { } slug)
            {
                if (timeline.TryGetValue(slug, out var id))
                {
                    onTheTimeline = id;
                }
                else
                {
                    logger.LogWarning(
                        "ReignRulings.json says {Event} is the timeline's \"{Slug}\", which the timeline does not " +
                        "hold. Either the chronology has not been loaded or the event was renamed; the verses " +
                        "still date it",
                        happened.Slug, slug);
                    missing++;
                }
            }

            foreach (var dating in happened.Datings)
            {
                if (!entities.TryGetValue(dating.Ruler, out var ruler) || Verses(dating.Verse) is not { } verse)
                {
                    logger.LogWarning(
                        "ReignRulings.json dates {Event} by {Ruler} at {Verse}, and either the encyclopedia holds " +
                        "no record of that slug or the verse is not written as 2KI 17:6 or 2KI 18:10-11",
                        happened.Slug, dating.Ruler, dating.Verse);
                    missing++;
                    continue;
                }

                events.Add(new ReignEvent
                {
                    Slug = happened.Slug,
                    Kind = happened.Kind,
                    Realm = happened.Realm,
                    Position = position,
                    TimelineEventId = onTheTimeline,
                    RulerEntityId = ruler,
                    Year = dating.Year,
                    CanonicalBook = verse.Book,
                    CanonicalChapter = verse.Chapter,
                    CanonicalVerse = verse.Verse,
                    EndVerse = verse.EndVerse,
                    Source = rulings.Source,
                });
            }
        }

        var standing = await db.RulerReigns.ToListAsync(cancellationToken);
        var stated = await db.ReignStatements.ToListAsync(cancellationToken);
        var measured = await db.ReignLengths.ToListAsync(cancellationToken);
        var placed = await db.ProphetFields.ToListAsync(cancellationToken);
        var named = await db.ThroneNames.ToListAsync(cancellationToken);
        var judged = await db.RulerVerdicts
            .Include(v => v.Witnesses).ThenInclude(w => w.Passages)
            .ToListAsync(cancellationToken);
        var aged = await db.StatedAges.ToListAsync(cancellationToken);
        var befallen = await db.ReignEvents.ToListAsync(cancellationToken);
        if (Same(standing, reigns, Key) && Same(stated, statements, Key)
            && Same(measured, lengths, Key) && Same(placed, fields, Key) && Same(named, thrones, Key)
            && Same([.. judged.SelectMany(Keys)], [.. verdicts.SelectMany(Keys)], key => key)
            && Same(aged, ages, Key) && Same(befallen, events, Key))
        {
            return Finished(new ReignOutcome(true, 0, 0, 0, 0, 0, 0, 0, missing, started.Elapsed));
        }

        db.RulerReigns.RemoveRange(standing);
        db.ReignStatements.RemoveRange(stated);
        db.ReignLengths.RemoveRange(measured);
        db.ProphetFields.RemoveRange(placed);
        db.ThroneNames.RemoveRange(named);
        db.RulerVerdicts.RemoveRange(judged);
        db.StatedAges.RemoveRange(aged);
        db.ReignEvents.RemoveRange(befallen);
        db.RulerReigns.AddRange(reigns);
        db.ReignStatements.AddRange(statements);
        db.ReignLengths.AddRange(lengths);
        db.ProphetFields.AddRange(fields);
        db.ThroneNames.AddRange(thrones);
        db.RulerVerdicts.AddRange(verdicts);
        db.StatedAges.AddRange(ages);
        db.ReignEvents.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);

        return Finished(new ReignOutcome(
            false, reigns.Select(r => r.EntityId).Distinct().Count(), statements.Count, lengths.Count, fields.Count,
            verdicts.Count, ages.Count, events.Count, missing, started.Elapsed));
    }

    private ReignOutcome Finished(ReignOutcome outcome)
    {
        logger.LogInformation("The kings and the prophets of their days: {Outcome}", outcome);
        return outcome;
    }

    internal static ReignDecision Records() => Embedded<ReignDecision>(Resource);

    internal static ReignRulings Rulings() => Embedded<ReignRulings>(RulingsResource);

    private static T Embedded<T>(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
                           ?? throw new FileNotFoundException(
                               $"The embedded resource \"{resource}\" is not in this assembly. It is added by " +
                               "the EmbeddedResource item in Essenthos.Forge.csproj; if the file was moved or " +
                               "renamed, that item and this name have to move with it.",
                               resource);

        return JsonSerializer.Deserialize<T>(stream, Shape)
               ?? throw new InvalidDataException($"The embedded resource \"{resource}\" is empty.");
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

    /// <summary>
    /// The reigns <see cref="Periods.ClosedElsewhere"/> closes, for a corpus whose chronology was
    /// loaded before it closed them. A reign the chronology already holds is closed where the list
    /// now closes it and otherwise left as it is.
    /// </summary>
    private async Task CloseWhatTheDatasetLeftOpen(CancellationToken cancellationToken)
    {
        var anchors = Periods.ClosedElsewhere.SelectMany(pair => new[] { pair.Key, pair.Value }).ToList();
        var events = await db.Events
            .Where(e => anchors.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, StringComparer.Ordinal, cancellationToken);
        var eras = await db.Periods
            .Where(p => p.Kind == "era" && p.Realm == Realms.Scripture)
            .ToListAsync(cancellationToken);

        foreach (var (opening, closing) in Periods.ClosedElsewhere)
        {
            if (!events.TryGetValue(opening, out var opens) || !events.TryGetValue(closing, out var closed))
            {
                continue;
            }

            var period = Periods.Span(opens, closed, BibleDataLoader.Source);
            if (await db.Periods.SingleOrDefaultAsync(p => p.Slug == period.Slug, cancellationToken) is { } standing)
            {
                if (standing.EndEventId != closed.Id)
                {
                    standing.EndEventId = closed.Id;
                    standing.EndYear = closed.YearFromCreation;
                }

                continue;
            }

            period.Parent = Periods.EraOf(period, eras);
            db.Periods.Add(period);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

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

    private static RulerVerdictPassage Passage((int Book, int Chapter, int Verse, int? EndVerse) verse, int position) =>
        new()
        {
            CanonicalBook = verse.Book,
            CanonicalChapter = verse.Chapter,
            CanonicalVerse = verse.Verse,
            EndVerse = verse.EndVerse,
            Position = position,
        };

    private static bool Same<T>(IReadOnlyCollection<T> standing, IReadOnlyCollection<T> wanted, Func<T, object> key) =>
        standing.Count == wanted.Count && standing.Select(key).ToHashSet().SetEquals(wanted.Select(key));

    /// <summary>A verdict as its passages, each carrying the mark and the witness it is quoted for.</summary>
    private static IEnumerable<object> Keys(RulerVerdict v) =>
        v.Witnesses.SelectMany(w => w.Passages.Select(p => (object)(
            v.EntityId, v.Mark, w.Witness, w.Mark, w.Basis, w.Position,
            p.Position, p.CanonicalBook, p.CanonicalChapter, p.CanonicalVerse, p.EndVerse)));

    private static object Key(StatedAge a) =>
        (a.EntityId, a.Kind, a.Years, a.About, a.CanonicalBook, a.CanonicalChapter, a.CanonicalVerse, a.Position);

    private static object Key(ReignEvent e) =>
        (e.Slug, e.Kind, e.Realm, e.Position, e.TimelineEventId, e.RulerEntityId, e.Year,
            e.CanonicalBook, e.CanonicalChapter, e.CanonicalVerse, e.EndVerse);

    private static object Key(ReignLength l) =>
        (l.EntityId, l.Years, l.Months, l.Days, l.CanonicalBook, l.CanonicalChapter, l.CanonicalVerse);

    private static object Key(ThroneName n) =>
        (n.EntityId, n.Language, n.Name, n.CanonicalBook, n.CanonicalChapter, n.CanonicalVerse);

    private static object Key(ProphetField f) =>
        (f.EntityId, f.Realm, f.Kind, f.PlaceEntityId, f.CanonicalBook, f.CanonicalChapter, f.CanonicalVerse,
            f.EndVerse, f.Position);

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
/// <param name="Reigned">How long the text says he reigned.</param>
/// <param name="Throne">The name he reigned under, where his record is headed by another.</param>
/// <param name="Readings">
/// The reckonings whose bars for him differ from <paramref name="Reigned"/> or from the year of his
/// accession the text gives, by slug, each with what that reckoning does instead. Nothing loads it:
/// it is what the test holding the bars to the text accepts, and why.
/// </param>
internal sealed record RulerRecord(
    string Slug,
    string Realm,
    IReadOnlyList<ReignRecord> Reigns,
    LengthRecord? Reigned = null,
    ThroneRecord? Throne = null,
    IReadOnlyDictionary<string, string>? Readings = null);

/// <param name="Names">The name in each language, by its three-letter code: <c>eng</c>, <c>ukr</c>.</param>
/// <param name="Verse">Where the text gives him the name, written as 2KI 23:34.</param>
internal sealed record ThroneRecord(IReadOnlyDictionary<string, string> Names, string Verse);

/// <param name="Verse">Where the text says it, written as 1KI 16:8.</param>
internal sealed record LengthRecord(int? Years, int? Months, int? Days, string Verse)
{
    /// <summary>The length in years, a month a twelfth of one and a day a 365th.</summary>
    public double InYears => (Years ?? 0) + (Months ?? 0) / 12.0 + (Days ?? 0) / 365.0;
}

/// <param name="Realm">One of <see cref="ProphetRealms"/>.</param>
/// <param name="Kind">One of <see cref="ProphetFieldKinds"/>.</param>
/// <param name="Place">The town or land the verse names, by its record's slug.</param>
internal sealed record FieldRecord(string Realm, string Kind, string Verse, string? Place);

internal sealed record ProphetRecord(string Slug, IReadOnlyList<FieldRecord> Fields);

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
    IReadOnlyList<ProphetRecord> Prophets,
    IReadOnlyList<ReignStatementRecord> Statements);

/// <param name="Book">One of <see cref="VerdictWitnesses"/>.</param>
/// <param name="Mark">One of <see cref="RulerMarks"/>, as this history alone leaves him.</param>
/// <param name="Basis">One of <see cref="VerdictBases"/>.</param>
/// <param name="Verses">What the mark is quoted from, each written as 1KI 15:11 or 1KI 11:4-6.</param>
/// <param name="Reason">
/// Why a reading reads as it does, in a sentence. Nothing loads it: it is what the owner checks the
/// reading against.
/// </param>
internal sealed record VerdictWitnessRecord(
    string Book,
    string Mark,
    string Basis,
    IReadOnlyList<string> Verses,
    string? Reason = null);

/// <param name="Mark">One of <see cref="RulerMarks"/>, over all his witnesses.</param>
internal sealed record VerdictRecord(string King, string Mark, IReadOnlyList<VerdictWitnessRecord> Witnesses);

/// <summary>A king the text tells nothing to judge by. Nothing loads it: it says why he has no mark.</summary>
internal sealed record UnmarkedRecord(string King, IReadOnlyList<string> Verses, string Reason);

/// <param name="Kind">One of <see cref="StatedAgeKinds"/>.</param>
/// <param name="About">The verse says <em>about</em>.</param>
/// <param name="Verse">Where the text gives it, written as 2KI 18:2.</param>
internal sealed record AgeRecord(string Person, string Kind, int Years, string Verse, bool About = false);

/// <param name="Year">The ruler's year the verse gives; absent where it says only that it was in his days.</param>
internal sealed record EventDatingRecord(string Ruler, string Verse, int? Year = null);

/// <param name="Kind">One of <see cref="ReignEventKinds"/>.</param>
/// <param name="Realm">The kingdom it befell.</param>
/// <param name="Event">The same event on the timeline, by its slug, where the chronologies date one.</param>
internal sealed record EventRecord(
    string Slug,
    string Kind,
    string Realm,
    IReadOnlyList<EventDatingRecord> Datings,
    string? Event = null);

internal sealed record ReignRulings(
    string DecidedBy,
    string Policy,
    string Method,
    string Source,
    IReadOnlyList<VerdictRecord> Verdicts,
    IReadOnlyList<UnmarkedRecord> Unmarked,
    IReadOnlyList<AgeRecord> Ages,
    IReadOnlyList<EventRecord> Events);
