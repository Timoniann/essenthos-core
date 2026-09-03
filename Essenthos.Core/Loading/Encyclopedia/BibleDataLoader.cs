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
    /// The dataset's second entity of the same name: the Father, as the New Testament names him.
    /// Every one of its 352 namings is in a New Testament book.
    /// </summary>
    private const string TheFather = "person:YHVH_2";

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

    /// <summary>Who a New Testament label on <see cref="DivineName"/> is taken to name.</summary>
    internal enum DivineReading
    {
        /// <summary>The God of Israel, where the dataset already has him — no move, no flag.</summary>
        TheGodOfIsrael,

        Jesus,

        /// <summary>A word the New Testament uses of both, so the corpus states neither.</summary>
        Contested,
    }

    /// <summary>
    /// The labels this dataset puts on <see cref="DivineName"/> in the New Testament, decided one
    /// at a time.
    ///
    /// The first attempt at this listed the fourteen words that are ambiguous and sent everything
    /// else to Jesus, which is wrong in the direction that matters: <em>the G-d of Abraham, the
    /// G-d of Isaac, and the G-d of Jacob</em> is not in the list of fourteen, so Matthew 22:32
    /// filed the words Jesus quotes at the bush under Jesus himself. Twenty-four such rows, all of
    /// them compounds naming the God of Israel by an Old Testament title.
    ///
    /// So the whole population is listed instead — there are 106 labels, which is a page of
    /// reading rather than an algorithm — and anything the list does not name is contested. That
    /// default is the safe one: a label nobody has read yet is flagged rather than assigned, and a
    /// new label in a later BibleData shows up as a flag instead of as a silent claim.
    ///
    /// Where a label is genuinely used both ways it is contested even when one reading dominates.
    /// <em>Savior</em> is Christ in fifteen of its seventeen verses and God in Luke 1:47 and
    /// 1 Timothy 4:10; <em>King of kings</em> is the Lamb in Revelation and God in 1 Timothy 6:15.
    /// The flag says the corpus will not choose, which is true, and the label and the verse are
    /// both on the page for a reader who will.
    /// </summary>
    private static readonly Dictionary<string, DivineReading> Readings =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Named, or a title the New Testament gives the Son and no one else.
            ["Jesus"] = DivineReading.Jesus,
            ["Jesus Christ"] = DivineReading.Jesus,
            ["Jesus of Nazareth"] = DivineReading.Jesus,
            ["Jesus the Galilean"] = DivineReading.Jesus,
            ["Your holy servant Jesus"] = DivineReading.Jesus,
            ["Christ"] = DivineReading.Jesus,
            ["Christ Jesus"] = DivineReading.Jesus,
            ["Lord Jesus"] = DivineReading.Jesus,
            ["Lord Jesus Christ"] = DivineReading.Jesus,
            ["the Lord Christ"] = DivineReading.Jesus,
            ["the Lord's Christ"] = DivineReading.Jesus,
            ["The Messiah"] = DivineReading.Jesus,
            ["Nazarene"] = DivineReading.Jesus,
            ["Immanuel"] = DivineReading.Jesus,
            ["G-d With Us"] = DivineReading.Jesus,
            ["I AM"] = DivineReading.Jesus,
            ["the last Adam"] = DivineReading.Jesus,
            ["Son"] = DivineReading.Jesus,
            ["Son of Man"] = DivineReading.Jesus,
            ["Son of G-d"] = DivineReading.Jesus,
            ["Son of David"] = DivineReading.Jesus,
            ["Son of Abraham"] = DivineReading.Jesus,
            ["Son of the Living G-d"] = DivineReading.Jesus,
            ["Son of the Blessed One"] = DivineReading.Jesus,
            ["Son of the Most High"] = DivineReading.Jesus,
            ["Son of the Most High G-d"] = DivineReading.Jesus,
            ["My beloved Son"] = DivineReading.Jesus,
            ["My Servant"] = DivineReading.Jesus,
            ["Teacher"] = DivineReading.Jesus,
            ["Rabbi"] = DivineReading.Jesus,
            ["Rabboni"] = DivineReading.Jesus,
            ["Master"] = DivineReading.Jesus,
            ["Leader"] = DivineReading.Jesus,
            ["Lamb"] = DivineReading.Jesus,
            ["The Lamb of G-d"] = DivineReading.Jesus,
            ["King of the Jews"] = DivineReading.Jesus,
            ["King of Israel"] = DivineReading.Jesus,
            ["The Light"] = DivineReading.Jesus,
            ["Bread of Life"] = DivineReading.Jesus,
            ["The Word"] = DivineReading.Jesus,
            ["Word of Life"] = DivineReading.Jesus,
            ["Word of G-d"] = DivineReading.Jesus,
            ["Holy One of G-d"] = DivineReading.Jesus,
            ["Righteous One"] = DivineReading.Jesus,
            ["Chosen One"] = DivineReading.Jesus,
            ["Expected One"] = DivineReading.Jesus,
            ["The Living One"] = DivineReading.Jesus,
            ["High Priest"] = DivineReading.Jesus,
            ["the Apostle"] = DivineReading.Jesus,
            ["Advocate"] = DivineReading.Jesus,
            ["Guardian"] = DivineReading.Jesus,
            ["Shepherd"] = DivineReading.Jesus,
            ["Chief Shepherd"] = DivineReading.Jesus,
            ["Prince"] = DivineReading.Jesus,
            ["Prince of Life"] = DivineReading.Jesus,
            ["Lord of the Sabbath"] = DivineReading.Jesus,
            ["The Root of David"] = DivineReading.Jesus,
            ["The Lion of the Tribe of Judah"] = DivineReading.Jesus,
            ["The Bright Morning Star"] = DivineReading.Jesus,
            ["Firstborn of the dead"] = DivineReading.Jesus,
            ["Ruler of the kings of the earth"] = DivineReading.Jesus,
            ["Faithful witness"] = DivineReading.Jesus,
            ["Faithful and True"] = DivineReading.Jesus,
            ["Faithful and True Witness"] = DivineReading.Jesus,
            ["The Amen"] = DivineReading.Jesus,
            ["The Beginning of the Creation of G-d"] = DivineReading.Jesus,
            ["The First and the Last"] = DivineReading.Jesus,
            ["The Beginning and the End"] = DivineReading.Jesus,

            // An Old Testament title of the God of Israel, carried into the New Testament.
            ["Father"] = DivineReading.TheGodOfIsrael,
            ["Father of Lights"] = DivineReading.TheGodOfIsrael,
            ["the Most High"] = DivineReading.TheGodOfIsrael,
            ["G-d Most High"] = DivineReading.TheGodOfIsrael,
            ["Creator"] = DivineReading.TheGodOfIsrael,
            ["Lawgiver"] = DivineReading.TheGodOfIsrael,
            ["Almighty"] = DivineReading.TheGodOfIsrael,
            ["The Almighty"] = DivineReading.TheGodOfIsrael,
            ["Lord Almighty"] = DivineReading.TheGodOfIsrael,
            ["The Living G-d"] = DivineReading.TheGodOfIsrael,
            ["LORD G-d"] = DivineReading.TheGodOfIsrael,
            ["the G-d of Abraham, the G-d of Isaac, and the G-d of Jacob"] = DivineReading.TheGodOfIsrael,
            ["G-d of Abraham, Isaac, and Jacob"] = DivineReading.TheGodOfIsrael,
            ["the G-d of the fathers"] = DivineReading.TheGodOfIsrael,
            ["G-d of Israel"] = DivineReading.TheGodOfIsrael,
            ["Lord G-d of Israel"] = DivineReading.TheGodOfIsrael,
            ["G-d of Jacob"] = DivineReading.TheGodOfIsrael,
            ["G-d of peace"] = DivineReading.TheGodOfIsrael,
            ["G-d of heaven"] = DivineReading.TheGodOfIsrael,
            ["Lord of Sabaoth"] = DivineReading.TheGodOfIsrael,
            ["Lord of the Harvest"] = DivineReading.TheGodOfIsrael,
            ["He who Is and who was and who is to come"] = DivineReading.TheGodOfIsrael,
            ["Great King"] = DivineReading.TheGodOfIsrael,
            ["Mighty One"] = DivineReading.TheGodOfIsrael,
            ["Sovereign"] = DivineReading.TheGodOfIsrael,
            ["Lord of the earth"] = DivineReading.TheGodOfIsrael,
            ["King of the Nations"] = DivineReading.TheGodOfIsrael,

            // Used of both, in verses that do not settle it.
            ["G-d"] = DivineReading.Contested,
            ["God"] = DivineReading.Contested,
            ["Lord"] = DivineReading.Contested,
            ["LORD"] = DivineReading.Contested,
            ["Savior"] = DivineReading.Contested,
            ["G-d our Savior"] = DivineReading.Contested,
            ["King"] = DivineReading.Contested,
            ["King of kings"] = DivineReading.Contested,
            ["Lord of lords"] = DivineReading.Contested,
            ["Judge"] = DivineReading.Contested,
            ["the Judge"] = DivineReading.Contested,
            ["Holy One"] = DivineReading.Contested,
            ["The Alpha and the Omega"] = DivineReading.Contested,
        };

    internal static DivineReading Reading(string? label) =>
        Blank(label) is { } written && Readings.TryGetValue(written, out var reading)
            ? reading
            : DivineReading.Contested;

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
        var frame = ReferenceTable.Read(folder);

        var entities = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var slugs = new HashSet<string>(StringComparer.Ordinal);

        // Before the others, so that the slug `jesus` names Jesus of Nazareth. Slugs are claimed
        // first-come, and the dataset's own first Jesus is Paul's fellow worker at Colossians
        // 4:11, who has two references to Jesus of Nazareth's seventeen hundred.
        var jesus = Divide(entities, slugs);
        People(folder, entities, slugs);
        Places(folder, entities, slugs);
        Distinguish(entities);

        db.Entities.AddRange(entities.Values);
        await db.SaveChangesAsync(cancellationToken);

        var names = Names(folder, entities);
        var (relationships, duplicates, unpaired) = Relationships(folder, entities, frame, jesus);
        var (references, disputed) = References(folder, entities, frame, jesus);
        var events = Events(folder, entities, frame);
        var chronologies = Reckon();

        // After the relationships, because their notes name the dataset's rows too, and before the
        // save, so that the entities the first save is still tracking are written back changed.
        Name(entities, events, relationships);

        db.EntityNames.AddRange(names);
        db.EntityRelationships.AddRange(relationships);
        db.EntityVerses.AddRange(references);
        db.Chronologies.AddRange(chronologies);
        db.Events.AddRange(events);
        await db.SaveChangesAsync(cancellationToken);

        db.EventDates.AddRange(Dates(folder, events, chronologies));

        var (periods, unpairedPeriods) = Periods.From(events, Source);
        db.Periods.AddRange(periods);
        await db.SaveChangesAsync(cancellationToken);

        if (unpairedPeriods > 0)
        {
            logger.LogInformation(
                "{Unpaired} of the dataset's openings have no matching close and became no period.",
                unpairedPeriods);
        }

        Report(frame, duplicates, unpaired);

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
    /// What the source got wrong that the load could only drop or leave alone.
    ///
    /// Logged rather than swallowed, because each of these is a repair somebody upstream can make
    /// once and everybody downstream stops paying for — and because a number that changes between
    /// two loads is how a new defect in the source is noticed at all.
    /// </summary>
    private void Report(ReferenceTable frame, int duplicates, int unpaired)
    {
        if (frame.Dangling.Count > 0)
        {
            logger.LogWarning(
                "{Rows} citations name a verse the dataset's own reference table does not hold and were " +
                "dropped: {References}. Correcting them is upstream work, in BibleData itself.",
                frame.Dangling.Values.Sum(),
                string.Join(", ", frame.Dangling.OrderByDescending(d => d.Value).Select(d => $"{d.Key} ×{d.Value}")));
        }

        if (duplicates > 0)
        {
            logger.LogInformation(
                "{Duplicates} relationship rows repeated one the source already stated and were dropped.",
                duplicates);
        }

        if (unpaired > 0)
        {
            logger.LogInformation(
                "{Unpaired} relationships have no row stating the inverse. The source records both " +
                "readings of an ambiguous genealogy without pairing them, so nothing is inferred here.",
                unpaired);
        }
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
    /// So Jesus becomes his own entity and the New Testament references that plainly name him move
    /// to it. The rest stay where they are: a title of the God of Israel unflagged, and a word the
    /// New Testament uses of both marked disputed. Neither silence nor a guess — the reader is
    /// told which of the three the label supports. See <see cref="Readings"/>.
    /// </summary>
    internal static Entity Divide(Dictionary<string, Entity> entities, HashSet<string> slugs)
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

    /// <summary>
    /// Tells the dataset's two entities named YHVH apart, which its own attributes do not.
    ///
    /// Both are called YHVH, so the attribute is the only thing a list, a search result or a
    /// relationship row can offer to choose between them — and the first one's is a sample of its
    /// own titles, <em>"Holy, Holy, Holy (ISA 6:3) and too many others to fit here"</em>, which
    /// says nothing about which of the two it is. A reader searching the name gets two rows and no
    /// way to pick one.
    ///
    /// What separates them is what each is, and both halves are facts about the dataset rather
    /// than readings of the text: the first carries the divine name through the whole canon and is
    /// the entity Jesus was taken out of; the second is the Father, and all 352 of its namings are
    /// in New Testament books. The source's own attribute is kept in the notes, because it is a
    /// true thing it said even though it is not a distinguisher.
    /// </summary>
    internal static void Distinguish(Dictionary<string, Entity> entities)
    {
        if (entities.TryGetValue(DivineName, out var god))
        {
            god.Distinguisher = "the God of Israel";
            god.Notes = Sentences(
                "The dataset gives this entity and the Father the same name and tells them apart by a " +
                "sample of their titles — for this one, \"Holy, Holy, Holy (ISA 6:3) and too many others " +
                "to fit here\" — which does not say which of the two it is. This is the one the divine " +
                "name belongs to, named through the whole canon. It is also the entity Jesus is folded " +
                "into, and the New Testament namings that plainly mean him were moved to his own entry.",
                god.Notes);
        }

        if (entities.TryGetValue(TheFather, out var father))
        {
            father.Distinguisher = "the Father, whom the New Testament names (MAT 5:16)";
        }
    }

    private static string? Sentences(params string?[] parts) =>
        Blank(string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part))));

    internal static void People(string folder, Dictionary<string, Entity> entities, HashSet<string> slugs)
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

    internal static void Places(string folder, Dictionary<string, Entity> entities, HashSet<string> slugs)
    {
        foreach (var row in Csv.Read(Path.Combine(folder, "BibleData-Place.csv")))
        {
            var id = row["place_id"];
            entities[Key(EntityKind.Place, id)] = new Entity
            {
                Kind = EntityKind.Place,
                Slug = Unique(Slugs.Of(row["place_name"]), slugs),
                Name = row["place_name"],
                PlaceKind = Blank(row["place_type"]),
                ModernEquivalent = Blank(row["modern_equivalent"]),
                Notes = Blank(row["place_notes"]),
                OpenBibleId = Blank(row["openbible_id"]),
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
        List<Chronology> chronologies)
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

    /// <summary>
    /// What each entity is called, from both label files.
    ///
    /// The place file was the one nobody opened, which is why every place page offered no Hebrew,
    /// no Greek and no meaning while every person page offered all three. It carries the same
    /// columns bar the Greek meaning, so it reads through the same code.
    /// </summary>
    internal static List<EntityName> Names(string folder, Dictionary<string, Entity> entities)
    {
        var names = new List<EntityName>(4_000);

        foreach (var (file, key, kind) in LabelFiles)
        {
            var path = Path.Combine(folder, file);
            if (!File.Exists(path))
            {
                continue;
            }

            foreach (var row in Csv.Read(path))
            {
                if (!entities.TryGetValue(Key(kind, row[key]), out var entity))
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
                    Meaning = Blank(row["hebrew_label_meaning"])
                              ?? Blank(row.GetValueOrDefault("greek_label_meaning")),
                    HebrewStrongNumber = Strong(row["hebrew_strongs_number"], 'H'),
                    GreekStrongNumber = Strong(row["greek_strongs_number"], 'G'),
                    Kind = Blank(row["label_type"]),
                });
            }
        }

        return names;
    }

    private static readonly (string File, string Key, EntityKind Kind)[] LabelFiles =
    [
        ("BibleData-PersonLabel.csv", "person_id", EntityKind.Person),
        ("BibleData-PlaceLabel.csv", "place_id", EntityKind.Place),
    ];

    /// <summary>Where the source says a verse states the relation, rather than having deduced it.</summary>
    private const string Explicit = "explicit";

    /// <summary>
    /// Who stands in what relation to whom, once each.
    ///
    /// The source restates a line where the text does — Uriah is Meremoth's father in Ezra 8:33
    /// and again in Nehemiah 3:4 — and those two rows are two citations of one fact and are both
    /// kept. What is dropped is a row identical to one already read down to the note: Ezra 3:2
    /// gives Aaron as Jozadak's ancestor twice, once said to be explicit and once inferred, and an
    /// entity page that listed it twice would be reporting a spreadsheet rather than a genealogy.
    /// </summary>
    /// <summary>
    /// The relations to the divine name that are relations to Jesus of Nazareth.
    ///
    /// The separation was applied to the references and not to these, so every one of them stayed
    /// on the divine name: the twelve apostles were apostles of the God of Israel, Mary was his
    /// bearer, and the encyclopedia said <em>YHVH brother of James</em> at Matthew 13:55.
    ///
    /// Listed rather than derived, and the list is the whole population — nine types across
    /// sixty-four rows, which is a paragraph of reading rather than an algorithm. A rule saying
    /// "in the New Testament it means Jesus" would be wrong the first time the dataset records
    /// <em>servant</em> of God in a letter, and wrong silently.
    ///
    /// <para>
    /// **Both directions of a tie are separate rows with different words**, and both have to be
    /// here or the tie comes apart: <em>apostle</em> against <em>master</em>, <em>disciple</em>
    /// against <em>rabbi</em>, <em>bearer</em> against <em>born by</em>, <em>patron</em> against
    /// <em>client</em>. Listing only the first of each pair moved half of every tie and left 56
    /// relationships pointing at an entity that no longer pointed back.
    /// </para>
    ///
    /// <para>
    /// <em>master</em> is why the New Testament test is not redundant: it is the reverse of
    /// <em>apostle</em> twelve times in Matthew and the reverse of <em>servant</em> nine times from
    /// Genesis to Jeremiah, and Moses is not a servant of Jesus of Nazareth.
    /// </para>
    /// </summary>
    private static readonly HashSet<string> RelationsToJesus = new(StringComparer.OrdinalIgnoreCase)
    {
        "apostle", "master", "disciple", "rabbi", "brother", "bearer", "born by", "patron", "client",
    };

    /// <summary>The first book of the New Testament in the shared order.</summary>
    private const int FirstApostolicBook = 40;

    /// <param name="jesus">
    /// Where a relation to the divine name is a relation to Jesus of Nazareth. Both directions are
    /// separate rows in this dataset and both are moved, or the encyclopedia would say one thing
    /// on his page and the other on hers.
    /// </param>
    internal static (List<EntityRelationship> Relationships, int Duplicates, int Unpaired) Relationships(
        string folder,
        Dictionary<string, Entity> entities,
        ReferenceTable frame,
        Entity jesus)
    {
        var relationships = new List<EntityRelationship>(6_000);
        var seen = new Dictionary<(int From, string Type, int To, int? Book, int? Chapter, int? Verse, string? Notes),
            EntityRelationship>();
        var duplicates = 0;

        foreach (var row in Csv.Read(Path.Combine(folder, "BibleData-PersonRelationship.csv")))
        {
            if (!entities.TryGetValue(Key(EntityKind.Person, row["person_id_1"]), out var from)
                || !entities.TryGetValue(Key(EntityKind.Person, row["person_id_2"]), out var to))
            {
                continue;
            }

            var reference = frame.Resolve(row["reference_id"]);
            var type = row["relationship_type"];
            var relationship = new EntityRelationship
            {
                FromEntityId = Read(from, type, reference, jesus).Id,
                ToEntityId = Read(to, type, reference, jesus).Id,
                Type = row["relationship_type"],
                Category = row["relationship_category"],
                CanonicalBook = reference?.Book,
                CanonicalChapter = reference?.Chapter,
                CanonicalVerse = reference?.Verse,
                Notes = Blank(row["relationship_notes"]),
            };

            var key = (relationship.FromEntityId, relationship.Type, relationship.ToEntityId,
                relationship.CanonicalBook, relationship.CanonicalChapter, relationship.CanonicalVerse,
                relationship.Notes);

            if (seen.TryGetValue(key, out var already))
            {
                duplicates++;

                // The source contradicts itself on one of these: same verse, same note, explicit
                // once and inferred the other time. The stronger claim is the one it can point at.
                if (relationship.Category == Explicit)
                {
                    already.Category = Explicit;
                }

                continue;
            }

            seen[key] = relationship;
            relationships.Add(relationship);
        }

        var directed = relationships.Select(r => (r.FromEntityId, r.ToEntityId)).ToHashSet();
        var unpaired = relationships.Count(r => !directed.Contains((r.ToEntityId, r.FromEntityId)));

        return (relationships, duplicates, unpaired);
    }

    /// <summary>
    /// Which of the two the divine name is standing for in one relationship. Anything that is not
    /// the divine name is itself; anything outside the New Testament is the God of Israel; and a
    /// New Testament relation is his unless its type is one of the handful that can only be a
    /// relation to a man.
    /// </summary>
    private static Entity Read(
        Entity entity, string type, (int Book, int Chapter, int Verse)? reference, Entity jesus) =>
        entity.SourceId == DivineName
        && reference is { Book: >= FirstApostolicBook }
        && RelationsToJesus.Contains(type)
            ? jesus
            : entity;

    internal static (List<EntityVerse> References, int Disputed) References(
        string folder,
        Dictionary<string, Entity> entities,
        ReferenceTable frame,
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
                    || frame.Resolve(row["reference_id"]) is not { } reference)
                {
                    continue;
                }

                var label = row.GetValueOrDefault(kind == EntityKind.Place ? "place_label" : "person_label");
                var newTestament = reference.Book > BookReferences.OldTestamentBookCount;
                var contested = false;

                if (entity.SourceId == DivineName && newTestament)
                {
                    switch (Reading(label))
                    {
                        case DivineReading.Jesus:
                            entity = jesus;
                            break;
                        case DivineReading.Contested:
                            contested = true;
                            disputed++;
                            break;
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

    internal static List<Event> Events(
        string folder,
        Dictionary<string, Entity> entities,
        ReferenceTable frame)
    {
        var events = new List<Event>(600);
        var slugs = new HashSet<string>(StringComparer.Ordinal);

        foreach (var row in Csv.Read(Path.Combine(folder, "BibleData-Event.csv")))
        {
            var reference = frame.Resolve(row["event_reference_id"]);
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
    /// Puts names where the source wrote its own identifiers, in every column that holds prose.
    ///
    /// The sentences that show an event's arithmetic are generated from a template, and the
    /// template interpolates the row id: <em>"The year of Moses_1's birth is the year of the
    /// Exodus (2515) minus his age…"</em>. A reader has no idea there is a Moses_1, and the
    /// underscore and the number are an artefact of a spreadsheet rather than anything about
    /// Moses. The same identifiers are in the distinguishers — <em>"father of Shelemiah_6 (JER
    /// 36:26)"</em> — and in the notes on both entities and relationships.
    ///
    /// This is not editing a quotation. The sentence is machine-written and the identifier stands
    /// for exactly one entity or event; putting the name back is rendering it, not rewriting it.
    /// Anything the corpus cannot resolve is left exactly as it was, because a stray identifier
    /// visible on the page is a better outcome than a wrong name.
    /// </summary>
    internal static void Name(
        Dictionary<string, Entity> entities,
        List<Event> events,
        List<EntityRelationship> relationships)
    {
        // Case-insensitively, because the source writes ZADOK_3 for the row it elsewhere calls
        // Zadok_3, and a reader owed "Zadok" should not be given the shouted identifier instead.
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
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

        // The distinguisher is the field that appears everywhere — the entity list, every other
        // entity's relationship rows, search results — so an identifier left in one shows up in
        // more places than an identifier left anywhere else.
        foreach (var entity in entities.Values)
        {
            entity.Distinguisher = Named(entity.Distinguisher, names);
            entity.Notes = Named(entity.Notes, names);
        }

        foreach (var relationship in relationships)
        {
            relationship.Notes = Named(relationship.Notes, names);
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
    /// The dataset's own verse list, and the only thing that can say whether a citation exists.
    ///
    /// Parsing a reference is not enough. <c>GEN 45:52</c> parses — a known book, two numbers, a
    /// verse that is not zero — and Genesis 45 stops at verse 28, so four relationship rows dated
    /// Ephraim's parentage to a verse nobody has ever read. The dataset ships the 31,102 addresses
    /// it recognises in <c>BibleData-Reference.csv</c>, so the check costs a lookup.
    ///
    /// A citation that does not exist is dropped rather than repaired. The verse meant is plainly
    /// Genesis 41:52, one digit away and one verse after Manasseh's, but reading a transposition
    /// out of a wrong number is a guess, and a guess written into a citation column is
    /// indistinguishable afterwards from what the source said (RUL-0024).
    /// </summary>
    internal sealed class ReferenceTable
    {
        private readonly Dictionary<string, int> _books;
        private readonly HashSet<string> _verses;
        private readonly Dictionary<string, int> _dangling = new(StringComparer.Ordinal);

        private ReferenceTable(Dictionary<string, int> books, HashSet<string> verses)
        {
            _books = books;
            _verses = verses;
        }

        /// <summary>Every citation that parsed and named no verse, with how often it was made.</summary>
        public IReadOnlyDictionary<string, int> Dangling => _dangling;

        public static ReferenceTable Read(string folder) => new(
            Csv.Read(Path.Combine(folder, "BibleData-Book.csv"))
                .ToDictionary(row => row["usx_code"], row => int.Parse(row["book_id"])),
            [.. Csv.Read(Path.Combine(folder, "BibleData-Reference.csv")).Select(row => row["reference_id"])]);

        /// <summary>
        /// <c>GEN 1:1</c> in the shared frame. Answers null for the references the dataset makes
        /// that its own reference table does not hold — mostly Psalm superscriptions numbered
        /// verse 0, which our frame has no address for either, and the book codes it misspells.
        /// </summary>
        public (int Book, int Chapter, int Verse)? Resolve(string? reference)
        {
            if (reference is not { Length: > 0 })
            {
                return null;
            }

            var space = reference.LastIndexOf(' ');
            var colon = reference.LastIndexOf(':');
            if (space <= 0 || colon <= space
                || !_books.TryGetValue(reference[..space], out var book)
                || !int.TryParse(reference[(space + 1)..colon], out var chapter)
                || !int.TryParse(reference[(colon + 1)..], out var verse)
                || verse == 0)
            {
                return null;
            }

            if (!_verses.Contains(reference))
            {
                _dangling[reference] = _dangling.GetValueOrDefault(reference) + 1;
                return null;
            }

            return (book, chapter, verse);
        }
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

    /// <summary>
    /// The words this dataset writes where another would leave the cell empty. They are not
    /// values: <c>none</c> in a Strong column is the absence of a Strong number, and <c>[none]</c>
    /// in a spelling column is the absence of a spelling, so both answer null here rather than
    /// reaching a page as the text a name is written in.
    /// </summary>
    private static readonly HashSet<string> NullTokens =
        new(["none", "[none]", "na", "[na]"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Everything the dataset has been seen to put between two Strong numbers.</summary>
    private static readonly char[] Separators = [',', ';', ' ', '&'];

    internal static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
            : value.Trim() is var trimmed && NullTokens.Contains(trimmed) ? null
                : trimmed;

    private static int? Number(string? value) =>
        int.TryParse(Blank(value), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;

    /// <summary>
    /// The Strong numbers a label carries, in the form <c>strong_entry</c> holds them.
    ///
    /// Four of the dataset's habits have to be undone before a number is a key. It separates the
    /// numbers of a phrase with a comma and sometimes with a space; it pads some with a zero and
    /// doubles the prefix on one; it keeps the lexicon's homograph letter, which distinguishes
    /// <em>Bethuel the man</em> from <em>Bethuel the town</em> but has no entry of its own; and it
    /// writes its null word inside a list, so <c>G935, none</c> is one number and not two.
    ///
    /// The homograph letter is dropped rather than kept, because the entry it would reach does not
    /// exist — the distinction it makes is one the concordance does not carry.
    ///
    /// <paramref name="language"/> is the letter the column already implies, and is what a number
    /// written without one gets: the Hebrew column's <c>H1350, 3478</c> is two Hebrew numbers.
    /// </summary>
    internal static string? Strong(string? value, char language)
    {
        if (Blank(value) is not { } text)
        {
            return null;
        }

        var numbers = new List<string>();
        foreach (var part in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            if (Blank(part) is not { } candidate || StrongNumber().Match(candidate) is not { Success: true } match)
            {
                continue;
            }

            var prefix = match.Groups[1].Value;
            var number = $"{(prefix.Length > 0 ? char.ToUpperInvariant(prefix[^1]) : language)}{match.Groups[2].Value}";
            if (!numbers.Contains(number, StringComparer.Ordinal))
            {
                numbers.Add(number);
            }
        }

        return numbers.Count > 0 ? string.Join(",", numbers) : null;
    }

    /// <summary>
    /// A language letter that may be missing or doubled, the number with any padding zeros, and
    /// the lexicon's trailing homograph letter.
    /// </summary>
    [GeneratedRegex(@"^([HG]*)0*([1-9][0-9]*)[A-Za-z]?$", RegexOptions.IgnoreCase)]
    private static partial Regex StrongNumber();
}
