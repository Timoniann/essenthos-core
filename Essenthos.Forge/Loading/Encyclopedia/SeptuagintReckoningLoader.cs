using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record SeptuagintOutcome(
    bool AlreadyLoaded,
    int Reckonings,
    int Dates,
    int Unplaced,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Septuagint reckonings are already loaded"
            : $"{Reckonings} Septuagint reckonings computed from the Greek of Genesis 5 and 11, with " +
              $"{Dates} dates and {Unplaced} of the base reckoning's events left undated, in {Elapsed}";
}

/// <param name="FromSwete">
/// The verses of Genesis this reckoning reads as Swete prints them — Codex Alexandrinus — rather than
/// as Brenton does. Everything else is Brenton's.
/// </param>
internal sealed record SeptuagintDefinition(
    string Slug,
    string Name,
    string Authority,
    string Source,
    IReadOnlyList<(int Chapter, int Verse)> FromSwete,
    int Position);

/// <summary>
/// The Septuagint's chronology, written as two reckonings beside the Masoretic ones — one reading
/// Genesis as Brenton prints it and one as Swete prints Codex Alexandrinus. The arithmetic is
/// <see cref="SeptuagintReckoning"/>; this writes it down.
///
/// A reckoning dates the base chronology's events and the world's. It says nothing about Ussher's
/// annals, which are his, and nothing about an event of the primeval history that the rules do not
/// reach: a year carried over from the Hebrew there would be exactly the chronology nobody holds.
/// </summary>
internal sealed class SeptuagintReckoningLoader(AppDbContext db, ILogger<SeptuagintReckoningLoader> logger)
{
    private const string BaseReckoning = "bibledata";

    internal const string CainanSource = "Essenthos, from the Septuagint's Genesis 11:12–13 and Luke 3:36";

    private const string CainanPerson = "person:Cainan_1";

    internal static readonly SeptuagintDefinition[] Definitions =
    [
        new("septuagint", "Septuagint (Brenton)",
            "Essenthos, computed from Brenton's Greek by BibleData's method",
            "Brenton's Septuagint, 1851, public domain: Genesis 5 and 11, Exodus 12:40, 3 Kingdoms 6:1",
            [], 4),
        new("septuagint-alexandrinus", "Septuagint (Alexandrinus)",
            "Essenthos, computed from Brenton's Greek with Codex Alexandrinus at Genesis 5:25–26",
            "Brenton's Septuagint, 1851, public domain; Genesis 5:25–26 from Swete, The Old Testament " +
            "in Greek, 1887, which prints Codex Alexandrinus there, digitised by First1KGreek under " +
            "CC BY-SA 4.0",
            [(5, 25), (5, 26)], 5),
    ];

    public async Task<SeptuagintOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var slugs = Definitions.Select(d => d.Slug).ToList();
        if (await db.Chronologies.AnyAsync(c => slugs.Contains(c.Slug), cancellationToken))
        {
            return new SeptuagintOutcome(true, 0, 0, 0, TimeSpan.Zero);
        }

        var brentonFolder = Path.Combine(resources, "Septuagint");
        var sweteFolder = Path.Combine(resources, "Swete");
        if (!Directory.Exists(brentonFolder) || !Directory.Exists(sweteFolder))
        {
            logger.LogWarning("No Septuagint reckoning: {Brenton} or {Swete} is not there.", brentonFolder, sweteFolder);
            return new SeptuagintOutcome(true, 0, 0, 0, started.Elapsed);
        }

        var reckoning = await db.Chronologies.SingleOrDefaultAsync(c => c.Slug == BaseReckoning, cancellationToken);
        if (reckoning is null)
        {
            logger.LogWarning("No Septuagint reckoning: the base reckoning is not loaded yet.");
            return new SeptuagintOutcome(true, 0, 0, 0, started.Elapsed);
        }

        var dated = await db.EventDates
            .Where(d => d.ChronologyId == reckoning.Id && d.Year != null)
            .Select(d => new BaseDate(
                d.EventId, d.Event!.Slug, d.Event.Name, d.Event.Realm, d.Event.Source,
                d.Year!.Value, d.Calculation, d.Citation))
            .ToListAsync(cancellationToken);

