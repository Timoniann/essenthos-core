using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record EncyclopediaOutcome(
    bool AlreadyLoaded,
    int People,
    int Places,
    int Names,
    int Relationships,
    int References,
    int Disputed,
    int Events,
    int Periods,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the encyclopedia is already loaded"
            : $"{People} people and {Places} places with {Names} names, {Relationships} relationships, " +
              $"{References} references ({Disputed} of them disputed), {Events} dated events and " +
              $"{Periods} periods in {Elapsed}";
}

/// <summary>
/// Brady Stephenson's BibleData: the people, places, relationships and chronology.
///
/// Chosen over Theographic after DOC-0099 read both. Its dates are the reason: every one is
/// computed from a verse and carries the arithmetic in a sentence, with Ussher's and Shulman's
/// figures beside it rather than instead of it. Theographic asserts its dates, only 75 of its
/// 3,069 people have one, and the ones it has contradict its own event table — it gives Joshua a
/// 97-year life where the text says 110.
///
/// **CC BY 4.0, and only on the current main.** The same repository was CC BY-NC-SA until May
/// 2026, and the v1.0.0 tag still is; the author settled it in his own issue tracker — "the
/// LICENSE file governs". The copy loaded here is from `main`, and the folder it lives in is not
/// the older one this workspace already held.
/// </summary>
internal sealed partial class BibleDataLoader(AppDbContext db, ILogger<BibleDataLoader> logger)
{
    private const string Source = "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    /// <summary>
    /// The identifier the dataset gives the God of Israel — and, in the New Testament, to Jesus as
    /// well. See <see cref="Divide"/>.
    /// </summary>
    private const string DivineName = "person:YHVH_1";

    /// <summary>
    /// The two id spaces are separate in the source and must be separate here. Twenty ids name
    /// both a person and a place — Canaan, Cush, Eden, Midian, Moab, Shechem — because a nation is
    /// called after its ancestor, and keying them together let the place quietly overwrite the
    /// person. That is the same fault PRB-0034 records in the other dataset, arrived at from the
    /// other direction.
    /// </summary>
    private static string Key(EntityKind kind, string id) =>
        kind == EntityKind.Place ? $"place:{id}" : $"person:{id}";

    private const string JesusSlug = "jesus";

    /// <summary>
    /// The reckonings this dataset carries, each as its own authority rather than as a column.
    ///
    /// They disagree constantly — Ussher differs from the base in 413 of 419 shared events, by up
    /// to 236 years — and that disagreement is the thing worth showing. A reader wants to see that
    /// the Exodus is 1447 on one reckoning and 1491 on another, and which text each rests on.
    /// </summary>
    private static readonly (string Slug, string Name, string? Authority, string Basis, string? Source,
        int Zero, bool Default, int Position)[] Reckonings =
    [
        ("bibledata", "BibleData", "Brady Stephenson",
            "Computed from the genealogies and reign lengths of the Masoretic text, with each year " +
            "derived from a verse and the arithmetic recorded. Anchored on a 1447 BCE Exodus and a " +
            "931 BCE division of the kingdom.",
            "github.com/BradyStephenson/bible-data", 3961, true, 1),
        ("ussher", "Ussher", "James Ussher, 1650",
            "The Annals of the World. Creation at 4004 BCE, the Exodus at 1491 BCE. Still the " +
            "reckoning printed in the margins of many English Bibles.",
            "Annales Veteris Testamenti, 1650", 4003, false, 2),
        ("shulman", "Seder Olam", "Eliezer Shulman",
            "The Sequence of Events in the Old Testament, following Seder Olam Rabbah — the " +
            "rabbinic reckoning, which compresses the Persian period and so runs several " +
            "centuries short of the others after the exile.",
            "Eliezer Shulman, The Sequence of Events in the Old Testament", 3760, false, 3),
    ];

