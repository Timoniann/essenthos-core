using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Claimed">
/// Persons the encyclopedia already held that our own derivation reaches. The row stays and its slug
/// with it; what changes is who says the man is a man of his own.
/// </param>
/// <param name="Added">Bearers the derivation reaches that no entity held.</param>
/// <param name="Untouched">
/// Entities of these names nothing of ours reaches, which keep exactly the provenance they had. This
/// is the number that says what the design costs.
/// </param>
/// <param name="OnAVerse">Records a verse of this corpus establishes.</param>
/// <param name="OnTheLexicon">
/// Records nothing we hold distinguishes — the price of the maximal grain, and the figure a reader
/// of the register is owed before any other.
/// </param>
/// <param name="Referenced">
/// Verse rows written, each one a verse that prints the name and that the record did not already
/// hold. It is what a reader sees of the assignment: a page that gains the verses this reading gives
/// it, beside the ones the dataset gave it.
/// </param>
internal sealed record PersonRegisterOutcome(
    bool AlreadyLoaded,
    int Entries,
    int Records,
    int Claimed,
    int Added,
    int Untouched,
    int OnAVerse,
    int OnTheLexicon,
    int Referenced,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the person register is already there"
            : $"{Records} persons of {Entries} bearers read — {Claimed} of them somebody the " +
              $"encyclopedia already held, now here because this corpus draws the line where it " +
              $"does, and {Added} added with {Referenced} verses of their own. {OnAVerse} stand on " +
              $"a verse and {OnTheLexicon} on the enumeration alone. {Untouched} held namesakes " +
              $"nothing of ours reaches keep the provenance they had, in {Elapsed}";
}

/// <summary>
/// The men who share a name, told apart — as a record of ours rather than a count somebody lent us.
///
/// 3,009 persons existed because BibleData drew the lines that way, and 1,801 of them share a name
/// with somebody. Strong drew the lines too and wrote his out: <c>detailed_definition</c> enumerates
/// the bearers with a distinguishing clause on each, for 508 of the 528 namesake groups. The owner
/// has chosen the maximal grain that enumeration describes — eight Nathans, two Absaloms — so the
/// pass in <c>scripts/persons.py</c> takes the enumeration as its spine and assigns the occurrences
/// to it, and this reads the result in.
///
/// <para>
/// <strong>What it does to a row that already exists.</strong> Where a bearer's verses meet those of
/// a person the encyclopedia holds, the two are the same man: the row stays and its slug stays with
/// it, because <c>entity_verse</c>, <c>entity_relationship</c>, <c>entity_descriptor</c> and the
/// reader's own URLs all key on exactly that. What changes is <see cref="Entity.Source"/>, and the
/// dataset's testimony moves to a claim beside ours rather than standing as the reason the record
/// exists.
/// </para>
///
/// <para>
/// <strong>What it does to a bearer nobody holds.</strong> Adds him, with the verses the reading
/// assigns him — and only the verses that print his name. A verse a dataset attributes to somebody
/// of this name without naming him is carried on the record as context and is never written as a
/// reference, because a page whose citation does not contain the person is worse than a page with
/// none.
/// </para>
///
/// <para>
/// <strong>What it does to a namesake nothing reaches.</strong> Nothing. Jezebel calls Jehu
/// <em>Zimri</em> once, so Jehu is a member of the Zimri group and forty-eight of his verses are in
/// its occurrence list; no bearer of that name reaches him and his record stays exactly as it was.
/// </para>
///
/// <para>
/// <strong>A split the text does not make is still a split, and the row says so.</strong> Under this
/// grain some persons stand on the lexicon's say-so and no verse of ours. That is a difference in
/// what established the record and not licence to cite a verse nobody read, so the claim
/// on such a record says which it is, in its own words and with its own confidence, and cites
/// nothing.
/// </para>
///
/// <para>
/// Idempotent on the claim this pass writes, which is the one thing that cannot be true before it
/// has run.
/// </para>
/// </summary>
internal sealed class PersonRegisterLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<PersonRegisterLoader> logger)
{
    /// <summary>
    /// What a record made from the enumeration says about itself. The record is ours — Strong wrote
    /// a dictionary and not a census, and which occurrence belongs to which of his numbered men is
    /// nobody's statement — and the entries it is made of are his.
    /// </summary>
    private const string FromTheEnumeration =
        "Essenthos, from the bearers Strong's Dictionary enumerates under the name";