        var editions = new Dictionary<string, Func<int, int, int, string?>>(StringComparer.Ordinal)
        {
            [SeptuagintTextSource.Slug] = Verses(SeptuagintTextSource.Read(brentonFolder)),
            [SweteTextSource.Slug] = Verses(SweteTextSource.Read(sweteFolder)),
        };

        var cainan = await Cainan(cancellationToken);
        var written = 0;
        var unplaced = 0;

        foreach (var definition in Definitions)
        {
            var computed = Reckon(definition, reckoning.LastYearBeforeTheCommonEra, dated, cainan,
                Edition(definition, editions[SeptuagintTextSource.Slug], editions[SweteTextSource.Slug]));

            db.Chronologies.Add(computed.Chronology);
            await db.SaveChangesAsync(cancellationToken);

            foreach (var date in computed.Dates)
            {
                date.ChronologyId = computed.Chronology.Id;
            }

            db.EventDates.AddRange(computed.Dates);
            await db.SaveChangesAsync(cancellationToken);
            written += computed.Dates.Count;
            unplaced = computed.Unplaced;
        }

        var outcome = new SeptuagintOutcome(false, Definitions.Length, written, unplaced, started.Elapsed);
        logger.LogInformation("Loaded the Septuagint reckonings: {Outcome}", outcome);
        return outcome;
    }

    internal sealed record BaseDate(
        int EventId,
        string Slug,
        string Name,
        string Realm,
        string Source,
        int Year,
        string? Calculation,
        string? Citation);

    internal sealed record Reckoned(Chronology Chronology, List<EventDate> Dates, int Unplaced);

    /// <summary>One reckoning: its chronology row and every date it gives.</summary>
    /// <param name="baseZero">The year the base reckoning calls 1 BCE.</param>
    /// <param name="cainan">Cainan's birth and death, which only these reckonings date.</param>
    internal static Reckoned Reckon(
        SeptuagintDefinition definition,
        int baseZero,
        IReadOnlyList<BaseDate> dated,
        (Event Born, Event Died) cainan,
        Func<int, int, int, string?> verse)
    {
        var (values, problems) = SeptuagintReckoning.Read(verse);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"The {definition.Name} reckoning cannot be computed: {string.Join(" ", problems)} " +
                "Correct the reading in GreekNumerals or the verse in the edition, then load again.");
        }

        var bySlug = dated.Where(d => d.Source == BibleDataLoader.Source).ToDictionary(d => d.Slug);
        var ruled = SeptuagintReckoning.Rules.ToDictionary(rule => rule.Event, rule => Key(rule.Event));
        string Named(string id) =>
            id == SeptuagintReckoning.CainanBorn ? cainan.Born.Name
            : id == SeptuagintReckoning.CainanDied ? cainan.Died.Name
            : bySlug.TryGetValue(Key(id), out var row) ? row.Name : id;

        var years = SeptuagintReckoning.Compute(values, Named);
        int Base(string id) => bySlug.TryGetValue(Key(id), out var row)
            ? row.Year
            : throw new InvalidOperationException(
                $"The base reckoning has no year for {id}, which the Septuagint reckoning counts from. " +
                "Load BibleData before this.");

        var sojourn = Base(SeptuagintReckoning.TheExodus) - Base(SeptuagintReckoning.AbramLeavesHaran);
        if (values[SeptuagintReckoning.Sojourn].Value != sojourn)
        {
            throw new InvalidOperationException(
                $"Exodus 12:40 reads {values[SeptuagintReckoning.Sojourn].Value} years where the base reckoning " +
                $"counts {sojourn} from Abram leaving Haran to the Exodus, and this reckoning only knows how to " +
                "shift events by one amount on each side of the Exodus. Model the difference before loading.");
        }

        var abram = years[SeptuagintReckoning.AbramBorn].Year;
        var genealogies = abram - Base(SeptuagintReckoning.AbramBorn);
        var templeGap = Base(SeptuagintReckoning.TheTemple) - Base(SeptuagintReckoning.TheExodus);
        var temple = values[SeptuagintReckoning.ExodusToTemple].Value;
        var afterTemple = genealogies + temple - templeGap;

        var chronology = new Chronology
        {
            Slug = definition.Slug,
            Name = definition.Name,
            Authority = definition.Authority,
            Basis = Basis(definition, values, years, genealogies, templeGap, temple),
            Source = definition.Source,
            // The Temple and everything after it keep their historical years, so the zero moves by
            // exactly what the reckoning adds before the Temple.
            LastYearBeforeTheCommonEra = baseZero + afterTemple,
            Position = definition.Position,
        };

        var scripture = dated
            .Where(d => d.Source == BibleDataLoader.Source && d.Realm == Realms.Scripture)
            .ToList();
        var sides = SeptuagintReckoning.Sides(
            [.. scripture.Select(d => (d.Slug, d.Year, d.Calculation))],
            Base(SeptuagintReckoning.TheExodus),
            Base(SeptuagintReckoning.TheTemple));
        var computedSlugs = ruled.Values.ToHashSet(StringComparer.Ordinal);
        var dates = new List<EventDate>(dated.Count + 2);
        var unplaced = 0;

        foreach (var rule in SeptuagintReckoning.Rules)
        {
            var year = years[rule.Event];
            var eventId = rule.Event switch
            {
                SeptuagintReckoning.CainanBorn => cainan.Born.Id,
                SeptuagintReckoning.CainanDied => cainan.Died.Id,
                _ => bySlug.TryGetValue(Key(rule.Event), out var row) ? row.EventId : 0,
            };

            if (eventId != 0)
            {
                dates.Add(new EventDate
                {
                    EventId = eventId,
                    Year = year.Year,
                    Calculation = year.Calculation,
                    Citation = year.Citation,
                });
            }
        }

        var abramBase = Base(SeptuagintReckoning.AbramBorn);
        foreach (var row in dated)
        {
            if (computedSlugs.Contains(row.Slug))
            {
                continue;
            }

            if (row.Realm == Realms.World)
            {
                dates.Add(new EventDate
                {
                    EventId = row.EventId,
                    Year = row.Year + afterTemple,
                    Citation = row.Citation,
                });
                continue;
            }

            if (row.Source != BibleDataLoader.Source)
            {
                continue;
            }

            SeptuagintSide? side = row.Year < abramBase ? null : sides.TryGetValue(row.Slug, out var found) ? found : null;
            if (side is null)
            {
                unplaced++;
                continue;
            }

            var shift = side == SeptuagintSide.Exodus ? genealogies : afterTemple;
            dates.Add(new EventDate
            {
                EventId = row.EventId,
                Year = row.Year + shift,
                Calculation = side == SeptuagintSide.Exodus
                    ? $"BibleData's year ({SeptuagintReckoning.Number(row.Year)}) + {SeptuagintReckoning.Number(genealogies)}, " +
                      $"by which the Greek of Genesis 5 and 11 moves the Birth of Abram " +
                      $"({SeptuagintReckoning.Number(abramBase)} there, {SeptuagintReckoning.Number(abram)} here) " +
                      $"= {SeptuagintReckoning.Number(row.Year + shift)}"
                    : $"BibleData's year ({SeptuagintReckoning.Number(row.Year)}) + {SeptuagintReckoning.Number(afterTemple)}: " +
                      $"the {SeptuagintReckoning.Number(genealogies)} the Greek of Genesis 5 and 11 adds before Abram, less the " +
                      $"{templeGap - temple} by which 1 Kings 6:1 reads {temple} years from the Exodus to the Temple against " +
                      $"{templeGap} = {SeptuagintReckoning.Number(row.Year + shift)}",
                Citation = side == SeptuagintSide.Exodus ? "Genesis 5; Genesis 11" : "Genesis 5; Genesis 11; 1 Kings 6:1",
            });
        }

        return new Reckoned(chronology, dates, unplaced);
    }

    private static string Key(string id) => Slugs.Of(id);

    /// <summary>What the reckoning rests on, in a paragraph, with the numbers it actually read.</summary>
    private static string Basis(
        SeptuagintDefinition definition,
        IReadOnlyDictionary<string, (int Value, SeptuagintReading Reading)> values,
        IReadOnlyDictionary<string, SeptuagintYear> years,
        int genealogies,
        int templeGap,
        int temple)
    {
        var edition = definition.FromSwete.Count == 0
            ? "Brenton's Greek, the Sixtine edition"
            : "Brenton's Greek, except at Genesis 5:25–26, read as Codex Alexandrinus has it in Swete's edition";
        var methuselah = years["Death_Methuselah_1"].Year - years["Begin_Flood"].Year;
        var outlives = methuselah > 0
            ? $"he outlives the Flood by {methuselah} years"
            : methuselah == 0 ? "he dies in the year of the Flood" : $"he dies {-methuselah} years before the Flood";
        return
            $"BibleData's arithmetic with the Septuagint's numbers, read from {edition}. The fathers of " +
            $"Genesis 5 beget later than in the Hebrew — Adam at {values["adam-begets"].Value}, Methuselah at " +
            $"{values["methuselah-begets"].Value}, so that {outlives}, Lamech at {values["lamech-begets"].Value} — " +
            $"and the Flood falls in year {SeptuagintReckoning.Number(years["Begin_Flood"].Year)}. Genesis 11 adds " +
            $"Cainan, whom Arphaxad begets at {values["arphaxad-begets"].Value}, and has Nahor beget Terah at " +
            $"{values["nahor-begets"].Value}; Abram is born in year " +
            $"{SeptuagintReckoning.Number(years[SeptuagintReckoning.AbramBorn].Year)}, " +
            $"{SeptuagintReckoning.Number(genealogies)} years after the Hebrew places him. Exodus 12:40 counts " +
            $"the {values[SeptuagintReckoning.Sojourn].Value} years in Egypt and in Canaan together, as the base " +
            $"reckoning already does. 1 Kings 6:1 puts the Temple {temple} years after the Exodus against the " +
            $"Hebrew's {templeGap}: the judges keep their distance from the Exodus and Saul and David theirs " +
            $"from the Temple, so the {templeGap - temple} years come out between Samson and Eli. From the " +
            "Temple on it follows the base reckoning's reigns.";
    }

    /// <summary>
    /// Cainan's birth and death, written once as events of their own: the Hebrew has no Cainan, so
    /// no other reckoning dates them and they appear only when a Septuagint one is chosen.
    /// </summary>
    private async Task<(Event Born, Event Died)> Cainan(CancellationToken cancellationToken)
    {
        var person = await db.Entities.FirstOrDefaultAsync(e => e.SourceId == CainanPerson, cancellationToken);
        var existing = await db.Events.Where(e => e.Source == CainanSource).ToListAsync(cancellationToken);
        var born = existing.FirstOrDefault(e => e.Kind == "Birth") ?? Made("birthofcainan", "Birth of Cainan", "Birth", 12);
        var died = existing.FirstOrDefault(e => e.Kind == "Death") ?? Made("deathofcainan", "Death of Cainan", "Death", 13);

        if (born.Id == 0 || died.Id == 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return (born, died);

        Event Made(string slug, string name, string kind, int verse)
        {
            var made = new Event
            {
                Slug = slug,
                Name = name,
                Kind = kind,
                Description =
                    "Cainan, son of Arphaxad and father of Shelah, stands in the Septuagint's Genesis 11:12–13 " +
                    "and in Luke 3:36 and not in the Hebrew, so only the Septuagint reckonings date him.",
                EntityId = person?.Id,
                CanonicalBook = 1,
                CanonicalChapter = 11,
                CanonicalVerse = verse,
                Realm = Realms.Scripture,
                Source = CainanSource,
            };
            db.Events.Add(made);
            return made;
        }
    }

    /// <summary>Brenton's verses, with the ones the reckoning takes from Swete taken from Swete.</summary>
    internal static Func<int, int, int, string?> Edition(
        SeptuagintDefinition definition,
        Func<int, int, int, string?> brenton,
        Func<int, int, int, string?> swete) =>
        (book, chapter, verse) => book == 1 && definition.FromSwete.Contains((chapter, verse))
            ? swete(book, chapter, verse)
            : brenton(book, chapter, verse);

    /// <summary>A verse's words as the edition prints them, by canonical book, chapter and verse.</summary>
    private static Func<int, int, int, string?> Verses(TextSource source)
    {
        var verses = new Dictionary<(int, int, int), string>();
        foreach (var book in source.Books.Where(b => b.CanonicalOrdinal is 1 or 2 or 11))
        {
            foreach (var chapter in book.Chapters)
            {
                foreach (var verse in chapter.Verses.Where(v => v.Label.Length == 0))
                {
                    verses[(book.CanonicalOrdinal, chapter.Number, verse.Number)] =
                        string.Join(' ', verse.Words.Select(word => word.Surface));
                }
            }
        }

        return (book, chapter, verse) => verses.GetValueOrDefault((book, chapter, verse));
    }
}
