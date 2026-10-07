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
    DuplicateRecordLoader duplicates,
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

    /// <summary>What the dataset's testimony says once a record it held becomes ours.</summary>
    private const string HeldAsItsOwn =
        "holds this man as a record of its own, which is where this record's relationships, verses and "
        + "descriptors come from and whose they stay";

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
            + "tetrarch.",
            Labels: ["Tetrarch of Ituraea and Trachonitis"]),
        new("person:Zadok_6", "NEH 13:13", "Zadok#7", NotesToo: false,
            "The dataset's record is Zadok son of Meraioth of Nehemiah 11:11, the same list as 1 Chronicles "
            + "9:11. Nehemiah 13:13 names Zadok the scribe, whom Nehemiah made a treasurer over the "
            + "storehouses a generation after the lists of chapter 11."),
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

        // The dataset's verse list puts these on a namesake whose own description, kinship or era
        // it contradicts. Each was read in its context and parallels, disagreed with by a reading of
        // the verse, adjudicated with a second model, and is assigned the same way by the register.
        .. Namesake("person:Zadok_3", "person:Zadok_1", "Zadok#1",
            "BibleData files it under the later Zadok whose son is Shallum (1 Chronicles 6:12). David's "
            + "summons of Zadok and Abiathar, Zadok before the tabernacle at Gibeon, and Ezekiel's sons of "
            + "Zadok are the high priest of 2 Samuel 8:17 and his line.",
            "1CH 15:11", "1CH 16:39", "EZK 40:46", "EZK 43:19", "EZK 44:15", "EZK 48:11"),
        .. Namesake("person:Azariah_7", "person:Azariah_15", "Azariah#26",
            "'Azariah the ruler of the house of God' is the chief priest of the house of Zadok whom 2 "
            + "Chronicles 31:10 names three verses earlier, not the pre-exilic son of Hilkiah of 1 Chronicles 6:13.",
            "2CH 31:13"),
        .. Namesake("person:Jeshua_7", "person:Jeshua_3", "Jeshua#2",
            "'Jeshua begat Joiakim, Joiakim also begat Eliashib' is the high-priestly succession, and "
            + "Nehemiah 12:26 names the same man 'Joiakim the son of Jeshua, the son of Jozadak'; the "
            + "dataset files it under the Levite of Nehemiah 8:7.",
            "NEH 12:10"),
        .. Namesake("person:Ahijah_1", "person:Ahijah_3", "Ahijah#4",
            "'Baasha the son of Ahijah': the dataset's own description makes ahijah-3 Baasha's father, and "
            + "files the verse under the priest of Shiloh.",
            "1KI 15:33"),
        .. Namesake("person:Harim_3", "person:Harim_2", "Harim#3",
            "Ezra 10:25 opens the lay families and 10:31 stands inside them, so this is the lay house of "
            + "Ezra 2:32, whose descendants the dataset's own kinship makes exactly the men of this verse; "
            + "the priestly Harim of Ezra 2:39 is answered at 10:21.",
            "EZR 10:31"),
        .. Namesake("person:Jeremoth_3", "person:Jerimoth_3", "Jerimoth#6",
            "The lots of 1 Chronicles 25 fall to the sons of Asaph, Jeduthun and Heman named in 25:2-4, "
            + "and Jerimoth is Heman's son; the dataset's own description of jerimoth-3 cites this verse. "
            + "It files it under a Merarite of the courses of 1 Chronicles 23.",
            "1CH 25:22"),
        .. Namesake("person:Kadmiel_2", "person:Kadmiel_1", "Kadmiel#1",
            "Nehemiah 12:1-9 is the roster of those who came up with Zerubbabel and Jeshua, and the "
            + "dataset's own split of the two Kadmiels puts the returnee on that side of it, not the "
            + "Levite of Nehemiah 9.",
            "NEH 12:8"),
        .. Namesake("person:Pharaoh_5", "person:Shishak_1", "Shishak#1",
            "The verse names Shishak king of Egypt. The dataset holds a record for Shishak and gives it no "
            + "verse, and files every one of them under the Pharaoh whose daughter Solomon married.",
            "1KI 11:40", "1KI 14:25", "2CH 12:2", "2CH 12:5", "2CH 12:7", "2CH 12:9"),
        .. Namesake("person:Elizaphan_1", "person:Elzaphan_1", "Elizaphan#1",
            "1 Chronicles 15:5-10 lists the Levite houses, so the sons of Elizaphan are the Kohathite house "
            + "of Elzaphan son of Uzziel, not the prince of Zebulun of Numbers 34:25.",
            "1CH 15:8"),
        .. Namesake("person:Seraiah_5", "person:Seraiah_2", "Seraiah#2",
            "The Seraiah who begat Jehozadak is the chief priest of 2 Kings 25:18, whom the dataset's own "
            + "kinship makes Jehozadak's father; it files the verse under a Simeonite of 1 Chronicles 4:35.",
            "1CH 6:14"),
        .. Namesake("person:Seraiah_8", "person:Seraiah_6", "Seraiah#10",
            "Nehemiah 12:12-21 repeats the priestly houses of 12:1-7 in order with the next generation's "
            + "heads, so 'of Seraiah, Meraiah' is the Seraiah of 12:1, whom the dataset's own kinship makes "
            + "Meraiah's father.",
            "NEH 12:12"),
        .. Namesake("person:Nahor_1", "person:Nahor_2", "Nahor#2",
            "Laban's grandfather, and the Nahor whose God is named beside Abraham's as 'the God of their "
            + "father', is Abraham's brother, not their grandfather the son of Serug.",
            "GEN 29:5", "GEN 31:53"),
        .. Namesake("person:Amariah_1", "person:Amariah_2", "Amariah#2",
            "Ezra's line runs Ahitub, Zadok, Shallum, which is the Amariah son of Azariah of 1 Chronicles "
            + "6:11, as the verse itself says; the Amariah of 6:7 fathers the earlier Ahitub.",
            "EZR 7:3"),
        .. Namesake("person:Shebuel_1", "person:Shebuel_2", "Shebuel#2",
            "The thirteenth lot of 1 Chronicles 25 falls among the singers named in 25:2-4, and Shubael is "
            + "Heman's son there; the dataset's own description of shebuel-2 cites this verse. It files it "
            + "under the Gershomite treasurer.",
            "1CH 25:20"),
        .. Namesake("person:Samuel_1", "person:Samuel_2", "Samuel#1",
            "'Samuel the seer' and 'the word of the LORD by Samuel' are the prophet; the Simeonite prince "
            + "of Numbers 34:20 is named in that one verse.",
            "1CH 9:22", "1CH 11:3"),
        .. Namesake("person:Jeroboam_1", "person:Jeroboam_2", "Jeroboam#2",
            "Jeroboam king of Israel is named as Jotham of Judah's contemporary, which is the second "
            + "Jeroboam; the son of Nebat reigned two centuries earlier.",
            "1CH 5:17"),
        .. Namesake("person:Hashabiah_7", "person:Hashabiah_4", "Hashabiah#6",
            "Hashabiah, Sherebiah and Jeshua son of Kadmiel are the company of Ezra 8:18-24. The dataset's "
            + "own kinship makes hashabiah-7 the great-grandfather of the overseer of Nehemiah 11:22, who "
            + "cannot be a chief of the Levites in the same generation.",
            "NEH 12:24"),
        .. Namesake("person:Darius_1", "person:Darius_2", "Darius#1",
            "Darius the Mede, who took the kingdom from Belshazzar and set Daniel over the presidents, is "
            + "the son of Ahasuerus of Daniel 9:1, not the Persian king of Ezra 4:5.",
            "DAN 5:31", "DAN 6:1", "DAN 6:6", "DAN 6:9", "DAN 6:25", "DAN 6:28", "DAN 11:1"),
        .. Namesake("person:Gemariah_1", "person:Gemariah_2", "Gemariah#1",
            "The verses name 'Gemariah the son of Shaphan the scribe'. The dataset holds a record for him "
            + "and gives it no verse, and files these under the son of Hilkiah of Jeremiah 29:3.",
            "JER 36:10", "JER 36:11", "JER 36:12", "JER 36:25"),
        .. Namesake("person:Zechariah_5", "person:Zechariah_4", "Zechariah#3",
            "Zechariah the firstborn of Meshelemiah is the porter of 1 Chronicles 9:21, not the musician "
            + "of 1 Chronicles 15:18-24.",
            "1CH 26:2", "1CH 26:14"),
        .. Namesake("person:Shelemiah_6", "person:Shelemiah_8", "Shelemiah#5",
            "Jehucal son of Shelemiah is Jucal son of Shelemiah of Jeremiah 38:1, which the dataset files "
            + "under shelemiah-8, not the son of Abdeel sent to seize Baruch.",
            "JER 37:3"),
        .. Namesake("person:Deborah_1", "person:Deborah_2", "Deborah#3",
            "The dataset's first Deborah is Rebekah's nurse and its second the prophetess who judged Israel, "
            + "but it numbers the judge by the word for a bee, so her name in Judges 4 and 5 resolved to the "
            + "nurse's record and the register reached the judge there.",
            "JDG 4:4", "JDG 4:5", "JDG 4:9", "JDG 4:10", "JDG 4:14", "JDG 5:1", "JDG 5:7", "JDG 5:12", "JDG 5:15"),
        .. Namesake("person:Ahitub_2", "person:Ahitub_3", "Ahitub#3",
            "Ezra's Ahitub has a son Zadok whose son is Shallum, which is the line of 1 Chronicles 6:11-12; "
            + "the Ahitub of David's Zadok has Ahimaaz for a grandson.",
            "EZR 7:2"),
    ];

    /// <summary>The verses <see cref="MisfiledVerses"/> takes off each record, by the dataset's id for it.</summary>
    private static readonly Dictionary<string, HashSet<(int Book, int Chapter, int Verse)>> Misplaced =
        MisfiledVerses
            .GroupBy(misfiled => misfiled.Held, StringComparer.Ordinal)
            .ToDictionary(
                held => held.Key,
                held => Addresses([.. held.Select(misfiled => misfiled.Reference)]).ToHashSet(),
                StringComparer.Ordinal);

    /// <summary>
    /// One <see cref="Misfiled"/> for each verse the dataset files under one man that names another
    /// record it already holds.
    /// </summary>
    private static IEnumerable<Misfiled> Namesake(
        string held, string target, string bearer, string why, params string[] references) =>
        references.Select(reference => new Misfiled(held, reference, bearer, NotesToo: false, why, Target: target));

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
            logger.LogInformation(
                "Matched the namesakes of the misfiled verses again: {Outcome}",
                await Rematch(PersonRegisterFiles.Read(directory), cancellationToken));
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

        var byGroup = Grouped(held, groups);

        var slugs = await Taken(cancellationToken);

        var made = new Dictionary<Entity, List<PersonRegisterRecord>>();
        var added = new List<Entity>();
        var referenced = 0;

        foreach (var group in groups.Order(StringComparer.Ordinal))
        {
            var mine = records.Where(record => string.Equals(record.Group, group, StringComparison.Ordinal))
                .OrderBy(record => record.Id)
                .ToList();
            var candidates = byGroup.TryGetValue(group, out var found) ? found : [];
            var links = Match(mine, candidates, Attested);

            foreach (var record in mine)
            {
                if (!links.TryGetValue(record.Id, out var hit))
                {
                    hit = Bearer(record, slugs);
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
                BecomeOurs(person);
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
            .Include(entity => entity.Names)
            .AsSplitQuery()
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

        var stranded = TakeTheNamesTheMovedVersesAloneUsed(repairs, bySourceId);

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

        if (repaired > 0 || stranded > 0 || obsolete.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return repaired;
    }

    /// <summary>
    /// The names a record held only for the verses taken off it. The dataset labelled the six Shishak
    /// verses it filed under Solomon's father-in-law with Shishak's name, and once the verses are
    /// Shishak's the name stayed behind, so the father-in-law's page gave Shishak as another name of
    /// his and he stood in the namesake group of a name he never bore. A name leaves the record where
    /// the record it lost verses to bears it, it is not the record's own heading, and no verse still
    /// on the record is labelled with it.
    /// </summary>
    private static int TakeTheNamesTheMovedVersesAloneUsed(
        IReadOnlyList<Misfiled> repairs,
        IReadOnlyDictionary<string, Entity> bySourceId)
    {
        var taken = 0;
        foreach (var (from, to) in repairs
                     .Select(misfiled => (bySourceId.GetValueOrDefault(misfiled.Held), bySourceId.GetValueOrDefault(misfiled.Target!)))
                     .Where(pair => pair.Item1 is not null && pair.Item2 is not null)
                     .Distinct())
        {
            var stranded = from!.Names
                .Where(name => name.Label != from.Name
                               && to!.Names.Any(other => other.Label == name.Label)
                               && !from.Verses.Any(verse => verse.Label == name.Label))
                .ToList();
            foreach (var name in stranded)
            {
                from.Names.Remove(name);
                taken++;
            }
        }

        return taken;
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
                .Concat(misfiled.Labels ?? [])
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
                var says = " under a record it also gives another man of this name; the verse, the labels it "
                           + "uses only there" + (misfiled.NotesToo ? " and its note" : "")
                           + $" are this man's. {misfiled.Why}";
                var said = to.Claims.FirstOrDefault(claim =>
                    claim.Method == LinkMethod.StatedBySource
                    && string.Equals(claim.Source, from.Source, StringComparison.Ordinal));

                // One claim per source on a record, so a second verse moved for the same reason joins
                // the first one's.
                if (said is null)
                {
                    to.Claims.Add(new EntityClaim
                    {
                        Method = LinkMethod.StatedBySource,
                        Confidence = null,
                        Source = from.Source,
                        Note = $"files {misfiled.Reference}{says}",
                    });
                }
                else
                {
                    var several = says.Replace("; the verse, ", "; the verses, ", StringComparison.Ordinal);
                    var ending = said.Note?.EndsWith(says, StringComparison.Ordinal) == true ? says
                        : said.Note?.EndsWith(several, StringComparison.Ordinal) == true ? several
                        : null;
                    if (ending is not null)
                    {
                        said.Note = said.Note![..^ending.Length] + $", {misfiled.Reference}{several}";
                    }
                }
            }

            moved += verses.Count;
        }

        return moved;
    }

    private static PersonRegisterOutcome Nothing(Stopwatch started) =>
        new(true, 0, 0, 0, 0, 0, 0, 0, 0, started.Elapsed);

    /// <summary>
    /// The register matched again, on a corpus it has already been read into, for the names a
    /// misfiled verse belongs to.
    ///
    /// <para>
    /// A corpus read before a verse was moved to the man it names was matched with the verse where
    /// the dataset had put it: the bearer reached the wrong man, that record's claim describes him,
    /// and the man the record really is was added a second time. Matching the name again with the
    /// verses where they now stand says where each bearer belongs, and this moves what the register
    /// wrote to follow it — its claim and the verses it cited — and nothing any other pass wrote.
    /// </para>
    ///
    /// <para>
    /// **A bearer leaves a record only when the record no longer shares a verse with him.** One that
    /// still does and carries him beside another bearer of the name is a record a fold made of two,
    /// which is a reading of ours that one man stands under both, and it is left as it is. A record
    /// the register added for a bearer who now reaches a held one is folded into it, so its address
    /// still arrives; a bearer it reaches nowhere any more is added, as a load from nothing adds him.
    /// </para>
    /// </summary>
    internal async Task<RegisterRematchOutcome> Rematch(
        IReadOnlyList<PersonRegisterRecord> entries,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var bearers = MisfiledVerses.Select(misfiled => misfiled.Bearer).ToHashSet(StringComparer.Ordinal);
        var records = entries.Where(record => record.Kept).ToList();
        var groups = records
            .Where(record => bearers.Contains(record.Key))
            .Select(record => record.Group)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (groups.Count == 0)
        {
            return new RegisterRematchOutcome(0, 0, 0, 0, started.Elapsed);
        }

        var ours = records.Select(record => SourceIdOf(record.Key)).ToHashSet(StringComparer.Ordinal);
        var foldedInto = (await db.MergedRecords
                .Where(m => ours.Contains(m.RecordSourceId))
                .Select(m => new { m.RecordSourceId, m.EntityId })
                .ToListAsync(cancellationToken))
            .GroupBy(m => m.RecordSourceId, StringComparer.Ordinal)
            .ToDictionary(m => m.Key, m => m.First().EntityId, StringComparer.Ordinal);
        var targets = foldedInto.Values.ToList();

        var people = await db.Entities
            .Where(e => e.Kind == EntityKind.Person
                        && (groups.Contains(e.Name)
                            || e.Names.Any(n => n.Kind == ProperName && groups.Contains(n.Label))
                            || targets.Contains(e.Id)))
            .Include(e => e.Names)
            .Include(e => e.Verses)
            .Include(e => e.Claims)
            .AsSplitQuery()
            .ToListAsync(cancellationToken);

        bool AddedHere(Entity person) => ours.Contains(person.SourceId);

        // A record the register added and the list of records written twice folded is the man it was
        // folded into, whatever the verses say now: a bearer the lexicon alone establishes shares no
        // verse with anybody, and would otherwise be added again on every load.
        Entity? Folded(PersonRegisterRecord record) =>
            foldedInto.TryGetValue(SourceIdOf(record.Key), out var id) ? people.FirstOrDefault(p => p.Id == id) : null;
        var held = people.Where(person => !AddedHere(person)).OrderBy(person => person.Id).ToList();
        var byGroup = Grouped(held, groups);

        var attested = new Dictionary<Entity, HashSet<(int Book, int Chapter, int Verse)>>();
        HashSet<(int Book, int Chapter, int Verse)> Attests(Entity person) =>
            attested.TryGetValue(person, out var known) ? known : attested[person] = Attested(person);
        bool Shares(Entity person, PersonRegisterRecord record) =>
            Addresses(record.References).Any(Attests(person).Contains);

        var prefixes = records.Select(record => (Record: record, Prefix: Prefix(record))).ToList();
        var before = new Dictionary<Entity, List<PersonRegisterRecord>>();
        foreach (var person in people)
        {
            if (Ours(person)?.Note is { } note)
            {
                before[person] = prefixes
                    .Where(p => note.StartsWith(p.Prefix, StringComparison.Ordinal)
                                || note.Contains("; " + p.Prefix, StringComparison.Ordinal))
                    .Select(p => p.Record)
                    .ToList();
            }
        }

        var after = before.ToDictionary(pair => pair.Key, pair => pair.Value.ToList());
        List<PersonRegisterRecord> On(Entity person) =>
            after.TryGetValue(person, out var on) ? on : after[person] = [];

        var slugs = await Taken(cancellationToken);
        var folds = new List<(Entity Added, Entity Into, PersonRegisterRecord Record)>();
        var made = new Dictionary<Entity, List<PersonRegisterRecord>>();
        var unplaced = new List<(PersonRegisterRecord Record, Entity Into)>();

        foreach (var group in groups)
        {
            var mine = records
                .Where(record => string.Equals(record.Group, group, StringComparison.Ordinal))
                .OrderBy(record => record.Id)
                .ToList();
            var links = Match(mine, byGroup.TryGetValue(group, out var found) ? found : [], Attests);

            foreach (var record in mine)
            {
                var at = after.Where(pair => pair.Value.Contains(record)).Select(pair => pair.Key).ToList();
                if ((Folded(record) ?? links.GetValueOrDefault(record.Id)) is { } into)
                {
                    if (at.Contains(into) || at.Any(person => !AddedHere(person) && Shares(person, record)))
                    {
                        continue;
                    }

                    foreach (var person in at)
                    {
                        On(person).Remove(record);
                        if (AddedHere(person))
                        {
                            folds.Add((person, into, record));
                        }
                    }

                    if (at.Count == 0)
                    {
                        unplaced.Add((record, into));
                    }
                    else
                    {
                        On(into).Add(record);
                    }

                    continue;
                }

                var stale = at.Where(person => !AddedHere(person) && !Shares(person, record)).ToList();
                if (stale.Count == 0)
                {
                    continue;
                }

                foreach (var person in stale)
                {
                    On(person).Remove(record);
                }

                if (at.Count == stale.Count)
                {
                    var bearer = Bearer(record, slugs);
                    db.Entities.Add(bearer);
                    made[bearer] = [record];
                    On(bearer).Add(record);
                }
            }
        }

        // A bearer no record names goes to the record he reaches once the others have moved, and
        // only to one carrying no other bearer of his name: a record carrying two is a fold's.
        foreach (var (record, into) in unplaced)
        {
            if (!On(into).Any(other => string.Equals(other.Group, record.Group, StringComparison.Ordinal)))
            {
                On(into).Add(record);
            }
        }

        var rewritten = 0;
        foreach (var (person, now) in after)
        {
            var was = before.TryGetValue(person, out var earlier) ? earlier : [];
            if (was.ToHashSet().SetEquals(now))
            {
                continue;
            }

            rewritten++;
            foreach (var record in now.Except(was))
            {
                Cite(person, record);
            }

            foreach (var record in was.Except(now))
            {
                Uncite(person, record, now);
            }

            var claim = Ours(person);
            if (now.Count == 0)
            {
                if (claim is not null)
                {
                    person.Claims.Remove(claim);
                    db.EntityClaims.Remove(claim);
                }

                if (!AddedHere(person)
                    && person.Claims.FirstOrDefault(c => c.Method == LinkMethod.StatedBySource
                                                         && c.Note == HeldAsItsOwn) is { } theirs)
                {
                    person.Source = theirs.Source;
                    person.Claims.Remove(theirs);
                    db.EntityClaims.Remove(theirs);
                }

                continue;
            }

            var fresh = Claim([.. now.OrderBy(record => record.Group, StringComparer.Ordinal).ThenBy(record => record.Id)]);
            if (claim is null)
            {
                if (!string.Equals(person.Source, FromTheEnumeration, StringComparison.Ordinal))
                {
                    BecomeOurs(person);
                }

                person.Claims.Add(fresh);
            }
            else
            {
                claim.Note = fresh.Note;
                claim.Confidence = fresh.Confidence;
            }
        }

        // After the claims, so a record the register lets go of says whose it is again before the
        // dataset's verses leave it with that dataset's name on them.
        var refiled = Refile(held, made);
        await db.SaveChangesAsync(cancellationToken);

        var folded = 0;
        if (folds.Count > 0)
        {
            var pairs = folds
                .Select(fold => new DuplicateRecordPair(
                    fold.Record.StrongNumbers?.FirstOrDefault() ?? "",
                    fold.Into.Slug,
                    fold.Added.Slug,
                    $"The person register added this record for {fold.Record.Group} #{fold.Record.Id} while the "
                    + "verse that names him was still filed under a namesake. With the verse where it belongs, "
                    + "the register reaches the record he is on."))
                .ToList();
            folded = (await duplicates.Fold(
                new DuplicateRecordList(ByTheReading, OnTheVerses, FromTheEnumeration, pairs),
                cancellationToken)).Folded;
        }

        return new RegisterRematchOutcome(rewritten, folded, made.Count, refiled, started.Elapsed);
    }

    /// <summary>
    /// The verses a held record attests a bearer by: every verse on its list but the ones this pass
    /// cited for a bearer itself, which would draw him back to wherever he was put, and the ones
    /// <see cref="MisfiledVerses"/> says the dataset filed under it for another man, which are that
    /// man's evidence and not this one's.
    /// </summary>
    private static HashSet<(int Book, int Chapter, int Verse)> Attested(Entity person)
    {
        var elsewhere = Misplaced.TryGetValue(person.SourceId, out var verses) ? verses : [];
        return person.Verses
            .Where(verse => !string.Equals(verse.Source, FromTheAssignment, StringComparison.Ordinal))
            .Select(verse => (verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse))
            .Where(address => !elsewhere.Contains(address))
            .ToHashSet();
    }

    /// <summary>The held people each name's bearers can reach, lowest row first.</summary>
    private static Dictionary<string, List<Entity>> Grouped(IEnumerable<Entity> held, IReadOnlyCollection<string> groups)
    {
        var byGroup = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
        foreach (var person in held.OrderBy(e => e.Id))
        {
            foreach (var label in person.Names
                         .Where(name => name.Kind == ProperName)
                         .Select(name => name.Label)
                         .Append(person.Name)
                         .Distinct(StringComparer.Ordinal)
                         .Where(groups.Contains))
            {
                if (!byGroup.TryGetValue(label, out var already))
                {
                    byGroup[label] = already = [];
                }

                already.Add(person);
            }
        }

        return byGroup;
    }

    /// <summary>
    /// Every address a new record may not take: the records held, and the ones folded into another,
    /// whose address still arrives where it was folded.
    /// </summary>
    private async Task<HashSet<string>> Taken(CancellationToken cancellationToken)
    {
        var slugs = await db.Entities.Select(e => e.Slug).ToListAsync(cancellationToken);
        var folded = await db.MergedRecords.Select(m => m.Slug).ToListAsync(cancellationToken);
        return slugs.Concat(folded).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>A record for a bearer no held record is.</summary>
    private static Entity Bearer(PersonRegisterRecord record, HashSet<string> slugs) =>
        new()
        {
            Kind = EntityKind.Person,
            Slug = Unique(Slugs.Of(record.Name), slugs),
            Name = record.Name,
            Distinguisher = record.Description,
            SourceId = SourceIdOf(record.Key),
            Source = FromTheEnumeration,
            Names = { Name(record) },
        };

    /// <summary>The dataset's testimony kept as a claim, as the record becomes ours.</summary>
    private static void BecomeOurs(Entity person)
    {
        person.Claims.Add(new EntityClaim
        {
            Method = LinkMethod.StatedBySource,
            Confidence = null,
            Source = person.Source,
            Note = HeldAsItsOwn,
        });

        person.Source = FromTheEnumeration;
    }

    /// <summary>The claim this pass writes on a record, if it has written one.</summary>
    private static EntityClaim? Ours(Entity person) =>
        person.Claims.FirstOrDefault(claim =>
            claim.Method == ByTheReading && string.Equals(claim.Source, FromTheEnumeration, StringComparison.Ordinal));

    /// <summary>How a claim begins what it says of one bearer, as <see cref="Says"/> writes it.</summary>
    private static string Prefix(PersonRegisterRecord record) => $"{record.Group} #{record.Id}, ";

    /// <summary>
    /// The verses this pass cited on a record for a bearer who is no longer on it, but for those a
    /// bearer who stays there was cited for too.
    /// </summary>
    private void Uncite(Entity person, PersonRegisterRecord record, IReadOnlyList<PersonRegisterRecord> staying)
    {
        var kept = staying.SelectMany(other => Addresses(other.References)).ToHashSet();
        var gone = Addresses(record.References).Where(address => !kept.Contains(address)).ToHashSet();
        foreach (var verse in person.Verses
                     .Where(verse => string.Equals(verse.Source, FromTheAssignment, StringComparison.Ordinal)
                                     && gone.Contains((verse.CanonicalBook, verse.CanonicalChapter, verse.CanonicalVerse)))
                     .ToList())
        {
            person.Verses.Remove(verse);
            db.EntityVerses.Remove(verse);
        }
    }

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
        IReadOnlyList<Entity> candidates,
        Func<Entity, HashSet<(int Book, int Chapter, int Verse)>> attests)
    {
        var reached = new HashSet<Entity>();
        var attested = candidates.ToDictionary(person => person, attests);

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

/// <param name="Rewritten">Records whose claim now names other bearers than it did.</param>
/// <param name="Folded">Records the register added for a man it now reaches on a record already held, folded into it.</param>
/// <param name="Added">Bearers the register now reaches nowhere, added as a load from nothing adds them.</param>
/// <param name="Refiled">Verse rows moved to a bearer added here, from the record the dataset filed them under.</param>
internal sealed record RegisterRematchOutcome(int Rewritten, int Folded, int Added, int Refiled, TimeSpan Elapsed)
{
    public override string ToString() =>
        Rewritten + Folded + Added == 0
            ? "every bearer of those names is on the record its verses reach"
            : $"{Rewritten} records now name the bearers their verses reach, {Folded} records added for a man "
              + $"already held folded into his, and {Added} bearers added with {Refiled} verses the dataset "
              + $"filed under a namesake, in {Elapsed}";
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
/// <param name="Labels">
/// Names the dataset gives the namesake at that verse and nowhere else, which go with it. Its verse
/// list holds a verse once, under one label, so a title it also lists there is on no verse row.
/// </param>
internal sealed record Misfiled(
    string Held,
    string Reference,
    string Bearer,
    bool NotesToo,
    string Why,
    string? Target = null,
    string? Obsolete = null,
    IReadOnlyList<string>? Labels = null);