    /// <summary>
    /// The verses an added bearer is named in, said in the words that make it a reading rather than
    /// a citation lent by a dataset. <c>entity_verse</c> carries no method and no confidence, so the
    /// source has to carry both, and it does.
    /// </summary>
    private const string FromTheAssignment =
        "Essenthos, from the person split — the King James prints the name here and a reading of "
        + "the whole namesake group assigns the verse to this bearer";

    /// <summary>
    /// What established a record: a model read the verses of a shared name and said which of the
    /// enumerated men each one is about. It is a reading, so it carries a number — this corpus
    /// refuses to store an inference that looks like a dictionary's statement.
    /// </summary>
    private const LinkMethod ByTheReading = LinkMethod.ModelReading;

    /// <summary>
    /// The room a record with verses of its own leaves. The lexicon proposes the man, a verse of
    /// this corpus prints his name, and a reading puts the two together — three things agreeing,
    /// and what is short of certainty is that the reading is the third of them.
    /// </summary>
    private const double OnTheVerses = 0.9;

    /// <summary>
    /// The room a record with no verse of its own leaves. Lower on purpose and by a wide margin:
    /// nothing this corpus holds tells this man from his namesake, and what carries him is a
    /// nineteenth-century lexicographer's numbered clause and a second reading of it.
    /// </summary>
    private const double OnTheEnumeration = 0.7;

    private const string SourceIdPrefix = "essenthos:";

    /// <summary>Why Genesis 36's Adah is moved: the same woman the dataset keeps as Basemath.</summary>
    private const string AdahIsBasemath =
        "BibleData's Adah record is the wife of Lamech, but it also files the Hittite wife of Esau "
        + "under it. The same dataset names that woman Basemath and gives her Adah as a name label.";

    /// <summary>
    /// Verses a dataset files under a held man that belong to a namesake the register adds, by the
    /// dataset's id for the man, the verse, and the register's key for the namesake, with the reason.
    ///
    /// <para>
    /// Moved and not merely stood beside, which is what this pass otherwise does when two witnesses
    /// disagree about whom a verse names. Here the dataset does not disagree: it gives the verse to a
    /// record in which it has put two men together, and its own labels and notes at that verse describe
    /// the other one. The rows keep the dataset's source, because it is the dataset's testimony about
    /// the man the verse names, and the record they land on says where they came from.
    /// </para>
    /// </summary>
    internal static readonly IReadOnlyList<Misfiled> MisfiledVerses =
    [
        new("person:Philip_2", "LUK 3:1", "Philip#3", NotesToo: true,
            "Two sons of Herod the Great are called Philip. Luke 3:1 names the tetrarch of Ituraea and "
            + "Trachonitis; Matthew 14:3 and Mark 6:17 name Herodias's first husband, whom Josephus "
            + "calls Herod and who ruled nothing. The dataset holds both as one man, filed under the "
            + "husband's verses, with the tetrarch's verse, his title and a note calling him the "
            + "tetrarch."),
        new("person:Adah_1", "GEN 36:2", "Adah#2", NotesToo: false,
            AdahIsBasemath, Target: "person:Basemath_1", Obsolete: "essenthos:adah1"),
        new("person:Adah_1", "GEN 36:4", "Adah#2", NotesToo: false,
            AdahIsBasemath, Target: "person:Basemath_1", Obsolete: "essenthos:adah1"),
        new("person:Adah_1", "GEN 36:10", "Adah#2", NotesToo: false,
            AdahIsBasemath, Target: "person:Basemath_1", Obsolete: "essenthos:adah1"),
        new("person:Adah_1", "GEN 36:12", "Adah#2", NotesToo: false,
            AdahIsBasemath, Target: "person:Basemath_1", Obsolete: "essenthos:adah1"),
        new("person:Adah_1", "GEN 36:16", "Adah#2", NotesToo: false,
            AdahIsBasemath, Target: "person:Basemath_1", Obsolete: "essenthos:adah1"),
    ];

    /// <summary>The source id a record added for a register's bearer is written under.</summary>
    internal static string SourceIdOf(string key) => SourceIdPrefix + Slugs.Of(key);

    /// <summary>The kind of label a namesake group is made of, and the kind an added bearer gets.</summary>
    private const string ProperName = "proper name";

    public async Task<PersonRegisterOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();

        var repaired = await RepairMisfiledVerses(cancellationToken);

        var directory = configuration[PersonRegisterFiles.ConfigurationKey] is { Length: > 0 } set
            ? set
            : Path.Combine(resources, PersonRegisterFiles.DefaultFolder);