    /// <summary>
    /// New Testament labels on <see cref="DivineName"/> that name God rather than Jesus, or that
    /// could name either. Everything else on that entity in the New Testament — Christ, Son of
    /// Man, Lamb, Rabbi, King of the Jews, and a hundred more — names Jesus and nothing else.
    ///
    /// Listing the ambiguous ones rather than the Christological ones is deliberate: the list is
    /// short, it is the one a reader would argue with, and inverting it would bury a hundred
    /// uncontroversial decisions in a list nobody checks.
    /// </summary>
    private static readonly HashSet<string> Ambiguous = new(StringComparer.OrdinalIgnoreCase)
    {
        "G-d", "God", "Lord", "LORD", "LORD G-d", "LORD God", "The Living G-d", "The Living God",
        "Almighty", "The Almighty", "Most High", "The Most High", "Creator", "Father",
    };

    public async Task<EncyclopediaOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        if (await db.Entities.AnyAsync(cancellationToken))
        {
            logger.LogInformation("The encyclopedia is already loaded; nothing to do");
            return new EncyclopediaOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (!Directory.Exists(folder))
        {
            logger.LogWarning("No encyclopedia data at {Folder}; the corpus keeps its texts only", folder);
            return new EncyclopediaOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var books = Csv.Read(Path.Combine(folder, "BibleData-Book.csv"))
            .ToDictionary(row => row["usx_code"], row => int.Parse(row["book_id"]));

        var entities = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var slugs = new HashSet<string>(StringComparer.Ordinal);

        // Before the others, so that the slug `jesus` names Jesus of Nazareth. Slugs are claimed
        // first-come, and the dataset's own first Jesus is Paul's fellow worker at Colossians
        // 4:11, who has two references to Jesus of Nazareth's seventeen hundred.
        var jesus = Divide(entities, slugs);
        People(folder, entities, slugs);
        Places(folder, entities, slugs);

        db.Entities.AddRange(entities.Values);
        await db.SaveChangesAsync(cancellationToken);

        var names = Names(folder, entities);
        var relationships = Relationships(folder, entities, books);
        var (references, disputed) = References(folder, entities, books, jesus);
        var events = Events(folder, entities, books);
        var chronologies = Reckon();

        Name(entities, events);

        db.EntityNames.AddRange(names);
        db.EntityRelationships.AddRange(relationships);
        db.EntityVerses.AddRange(references);
        db.Chronologies.AddRange(chronologies);
        db.Events.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);

        db.EventDates.AddRange(Dates(folder, events, chronologies, books));

        var (periods, unpaired) = Periods.From(events, Source);
        db.Periods.AddRange(periods);
        await db.SaveChangesAsync(cancellationToken);

        if (unpaired > 0)
        {
            logger.LogInformation(
                "{Unpaired} of the dataset's openings have no matching close and became no period.",
                unpaired);
        }

        var outcome = new EncyclopediaOutcome(
            false,
            entities.Values.Count(e => e.Kind == EntityKind.Person),
            entities.Values.Count(e => e.Kind == EntityKind.Place),
            names.Count,
            relationships.Count,
            references.Count,
            disputed,
            events.Count,
            periods.Count,
            started.Elapsed);

        logger.LogInformation("Loaded the encyclopedia: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Splits Jesus out of the divine name, which this dataset does not do.
    ///
    /// `YHVH_1` carries 11,134 references, and 3,131 of them are in the New Testament under labels
    /// like *Christ*, *Son of Man*, *Lamb* and *King of the Jews*. The dataset also holds the
    /// Father separately as `YHVH_2`, so what it asserts is not that two persons are one but that
    /// the God of Israel and Jesus are, with the Father beside them — which is a reading this
    /// corpus should not publish as fact.
    ///
    /// So Jesus becomes his own entity, the New Testament references that plainly name him move to
    /// it, and the ones that say only *God* or *Lord* stay where they are and are marked disputed.
    /// Neither silence nor a guess: the reader is told that the source cannot tell.
    /// </summary>
    private static Entity Divide(Dictionary<string, Entity> entities, HashSet<string> slugs)
    {
        var jesus = new Entity
        {
            Kind = EntityKind.Person,
            Slug = Unique(JesusSlug, slugs),
            Name = "Jesus",
            Distinguisher = "of Nazareth",
            Sex = "male",
            Tribe = "Judah",
            SourceId = "essenthos:jesus",
            Source = "Essenthos, separated from BibleData's YHVH_1 — see DOC-0099",
            Notes =
                "BibleData holds the God of Israel and Jesus as one entity and the Father as another. This " +
                "corpus separates them. New Testament references that name Jesus plainly were moved here; " +
                "those that say only God or Lord stayed on the divine name and are marked disputed, because " +
                "which of the two they mean is a reading of the text and not a fact about the dataset.",
        };

        entities[jesus.SourceId] = jesus;
        return jesus;
    }

    private static void People(string folder, Dictionary<string, Entity> entities, HashSet<string> slugs)
    {
        foreach (var row in Csv.Read(Path.Combine(folder, "BibleData-Person.csv")))
        {
            var id = row["person_id"];
            entities[Key(EntityKind.Person, id)] = new Entity
            {
                Kind = EntityKind.Person,
                Slug = Unique(Slugs.Of(row["person_name"]), slugs),
                Name = row["person_name"],
                Distinguisher = Blank(row["unique_attribute"]),
                Sex = Blank(row["sex"]),
                Tribe = Blank(row["tribe"]),
                Notes = Blank(row["person_notes"]),
                SourceId = Key(EntityKind.Person, id),
                Source = Source,
            };
        }
    }

    private static void Places(string folder, Dictionary<string, Entity> entities, HashSet<string> slugs)
    {
        foreach (var row in Csv.Read(Path.Combine(folder, "BibleData-Place.csv")))
        {
            var id = row["place_id"];
            var openBible = Blank(row["openbible_id"]);
            entities[Key(EntityKind.Place, id)] = new Entity
            {
                Kind = EntityKind.Place,
                Slug = Unique(Slugs.Of(row["place_name"]), slugs),
                Name = row["place_name"],
                PlaceKind = Blank(row["place_type"]),
                ModernEquivalent = Blank(row["modern_equivalent"]),
                Notes = Blank(row["place_notes"]),
                OpenBibleId = openBible is "none" ? null : openBible,
                SourceId = Key(EntityKind.Place, id),
                Source = Source,
            };
        }
    }

    private static List<Chronology> Reckon() =>
    [
        .. Reckonings.Select(r => new Chronology
        {
            Slug = r.Slug,
            Name = r.Name,
            Authority = r.Authority,
            Basis = r.Basis,
            Source = r.Source,
            LastYearBeforeTheCommonEra = r.Zero,
            IsDefault = r.Default,
            Position = r.Position,
        }),
    ];

    /// <summary>
    /// Every reckoning's answer for every event, as rows rather than columns.
    ///
    /// A reckoning that says nothing about an event writes no row — which is a different fact from
    /// saying nothing is known, and the reason this is not a table of nulls. Ussher treats 419 of
    /// the 572 and is silent on the rest.
    /// </summary>
    private static List<EventDate> Dates(
        string folder,
        List<Event> events,
        List<Chronology> chronologies,
        Dictionary<string, int> books)
    {
        var bySlug = events.ToDictionary(e => e.Slug, e => e.Id);
        var reckoning = chronologies.ToDictionary(c => c.Slug, c => c.Id);
        var dates = new List<EventDate>(1_500);
        var slugs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in Csv.Read(Path.Combine(folder, "BibleData-Event.csv")))
        {
            var slug = Unique(Slugs.Of(row["event_id"]), slugs);
            if (!bySlug.TryGetValue(slug, out var eventId))
            {
                continue;
            }

            Add(dates, eventId, reckoning["bibledata"], Number(row["event_year_ah"]),
                Blank(row["event_year_calculation"]), null, Blank(row["event_notes"]));
            Add(dates, eventId, reckoning["ussher"], Number(row["ussher_am_year"]),
                null, Blank(row["ussher_paragraph_number"]) is { } paragraph ? $"¶{paragraph}" : null, null);
            Add(dates, eventId, reckoning["shulman"], Number(row["shulman_am_year"]), null, null, null);
        }

        return dates;
    }

    private static void Add(
        List<EventDate> dates,
        int eventId,
        int chronologyId,
        int? year,
        string? calculation,
        string? citation,
        string? notes)
    {
        if (year is null)
        {
            return;
        }

        dates.Add(new EventDate
        {
            EventId = eventId,
            ChronologyId = chronologyId,
            Year = year,
            Calculation = calculation,
            Citation = citation,
            Notes = notes,
        });
    }

    private static List<EntityName> Names(string folder, Dictionary<string, Entity> entities)
    {
        var names = new List<EntityName>(4_000);
        foreach (var row in Csv.Read(Path.Combine(folder, "BibleData-PersonLabel.csv")))
        {
            if (!entities.TryGetValue(Key(EntityKind.Person, row["person_id"]), out var entity))
            {
                continue;
            }

            names.Add(new EntityName
            {
                EntityId = entity.Id,
                Label = row["english_label"],
                Hebrew = Blank(row["hebrew_label"]),
                HebrewTransliterated = Blank(row["hebrew_label_transliterated"]),
                Greek = Blank(row["greek_label"]),
                GreekTransliterated = Blank(row["greek_label_transliterated"]),
                Meaning = Blank(row["hebrew_label_meaning"]) ?? Blank(row["greek_label_meaning"]),
                StrongNumber = Strong(row["hebrew_strongs_number"]) ?? Strong(row["greek_strongs_number"]),
                Kind = Blank(row["label_type"]),
            });
        }

        return names;
    }

    private static List<EntityRelationship> Relationships(
        string folder,
        Dictionary<string, Entity> entities,
        Dictionary<string, int> books)
    {
        var relationships = new List<EntityRelationship>(6_000);
        foreach (var row in Csv.Read(Path.Combine(folder, "BibleData-PersonRelationship.csv")))
        {
            if (!entities.TryGetValue(Key(EntityKind.Person, row["person_id_1"]), out var from)
                || !entities.TryGetValue(Key(EntityKind.Person, row["person_id_2"]), out var to))
            {
                continue;
            }

            var reference = Reference(row["reference_id"], books);
            relationships.Add(new EntityRelationship
            {
                FromEntityId = from.Id,
                ToEntityId = to.Id,
                Type = row["relationship_type"],
                Category = row["relationship_category"],
                CanonicalBook = reference?.Book,
                CanonicalChapter = reference?.Chapter,
                CanonicalVerse = reference?.Verse,
                Notes = Blank(row["relationship_notes"]),
            });
        }

        return relationships;
    }

    private static (List<EntityVerse> References, int Disputed) References(
        string folder,
        Dictionary<string, Entity> entities,
        Dictionary<string, int> books,
        Entity jesus)
    {
        var references = new List<EntityVerse>(50_000);
        var disputed = 0;

        foreach (var (file, key, kind) in ((string, string, EntityKind)[])
                 [
                     ("BibleData-PersonVerse.csv", "person_id", EntityKind.Person),
                     ("BibleData-PlaceVerse.csv", "place_id", EntityKind.Place),
                 ])
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var row in Csv.Read(path))
            {
                if (!entities.TryGetValue(Key(kind, row[key]), out var entity)
                    || Reference(row["reference_id"], books) is not { } reference)
                {
                    continue;
                }

                var label = row.GetValueOrDefault(kind == EntityKind.Place ? "place_label" : "person_label");
                var newTestament = reference.Book > BookReferences.OldTestamentBookCount;
                var contested = false;

                if (entity.SourceId == DivineName && newTestament)
                {
                    if (label is { Length: > 0 } && !Ambiguous.Contains(label))
                    {
                        entity = jesus;
                    }
                    else
                    {
                        contested = true;
                        disputed++;
                    }
                }

                references.Add(new EntityVerse
                {
                    EntityId = entity.Id,
                    CanonicalBook = reference.Book,
                    CanonicalChapter = reference.Chapter,
                    CanonicalVerse = reference.Verse,
                    Label = Blank(label),
                    Disputed = contested,
                });
            }
        }