        if (!Directory.Exists(directory))
        {
            logger.LogWarning(
                "No person register at {Directory}, so the namesakes stay as the dataset drew them. "
                + "Produce it with \"python scripts/persons.py register\" and \"publish\", or point "
                + "{Key} at a folder that holds it",
                directory,
                PersonRegisterFiles.ConfigurationKey);
            return Nothing(started);
        }

        if (await db.EntityClaims.AnyAsync(c => c.Source == FromTheEnumeration, cancellationToken))
        {
            logger.LogInformation(
                repaired == 0
                    ? "The person register is already there; nothing to do"
                    : "The person register is already there; repaired {Verses} misfiled verses",
                repaired);
            return Nothing(started);
        }

        var entries = PersonRegisterFiles.Read(directory);
        var records = entries.Where(record => record.Kept).ToList();
        if (records.Count == 0)
        {
            logger.LogWarning(
                "The person register at {Directory} holds {Entries} bearers and no records. Either "
                + "the reading pass has not run or every bearer was refused; check "
                + "\"python scripts/persons.py register\"",
                directory,
                entries.Count);
            return new PersonRegisterOutcome(false, entries.Count, 0, 0, 0, 0, 0, 0, 0, started.Elapsed);
        }

        var groups = records.Select(record => record.Group).Distinct(StringComparer.Ordinal).ToList();

        // Two ways a held person is a candidate for a name, and the second is not a nicety. A group
        // is built out of `entity_name`, so a record carrying no row there — a record written after
        // this pass ran, or before its writer named anything — is invisible to the group, and a
        // second page would be added for a man already on one. An entity whose own name is the label
        // is a candidate too, and the verses still decide.
        var held = await db.Entities
            .Where(e => e.Kind == EntityKind.Person
                        && (groups.Contains(e.Name)
                            || e.Names.Any(n => n.Kind == ProperName && groups.Contains(n.Label))))
            .Include(e => e.Names)
            .Include(e => e.Verses)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        var byGroup = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
        foreach (var person in held.OrderBy(e => e.Id))
        {
            foreach (var label in person.Names
                         .Where(name => name.Kind == ProperName)
                         .Select(name => name.Label)
                         .Append(person.Name)
                         .Distinct(StringComparer.Ordinal)
                         .Where(label => groups.Contains(label)))
            {
                if (!byGroup.TryGetValue(label, out var already))
                {
                    byGroup[label] = already = [];
                }

                already.Add(person);
            }
        }

        var slugs = (await db.Entities.Select(e => e.Slug).ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var made = new Dictionary<Entity, List<PersonRegisterRecord>>();
        var added = new List<Entity>();
        var referenced = 0;

        foreach (var group in groups.Order(StringComparer.Ordinal))
        {
            var mine = records.Where(record => string.Equals(record.Group, group, StringComparison.Ordinal))
                .OrderBy(record => record.Id)
                .ToList();
            var candidates = byGroup.TryGetValue(group, out var found) ? found : [];
            var links = Match(mine, candidates);

            foreach (var record in mine)
            {
                if (!links.TryGetValue(record.Id, out var hit))
                {
                    hit = new Entity
                    {
                        Kind = EntityKind.Person,
                        Slug = Unique(Slugs.Of(record.Name), slugs),
                        Name = record.Name,
                        Distinguisher = record.Description,
                        SourceId = SourceIdOf(record.Key),
                        Source = FromTheEnumeration,
                        Names = { Name(record) },
                    };

                    added.Add(hit);
                }

                referenced += Cite(hit, record);

                if (!made.TryGetValue(hit, out var already))
                {
                    made[hit] = already = [];
                }

                already.Add(record);
            }
        }

        var refiled = Refile(held, made);

        foreach (var (person, from) in made)
        {
            if (!string.Equals(person.Source, FromTheEnumeration, StringComparison.Ordinal))
            {
                person.Claims.Add(new EntityClaim
                {
                    Method = LinkMethod.StatedBySource,
                    Confidence = null,
                    Source = person.Source,
                    Note = $"holds this man as {person.SourceId}, which is where this record's "
                           + "relationships, verses and descriptors come from and whose they stay",
                });

                person.Source = FromTheEnumeration;
            }

            person.Claims.Add(Claim(from));
        }

        db.Entities.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);