        return (references, disputed);
    }

    private static List<Event> Events(
        string folder,
        Dictionary<string, Entity> entities,
        Dictionary<string, int> books)
    {
        var events = new List<Event>(600);
        var slugs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in Csv.Read(Path.Combine(folder, "BibleData-Event.csv")))
        {
            var reference = Reference(row["event_reference_id"], books);
            events.Add(new Event
            {
                Slug = Unique(Slugs.Of(row["event_id"]), slugs),
                Name = row["event_name"],
                Description = Blank(row["event_description"]),
                Kind = Blank(row["event_type"]),
                EntityId = entities.TryGetValue(Key(EntityKind.Person, row["person_id"]), out var who) ? who.Id : null,
                YearFromCreation = Number(row["event_year_ah"]),
                BceYear = Number(row["bce_year"]),
                AgeAtEvent = Number(row["person_age_at_event"]),
                Calculation = Blank(row["event_year_calculation"]),
                CanonicalBook = reference?.Book,
                CanonicalChapter = reference?.Chapter,
                CanonicalVerse = reference?.Verse,
                Location = Blank(row["event_location"]),
                Notes = Blank(row["event_notes"]),
                Source = Source,
            });
        }

        return events;
    }

    /// <summary>
    /// Puts names where the source wrote its own identifiers.
    ///
    /// The sentences that show an event's arithmetic are generated from a template, and the
    /// template interpolates the row id: <em>"The year of Moses_1's birth is the year of the
    /// Exodus (2515) minus his age…"</em>. A reader has no idea there is a Moses_1, and the
    /// underscore and the number are an artefact of a spreadsheet rather than anything about
    /// Moses.
    ///
    /// This is not editing a quotation. The sentence is machine-written and the identifier stands
    /// for exactly one entity or event; putting the name back is rendering it, not rewriting it.
    /// Anything the corpus cannot resolve is left exactly as it was, because a stray identifier
    /// visible on the page is a better outcome than a wrong name.
    /// </summary>
    private static void Name(Dictionary<string, Entity> entities, List<Event> events)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, entity) in entities)
        {
            var at = key.IndexOf(':');
            names[at < 0 ? key : key[(at + 1)..]] = entity.Name;
        }

        foreach (var one in events)
        {
            names.TryAdd(one.Slug, one.Name);
        }

        foreach (var one in events)
        {
            one.Calculation = Named(one.Calculation, names);
            one.Notes = Named(one.Notes, names);
            one.Description = Named(one.Description, names);
        }
    }

    /// <summary>
    /// An identifier in this dataset is a name, an underscore and a number — <c>Moses_1</c>,
    /// <c>The_Exodus</c>. Underscores inside ordinary prose are rare enough that requiring one
    /// costs nothing and catches nothing it should not.
    /// </summary>
    private static string? Named(string? sentence, Dictionary<string, string> names)
    {
        if (sentence is not { Length: > 0 } || !sentence.Contains('_'))
        {
            return sentence;
        }

        return Identifier().Replace(sentence, match =>
        {
            var id = match.Value;
            return names.TryGetValue(id, out var name) ? name
                : names.TryGetValue(Slugs.Of(id), out var bySlug) ? bySlug
                : id;
        });
    }

    [GeneratedRegex(@"\b[A-Za-z][A-Za-z0-9-]*(?:_[A-Za-z0-9-]+)+")]
    private static partial Regex Identifier();

    /// <summary>
    /// <c>GEN 1:1</c> in the shared frame. Answers null for the 113 references the dataset makes
    /// that its own reference table does not hold — mostly Psalm superscriptions numbered verse 0,
    /// which our frame has no address for either.
    /// </summary>
    private static (int Book, int Chapter, int Verse)? Reference(string? reference, Dictionary<string, int> books)
    {
        if (reference is not { Length: > 0 })
        {
            return null;
        }

        var space = reference.LastIndexOf(' ');
        var colon = reference.LastIndexOf(':');
        if (space <= 0 || colon <= space
            || !books.TryGetValue(reference[..space], out var book)
            || !int.TryParse(reference[(space + 1)..colon], out var chapter)
            || !int.TryParse(reference[(colon + 1)..], out var verse)
            || verse == 0)
        {
            return null;
        }

        return (book, chapter, verse);
    }

    private static string Unique(string slug, HashSet<string> taken)
    {
        var candidate = slug.Length > 0 ? slug : "unnamed";
        var suffix = 2;
        while (!taken.Add(candidate))
        {
            candidate = $"{slug}-{suffix++}";
        }

        return candidate;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static int? Number(string? value) =>
        int.TryParse(Blank(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>The dataset writes a bare number; this corpus writes the language letter with it.</summary>
    private static string? Strong(string? value) =>
        Blank(value) is { } number && number.Length > 0
            ? char.IsAsciiDigit(number[0]) ? $"H{number}" : number.ToUpperInvariant()
            : null;
}