        var claimed = made.Keys.Count - added.Count;
        var outcome = new PersonRegisterOutcome(
            false,
            entries.Count,
            records.Count,
            claimed,
            added.Count,
            held.Count - claimed,
            records.Count(record => record.Standing == PersonRegisterFiles.ByAVerse),
            records.Count(record => record.Standing == PersonRegisterFiles.ByTheLexicon),
            referenced,
            started.Elapsed);

        logger.LogInformation("Told the namesakes apart: {Outcome}", outcome);
        if (refiled > 0)
        {
            logger.LogInformation(
                "Moved {Verses} verse rows a dataset filed under the wrong man of a name to the namesake "
                + "they belong to",
                refiled);
        }

        return outcome;
    }

    /// <summary>
    /// Corrects the small set of source rows that identify a different bearer from the one their
    /// source record describes, before the shared-name matcher sees them. Moving the rows first
    /// lets the ordinary evidence-based matcher retain both people rather than minting a duplicate.
    /// </summary>
    private async Task<int> RepairMisfiledVerses(CancellationToken cancellationToken)
    {
        var repairs = MisfiledVerses.Where(misfiled => misfiled.Target is not null).ToList();
        if (repairs.Count == 0)
        {
            return 0;
        }

        var sourceIds = repairs
            .SelectMany(misfiled => new[] { misfiled.Held, misfiled.Target! })
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var held = await db.Entities
            .Where(entity => sourceIds.Contains(entity.SourceId))
            .Include(entity => entity.Verses)
            .ToListAsync(cancellationToken);
        var bySourceId = held.ToDictionary(entity => entity.SourceId, StringComparer.Ordinal);
        var repaired = 0;

        foreach (var misfiled in repairs)
        {
            var addresses = Addresses([misfiled.Reference]).ToList();
            if (!bySourceId.TryGetValue(misfiled.Held, out var from)
                || !bySourceId.TryGetValue(misfiled.Target!, out var to)
                || addresses.Count != 1)
            {
                continue;
            }

            var address = addresses[0];
            var verses = from.Verses
                .Where(verse => (verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse)
                    == (address.Book, address.Chapter, address.Verse))
                .ToList();
            foreach (var verse in verses)
            {
                from.Verses.Remove(verse);
                if (!to.Verses.Any(existing =>
                        (existing.CanonicalBook, existing.CanonicalChapter, existing.CanonicalVerse, existing.Source)
                        == (verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse, verse.Source)))
                {
                    to.Verses.Add(verse);
                }

                repaired++;
            }
        }

        var obsolete = repairs
            .Select(misfiled => misfiled.Obsolete)
            .Where(sourceId => !string.IsNullOrWhiteSpace(sourceId))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (obsolete.Count > 0)
        {
            var stale = await db.Entities
                .Where(entity => obsolete.Contains(entity.SourceId))
                .ToListAsync(cancellationToken);
            db.Entities.RemoveRange(stale);
        }

        if (repaired > 0 || obsolete.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return repaired;
    }

    /// <summary>
    /// Moves each of <see cref="MisfiledVerses"/> to its namesake: the dataset's rows at the verse,
    /// the labels it uses only there, and its notes where they describe the namesake. Run before the
    /// held records' provenance is rewritten, so the claim it leaves names the dataset.
    /// </summary>
    private static int Refile(
        IReadOnlyList<Entity> held,
        Dictionary<Entity, List<PersonRegisterRecord>> made)
    {
        var moved = 0;
        foreach (var misfiled in MisfiledVerses)
        {
            var from = held.FirstOrDefault(e => string.Equals(e.SourceId, misfiled.Held, StringComparison.Ordinal));
            var to = made
                .Where(pair => pair.Value.Any(record => string.Equals(record.Key, misfiled.Bearer, StringComparison.Ordinal)))
                .Select(pair => pair.Key)
                .FirstOrDefault();
            var at = Addresses([misfiled.Reference]).ToList();
            if (from is null || to is null || ReferenceEquals(from, to) || at.Count != 1)
            {
                continue;
            }

            var verses = from.Verses
                .Where(verse => (verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse) == at[0])
                .ToList();
            if (verses.Count == 0)
            {
                continue;
            }

            foreach (var verse in verses)
            {
                from.Verses.Remove(verse);
                to.Verses.Add(verse);
            }

            var stillUsed = from.Verses.Select(verse => verse.Label).OfType<string>().ToHashSet(StringComparer.Ordinal);
            var labels = verses.Select(verse => verse.Label).OfType<string>()
                .Where(label => !stillUsed.Contains(label))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var name in from.Names.Where(name => labels.Contains(name.Label)).ToList())
            {
                from.Names.Remove(name);
                if (!to.Names.Any(mine => mine.Label == name.Label && mine.Kind == name.Kind))
                {
                    to.Names.Add(name);
                }
            }

            if (misfiled.NotesToo && from.Notes is { Length: > 0 } notes)
            {
                to.Notes ??= notes;
                from.Notes = null;
            }

            if (!string.Equals(to.Source, from.Source, StringComparison.Ordinal))
            {
                to.Claims.Add(new EntityClaim
                {
                    Method = LinkMethod.StatedBySource,
                    Confidence = null,
                    Source = from.Source,
                    Note = $"files {misfiled.Reference} under {from.SourceId}, a record it also gives "
                           + "another man of this name; the verse, the labels it uses only there"
                           + (misfiled.NotesToo ? " and its note" : "")
                           + $" are this man's. {misfiled.Why}",
                });
            }

            moved += verses.Count;
        }

        return moved;
    }

    private static PersonRegisterOutcome Nothing(Stopwatch started) =>
        new(true, 0, 0, 0, 0, 0, 0, 0, 0, started.Elapsed);

    /// <summary>
    /// The verses this reading gives a bearer that his record does not already hold, added beside
    /// whatever the dataset put there rather than instead of it.
    ///
    /// <para>
    /// It is what makes the assignment visible at all: without it a pass whose whole subject is
    /// which of twenty-four Zechariahs a verse is about would change a page's provenance and not one
    /// of its references. Additive on purpose — a row a dataset wrote stays, ours stands beside it,
    /// and <see cref="EntityVerse.Source"/> says which is which, because two witnesses disagreeing
    /// about whom a verse names is a fact about the corpus and not a conflict to be resolved by
    /// deleting one of them.
    /// </para>
    ///
    /// <para>
    /// Only the references that print the name. A verse a dataset attributes to somebody of this
    /// name without naming him belongs to every namesake equally, and a page whose citation does not
    /// contain the person is worse than a page with none.
    /// </para>
    /// </summary>
    private static int Cite(Entity person, PersonRegisterRecord record)
    {
        var already = person.Verses
            .Select(verse => (verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse))
            .ToHashSet();

        var written = 0;
        foreach (var address in Addresses(record.References).Where(address => already.Add(address)))
        {
            person.Verses.Add(new EntityVerse
            {
                CanonicalBook = address.Book,
                CanonicalChapter = address.Chapter,
                CanonicalVerse = address.Verse,
                Label = record.Name,
                Source = FromTheAssignment,
            });
            written++;
        }

        return written;
    }

    /// <summary>
    /// Which held person each bearer is, by the verses the two have in common.
    ///
    /// A bearer's evidence is the verses the reading assigned it, and a held person's is the verses
    /// a dataset attests them in; where those meet, the two are the same man and the encyclopedia's
    /// row stays with its slug. Only the references that print the name are matched on — a verse
    /// attributed to somebody of the name without naming him belongs to every namesake equally and
    /// so identifies none of them.
    ///
    /// <para>
    /// The pairs are taken in order of how much they share, each held person is claimed once within
    /// a name, and a bearer whose verses touch nothing held is somebody we reach that nobody else
    /// holds. Ties fall to the lower bearer and then the lower row, so the answer does not depend on
    /// dictionary order. <c>matched</c> in <c>scripts/persons.py</c> is the same rule in Python and
    /// the two are compared rather than trusted.
    /// </para>
    ///
    /// <para>
    /// Once within a name and not once altogether: Zimri son of Zerah is the man the dataset files
    /// under Zabdi, so both names reach that one row and both are recorded on it. Claiming globally
    /// would give the second name a page of its own for a man already on one, which is the namesake
    /// problem made worse rather than settled.
    /// </para>
    /// </summary>
    private static Dictionary<int, Entity> Match(
        IReadOnlyList<PersonRegisterRecord> records,
        IReadOnlyList<Entity> candidates)
    {
        var reached = new HashSet<Entity>();
        var attested = candidates.ToDictionary(
            person => person,
            person => person.Verses
                .Select(verse => (verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse))
                .ToHashSet());

        var pairs = new List<(int Shared, int Bearer, int Person, PersonRegisterRecord Record, Entity Entity)>();
        foreach (var record in records)
        {
            var mine = Addresses(record.References)
                .Select(address => (address.Book, address.Chapter, address.Verse))
                .ToHashSet();
            if (mine.Count == 0)
            {
                continue;
            }

            foreach (var person in candidates)
            {
                var shared = attested[person].Count(mine.Contains);
                if (shared > 0)
                {
                    pairs.Add((shared, record.Id, person.Id, record, person));
                }
            }
        }

        var links = new Dictionary<int, Entity>();
        foreach (var pair in pairs
                     .OrderByDescending(pair => pair.Shared)
                     .ThenBy(pair => pair.Bearer)
                     .ThenBy(pair => pair.Person))
        {
            if (links.ContainsKey(pair.Bearer) || reached.Contains(pair.Entity))
            {
                continue;
            }

            links[pair.Bearer] = pair.Entity;
            reached.Add(pair.Entity);
        }

        return links;
    }

    /// <summary>
    /// One claim per record, naming every bearer it was reached by and what stood behind each. One
    /// row rather than one per bearer because a claim is unique on the entity, the method and the
    /// source — and because a person can be reached from two namesake groups at once, Zimri son of
    /// Zerah being the man another spelling files under Zabdi.
    /// </summary>
    private static EntityClaim Claim(IReadOnlyList<PersonRegisterRecord> made) =>
        new()
        {
            Method = ByTheReading,
            Confidence = made.Any(record => record.Standing == PersonRegisterFiles.ByAVerse)
                ? OnTheVerses
                : OnTheEnumeration,
            Source = FromTheEnumeration,
            Note = string.Join("; ", made.Select(Says)),
        };

    private static string Says(PersonRegisterRecord record)
    {
        var lexicon = record.Enumeration is { Length: > 0 } clause
            ? $"\"{clause}\""
            : "no clause of the lexicon";
        return record.Standing == PersonRegisterFiles.ByTheLexicon
            ? $"{record.Group} #{record.Id}, {lexicon} — no verse of this corpus tells him from "
              + $"his namesakes, and the record stands on the enumeration: {record.Why}"
            : $"{record.Group} #{record.Id}, {lexicon} — {record.Why}";
    }

    private static EntityName Name(PersonRegisterRecord record) =>
        new()
        {
            Label = record.Name,
            Kind = ProperName,
            HebrewStrongNumber = (record.StrongNumbers ?? []).FirstOrDefault(n => n.StartsWith('H')),
            GreekStrongNumber = (record.StrongNumbers ?? []).FirstOrDefault(n => n.StartsWith('G')),
        };

    /// <summary>
    /// The canonical addresses a register's references name. A reference the frame has no ordinal
    /// for is skipped rather than guessed at — the register is written from this corpus's own
    /// occurrence lists, so one that does not parse is a defect in the file and not a book beyond
    /// the canon.
    /// </summary>
    private static IEnumerable<(int Book, int Chapter, int Verse)> Addresses(
        IReadOnlyList<string>? references)
    {
        foreach (var reference in references ?? [])
        {
            var space = reference.LastIndexOf(' ');
            var colon = reference.LastIndexOf(':');
            if (space <= 0 || colon <= space
                || !BookCodes.TryGetOrdinal(reference[..space], out var book)
                || !int.TryParse(reference[(space + 1)..colon], out var chapter)
                || !int.TryParse(reference[(colon + 1)..], out var verse))
            {
                continue;
            }

            yield return (book, chapter, verse);
        }
    }

    private static string Unique(string slug, HashSet<string> taken)
    {
        var candidate = slug;
        var suffix = 2;
        while (!taken.Add(candidate))
        {
            candidate = $"{slug}-{suffix++}";
        }

        return candidate;
    }
}

/// <param name="Held">The dataset's id for the man the verse is filed under.</param>
/// <param name="Reference">The verse, as the register writes it.</param>
/// <param name="Bearer">The register's key for the namesake the verse belongs to.</param>
/// <param name="NotesToo">Whether the dataset's note on the record describes the namesake too.</param>
/// <param name="Why">Why the verse is the namesake's, which the claim on his record repeats.</param>
/// <param name="Target">
/// The dataset's id for a record it already holds for the namesake. Where there is one the verse is
/// moved to it before the register is matched, on every load, so the register never mints a second
/// record for somebody the dataset already has.
/// </param>
/// <param name="Obsolete">
/// The source id of the duplicate an earlier load minted because the verse had not been moved yet;
/// removed where it is still there.
/// </param>
internal sealed record Misfiled(
    string Held,
    string Reference,
    string Bearer,
    bool NotesToo,
    string Why,
    string? Target = null,
    string? Obsolete = null);
