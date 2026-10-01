using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Records added, which is all of them on a cold corpus and none after.</param>
/// <param name="Revised">Records already held whose file entry has changed since, rewritten to it.</param>
/// <param name="Missing">
/// Things the file names that could not be written: a slug another record already holds, or a
/// relation to a record the encyclopedia does not.
/// </param>
/// <param name="Words">Witness words the rules settle, before the links carry them anywhere.</param>
/// <param name="Refused">Words two records' rules both claim, which neither is given.</param>
/// <param name="ByText">Words naming one of these records afterwards, per text, the carried ones included.</param>
/// <param name="About">What the list is, as <see cref="ThingSet.About"/> says it.</param>
internal sealed record ThingOutcome(
    bool AlreadyLoaded,
    int Written,
    int Revised,
    int Missing,
    int Words,
    int Refused,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed,
    string About = ThingSet.MadeAndKeptAbout)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"{About} are already records, with the words that name them"
            : $"{Written} of {About} written and {Revised} revised; {Words} witness words " +
              $"are theirs by the file's rules and {Refused} were claimed twice and given to neither, in " +
              $"{Elapsed}" + (Missing > 0 ? $"; {Missing} slugs or relations the file names could not be written" : "") +
              (ByText.Count > 0 ? ". Per text: " + string.Join(", ", ByText.Select(t => $"{t.Text} {t.Words}")) : "");
}

/// <summary>
/// The things the text speaks of that are made rather than born — the ark, the menorah, the temple
/// — and the times it appoints — the Sabbath, Passover, the Day of Atonement — as records of their
/// own, with the words that name them.
///
/// <para>
/// **One word is not one thing.** אָרוֹן is the ark of the covenant two hundred times, and Joseph's
/// coffin at Genesis 50:26 and Jehoiada's money chest at 2 Kings 12:10. Nothing in BHSA marks any of
/// them a name, so no resolution by number can reach them and none should: the question an
/// occurrence asks is whether it is this thing at all. So each record carries rules — a Strong
/// number, the spans of Scripture where it is this record, the verses where it is not, and where
/// needed a second word that has to stand beside it — and those rules were written verse by verse
/// from a reading of every occurrence. What that reading could not settle is not here; it is in the
/// review list beside the descriptors, for the owner, and stays unnamed until it is decided.
/// </para>
///
/// <para>
/// **A model read them, and they say so until a person has.** The verse lists were written by a
/// language model reading every occurrence, so what they establish is a
/// <see cref="LinkMethod.ModelReading"/> at <see cref="ByReading"/> — the record's claim, its
/// relations, its names in other languages and every word its rules reach. That standing is below a
/// resolution by number, as every model reading is: the reading can name a word nothing named and
/// cannot take a name away from a number only one record bears. A name the thing bears itself is
/// another matter — the bronze pillar Boaz is H1162 as Ruth's husband is, so that number no longer
/// settles 1 Kings 7:21 on its own (<see cref="NumbersElsewhere"/>). A rule or a record the owner has
/// reviewed says so in the file (<see cref="OccurrenceRule.Reviewed"/>, <see cref="ThingRecord.Reviewed"/>)
/// and is written as <see cref="LinkMethod.Manual"/> under <see cref="ReviewedSource"/>, which is the
/// standing that outranks the number. The rules seed the Hebrew witness and the Greek witnesses, and
/// the translations reach the record through the links, by the one carrying rule every annotation
/// follows.
/// </para>
///
/// <para>
/// **The file is the record.** A record already held is brought to what the file now says — its
/// fields, names, passages, times and relations — and the annotations are written again only when
/// the words the rules settle have changed, so a boot after the first writes nothing.
/// </para>
///
/// <para>
/// **Each list is loaded on its own** (<see cref="ThingSet"/>): the objects and the appointed times
/// are one, and the beings, things and places the narratives turn on another, decided on another
/// day. A list writes and takes back only what went under its own sources, and a list may also
/// name words for a record another list or a dataset holds — <em>the woman</em> of Genesis 3 for
/// Eve — without writing that record.
/// </para>
/// </summary>
internal sealed class ThingLoader(AppDbContext db, ILogger<ThingLoader> logger)
{
    /// <summary>What wrote the objects and the appointed times and the words no person has reviewed yet.</summary>
    public const string Source = ThingSet.MadeAndKeptSource;

    /// <summary>What wrote a rule or a record the owner has reviewed, which is then his ruling.</summary>
    public const string ReviewedSource = ThingSet.MadeAndKeptReviewedSource;

    /// <summary>
    /// How sure a reading here is, which nobody has measured for these records. It is the rate the
    /// corpus measured for a model's reading of a verse at the band it was not most sure of — 186
    /// readings re-read, 13 wrong — and not the higher rate of its confident band, because no second
    /// pass has re-read these and a number that claimed one had would overstate them.
    /// </summary>
    internal const double ByReading = 0.93;

    private static LinkMethod MethodOf(bool reviewed) => reviewed ? LinkMethod.Manual : LinkMethod.ModelReading;

    private static double? ConfidenceOf(bool reviewed) => reviewed ? null : ByReading;

    private const string SourceIdPrefix = "essenthos:thing:";

    /// <summary>What a name row on one of these records is, beside a proper name and a title.</summary>
    internal const string NameKind = "name";

    /// <summary>
    /// How near a second word has to stand for <see cref="OccurrenceRule.With"/>, in words of the
    /// witness. BHSA writes the article and the prepositions as words of their own, so <em>the altar
    /// of the incense</em> is three words and the two that matter are two apart.
    /// </summary>
    internal const int Reach = 3;

    /// <summary>
    /// Every occurrence of one number in the witnesses, where it stands and which of its number it is
    /// in its verse, kept where the second word stands close enough when one is asked for.
    /// </summary>
    private const string Occurrences =
        """
        SELECT o.id, o.book, o.chapter, o.verse, o.nth
        FROM (
            SELECT w.id, w.verse_id, w.position, r.canonical_book AS book, r.canonical_chapter AS chapter,
                   r.canonical_verse AS verse,
                   row_number() OVER (PARTITION BY w.verse_id ORDER BY w.position)::int AS nth
            FROM word w
            JOIN text t ON t.id = w.text_id AND t.slug = ANY(@witnesses)
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE w.strong_number = @strong) o
        WHERE @with = ''
           OR EXISTS (SELECT 1 FROM word beside
                      WHERE beside.verse_id = o.verse_id AND beside.strong_number = @with
                        AND abs(beside.position - o.position) <= @reach)
        """;

    /// <summary>
    /// What says an appointed time stands in each verse of the passages that command it. Not that a
    /// word there names it — Leviticus 16 never says <em>the day of atonement</em> and is the whole of
    /// how it is kept — so these are references from the passage, read as such by the model that
    /// listed the passages, and they say so beside the ones the words give.
    /// </summary>
    public const string PassageSource = ThingSet.MadeAndKeptPassageSource;

    /// <summary>
    /// Every verse of every commanding passage, once per record, from the verses the canonical frame
    /// holds; and the rows of that source no passage covers any longer, taken back.
    /// </summary>
    private const string CommandedVerses =
        """
        WITH wanted AS (
            SELECT DISTINCT p.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
            FROM entity_passage p
            JOIN verse_reference r
              ON r.is_primary
             AND r.canonical_book = p.canonical_book
             AND r.canonical_chapter BETWEEN p.canonical_chapter AND p.end_chapter
             AND (r.canonical_chapter > p.canonical_chapter OR p.canonical_verse IS NULL
                  OR r.canonical_verse >= p.canonical_verse)
             AND (r.canonical_chapter < p.end_chapter OR p.end_verse IS NULL
                  OR r.canonical_verse <= p.end_verse)
            WHERE p.role = @command AND p.source = @passages),
        withdrawn AS (
            DELETE FROM entity_verse v
            WHERE v.source = @source
              AND NOT EXISTS (SELECT 1 FROM wanted w
                              WHERE w.entity_id = v.entity_id AND w.canonical_book = v.canonical_book
                                AND w.canonical_chapter = v.canonical_chapter
                                AND w.canonical_verse = v.canonical_verse)
            RETURNING 1),
        written AS (
            INSERT INTO entity_verse (entity_id, canonical_book, canonical_chapter, canonical_verse,
                                      label, disputed, source)
            SELECT w.entity_id, w.canonical_book, w.canonical_chapter, w.canonical_verse, NULL, FALSE, @source
            FROM wanted w
            WHERE NOT EXISTS (SELECT 1 FROM entity_verse v
                              WHERE v.source = @source AND v.entity_id = w.entity_id
                                AND v.canonical_book = w.canonical_book
                                AND v.canonical_chapter = w.canonical_chapter
                                AND v.canonical_verse = w.canonical_verse)
            RETURNING 1)
        SELECT (SELECT count(*) FROM withdrawn) + (SELECT count(*) FROM written)
        """;

    /// <summary>The words these rules seeded last time, which is what decides whether to write again.</summary>
    private const string Seeded =
        """
        SELECT word_id, entity_id, source = @reviewed FROM word_entity
        WHERE source IN (@source, @reviewed) AND coalesce(note, '') NOT LIKE @carried
        """;

    /// <summary>The list being loaded; the objects and the appointed times unless a load says otherwise.</summary>
    private ThingSet _set = ThingSet.MadeAndKept;

    public Task<ThingOutcome> Load(CancellationToken cancellationToken = default) =>
        Load(ThingSet.MadeAndKept, cancellationToken);

    public Task<ThingOutcome> Load(ThingSet set, CancellationToken cancellationToken = default)
    {
        var file = ThingFiles.Read(set);
        return Load(set, file.Records, file.Refers ?? [], cancellationToken);
    }

    internal Task<ThingOutcome> Load(IReadOnlyList<ThingRecord> records, CancellationToken cancellationToken) =>
        Load(ThingSet.MadeAndKept, records, [], cancellationToken);

    internal async Task<ThingOutcome> Load(
        ThingSet set,
        IReadOnlyList<ThingRecord> records,
        IReadOnlyList<ThingReference> refers,
        CancellationToken cancellationToken)
    {
        _set = set;
        var started = Stopwatch.StartNew();
        var (written, revised, missing, held) = await Write(records, cancellationToken);

        var (seed, refused, unheld) = await Settled(records, held, refers, cancellationToken);
        var byText = await Annotate(seed, cancellationToken);
        var referenced = await ReferThePassagesThatCommandThem(cancellationToken);

        var outcome = new ThingOutcome(
            written == 0 && revised == 0 && missing + unheld == 0 && byText is null && referenced == 0,
            written, revised, missing + unheld, seed.Count, refused, byText ?? [], started.Elapsed, set.About);
        logger.LogInformation("{About}: {Outcome}", set.About, outcome);
        return outcome;
    }

    /// <summary>The records, brought to what the file says; and each one's id by slug.</summary>
    private async Task<(int Written, int Revised, int Missing, IReadOnlyDictionary<string, int> Held)> Write(
        IReadOnlyList<ThingRecord> records,
        CancellationToken cancellationToken)
    {
        var slugs = records.Select(r => r.Slug)
            .Concat(records.SelectMany(r => r.Related ?? []).Select(r => r.To))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var known = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Include(e => e.Names)
            .Include(e => e.Passages)
            .Include(e => e.Times)
            .Include(e => e.Claims)
            .AsSplitQuery()
            .ToDictionaryAsync(e => e.Slug, StringComparer.Ordinal, cancellationToken);

        var elsewhere = await NumbersElsewhere(cancellationToken);

        int written = 0, revised = 0, missing = 0;
        var ours = new List<(ThingRecord Record, Entity Entity)>();
        foreach (var record in records)
        {
            if (known.TryGetValue(record.Slug, out var entity))
            {
                if (entity.SourceId != SourceIdPrefix + record.Slug)
                {
                    logger.LogWarning(
                        "The slug {Slug} is held by another record ({SourceId}), so the {Kind} of that slug " +
                        "was not written. Give it another slug in the record file",
                        record.Slug, entity.SourceId, record.Kind);
                    missing++;
                    continue;
                }

                if (Revise(entity, record, elsewhere))
                {
                    revised++;
                }
            }
            else
            {
                entity = new Entity
                {
                    Kind = record.EntityKind,
                    Slug = record.Slug,
                    Name = record.Name,
                    SourceId = SourceIdPrefix + record.Slug,
                    Source = _set.Source,
                };
                Revise(entity, record, elsewhere);
                db.Entities.Add(entity);
                known[record.Slug] = entity;
                written++;
            }

            ours.Add((record, entity));
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var (record, entity) in ours)
        {
            missing += await Relate(record, entity, known, cancellationToken);
            await Name(record, entity, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        return (written, revised, missing, ours.ToDictionary(o => o.Record.Slug, o => o.Entity.Id, StringComparer.Ordinal));
    }

    /// <summary>
    /// The Strong numbers a record outside these files is named by, which one of these records'
    /// name rows may not carry. Satan's title is <em>the serpent</em>, H5175, and a thing named by
    /// the common noun as well would stand beside him wherever the word does.
    ///
    /// <para>
    /// A number the lexicon heads as a proper name is the exception, because the lexicon itself says
    /// who bears it: H1162 is <em>Boaz, the ancestor of David; also the name of a pillar in front of
    /// the temple</em>. The pillar carries it, so the number is borne by a man and a thing, and the
    /// resolution, which asks it of the kind BHSA marks, still answers the man in Ruth — as a choice
    /// the marking made between two bearers, which stands below a reading of the verse. So where the
    /// pillar's rule reads 1 Kings 7:21, the word is the pillar.
    /// </para>
    /// </summary>
    private async Task<IReadOnlySet<string>> NumbersElsewhere(CancellationToken cancellationToken)
    {
        var named = await db.EntityNames
            .Where(n => !n.Entity!.SourceId.StartsWith(SourceIdPrefix)
                        && (n.HebrewStrongNumber != null || n.GreekStrongNumber != null))
            .Select(n => new { n.HebrewStrongNumber, n.GreekStrongNumber })
            .ToListAsync(cancellationToken);
        var numbers = named
            .SelectMany(n => $"{n.HebrewStrongNumber},{n.GreekStrongNumber}".Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(number => number.Trim())
            .ToHashSet(StringComparer.Ordinal);
        var proper = await db.StrongEntries
            .Where(s => numbers.Contains(s.StrongNumber) && s.Morphology != null && s.Morphology.StartsWith(ProperNoun))
            .Select(s => s.StrongNumber)
            .ToListAsync(cancellationToken);
        numbers.ExceptWith(proper);
        return numbers;
    }

    /// <summary>How the lexicon's morphology opens for a proper noun: <c>n-pr</c>, <c>n-pr-m</c>, <c>n-pr-loc</c>.</summary>
    private const string ProperNoun = "n-pr";

    private static string? OnlyOurs(string? number, IReadOnlySet<string> elsewhere) =>
        number is not null && elsewhere.Contains(number) ? null : number;

    /// <summary>Brings a record to the file's entry, and says whether anything had to change.</summary>
    private bool Revise(Entity entity, ThingRecord record, IReadOnlySet<string> elsewhere)
    {
        var changed = false;
        void Set<T>(T current, T wanted, Action<T> assign)
        {
            if (!EqualityComparer<T>.Default.Equals(current, wanted))
            {
                assign(wanted);
                changed = true;
            }
        }

        Set(entity.Kind, record.EntityKind, v => entity.Kind = v);
        Set(entity.Subtype, record.Subtype, v => entity.Subtype = v);
        Set(entity.Name, record.Name, v => entity.Name = v);
        Set(entity.Distinguisher, record.Distinguisher, v => entity.Distinguisher = v);
        Set(entity.Notes, record.Notes, v => entity.Notes = v);

        var names = (record.Called ?? []).Select(w => new EntityName
        {
            Label = w.Label,
            Hebrew = w.Hebrew,
            HebrewTransliterated = w.HebrewTransliterated,
            HebrewStrongNumber = OnlyOurs(w.HebrewStrongNumber, elsewhere),
            Greek = w.Greek,
            GreekTransliterated = w.GreekTransliterated,
            GreekStrongNumber = OnlyOurs(w.GreekStrongNumber, elsewhere),
            Meaning = w.Meaning,
            Kind = NameKind,
        }).ToList();
        if (!entity.Names.Select(NameKey).SequenceEqual(names.Select(NameKey)))
        {
            entity.Names.Clear();
            names.ForEach(entity.Names.Add);
            changed = true;
        }

        var passages = (record.Passages ?? []).Select((p, at) => Passage(p, at)).ToList();
        if (!entity.Passages.OrderBy(p => p.Ordinal).Select(PassageKey).SequenceEqual(passages.Select(PassageKey)))
        {
            entity.Passages.Clear();
            passages.ForEach(entity.Passages.Add);
            changed = true;
        }

        var times = (record.Times ?? []).Select(Time).ToList();
        if (!entity.Times.OrderBy(t => t.Id).Select(TimeKey).SequenceEqual(times.Select(TimeKey)))
        {
            entity.Times.Clear();
            times.ForEach(entity.Times.Add);
            changed = true;
        }

        var reviewed = record.Reviewed is not null;
        if (!entity.Claims.Any(c => c.Source == _set.SourceOf(reviewed) && c.Method == MethodOf(reviewed)
                                    && c.Note == record.Why))
        {
            entity.Claims.Clear();
            entity.Claims.Add(new EntityClaim
            {
                Method = MethodOf(reviewed),
                Confidence = ConfidenceOf(reviewed),
                Source = _set.SourceOf(reviewed),
                Note = record.Why,
            });
            changed = true;
        }

        return changed;
    }

    private static string NameKey(EntityName n) =>
        string.Join('|', n.Label, n.Hebrew, n.HebrewTransliterated, n.HebrewStrongNumber, n.Greek,
            n.GreekTransliterated, n.GreekStrongNumber, n.Meaning);

    private static string PassageKey(EntityPassage p) =>
        $"{p.Role}|{p.CanonicalBook}|{p.CanonicalChapter}|{p.CanonicalVerse}|{p.EndChapter}|{p.EndVerse}|{p.Note}";

    private static string TimeKey(ObservanceTime t) =>
        $"{t.Cycle}|{t.Month}|{t.Day}|{t.LastDay}|{t.CanonicalBook}|{t.CanonicalChapter}|{t.CanonicalVerse}|{t.Note}";

    private EntityPassage Passage(ThingPassage passage, int ordinal)
    {
        if (!PassageRoles.All.Contains(passage.Role))
        {
            throw new InvalidDataException(
                $"The passage {passage.Reference} is listed as \"{passage.Role}\", which is not a role a passage " +
                $"has. Use one of {string.Join(", ", PassageRoles.All)}.");
        }

        var span = ScriptureSpan.Parse(passage.Reference);
        return new EntityPassage
        {
            Role = passage.Role,
            Ordinal = ordinal,
            CanonicalBook = span.Book,
            CanonicalChapter = span.FromChapter,
            CanonicalVerse = span.FromVerse,
            EndChapter = span.ToChapter,
            EndVerse = span.ToVerse,
            Note = passage.Note,
            Source = _set.Source,
        };
    }

    private ObservanceTime Time(ThingTime time)
    {
        var span = ScriptureSpan.Parse(time.Reference);
        if (!span.IsVerse || !ObservanceCycles.All.Contains(time.Cycle))
        {
            throw new InvalidDataException(
                $"The time stated at {time.Reference} has to cite one verse and a cycle of " +
                $"{string.Join(", ", ObservanceCycles.All)}; it has \"{time.Cycle}\".");
        }

        return new ObservanceTime
        {
            Cycle = time.Cycle,
            Month = time.Month,
            Day = time.Day,
            LastDay = time.LastDay,
            CanonicalBook = span.Book,
            CanonicalChapter = span.FromChapter,
            CanonicalVerse = span.FromVerse!.Value,
            Note = time.Note,
            Source = _set.Source,
        };
    }

    /// <summary>
    /// The record's relations, each resting on the verse the file cites. Returns how many name a
    /// record the encyclopedia does not hold.
    /// </summary>
    private async Task<int> Relate(
        ThingRecord record,
        Entity entity,
        IReadOnlyDictionary<string, Entity> known,
        CancellationToken cancellationToken)
    {
        var wanted = new List<EntityRelationship>();
        var missing = 0;
        foreach (var relation in record.Related ?? [])
        {
            if (!ThingRelations.All.Contains(relation.Type))
            {
                throw new InvalidDataException(
                    $"{record.Slug} is related to {relation.To} as \"{relation.Type}\", which is not a relation " +
                    $"these records stand in. Use one of {string.Join(", ", ThingRelations.All)}.");
            }

            var verse = ScriptureSpan.Parse(relation.Reference);
            if (!known.TryGetValue(relation.To, out var other) || !verse.IsVerse)
            {
                logger.LogWarning(
                    "{Slug} is related to {To} at {Reference}, and either the encyclopedia holds no record of " +
                    "that slug or the reference is not a verse. The record file has to follow the record",
                    record.Slug, relation.To, relation.Reference);
                missing++;
                continue;
            }

            wanted.Add(new EntityRelationship
            {
                FromEntityId = entity.Id,
                ToEntityId = other.Id,
                Type = relation.Type,
                Category = RelationshipCategories.Read,
                CanonicalBook = verse.Book,
                CanonicalChapter = verse.FromChapter,
                CanonicalVerse = verse.FromVerse,
                Method = MethodOf(record.Reviewed is not null),
                Confidence = ConfidenceOf(record.Reviewed is not null),
                Source = _set.SourceOf(record.Reviewed is not null),
                Notes = relation.Note,
            });
        }

        var held = await db.EntityRelationships
            .Where(r => r.FromEntityId == entity.Id && (r.Source == _set.Source || r.Source == _set.ReviewedSource))
            .ToListAsync(cancellationToken);
        if (!held.Select(RelationKey).Order().SequenceEqual(wanted.Select(RelationKey).Order()))
        {
            db.EntityRelationships.RemoveRange(held);
            db.EntityRelationships.AddRange(wanted);
        }

        return missing;
    }

    private static string RelationKey(EntityRelationship r) =>
        $"{r.ToEntityId}|{r.Type}|{r.Method}|{r.CanonicalBook}|{r.CanonicalChapter}|{r.CanonicalVerse}|{r.Notes}";

    /// <summary>The name in each reader's language, and in the cases a phrase needs where the file gives them.</summary>
    private async Task Name(ThingRecord record, Entity entity, CancellationToken cancellationToken)
    {
        var reviewed = record.Reviewed is not null;
        var wanted = new Dictionary<(string Language, string Case), string>();
        foreach (var (language, name) in record.Names ?? new Dictionary<string, string>())
        {
            wanted[(language, GrammaticalCases.Nominative)] = name;
        }

        foreach (var (language, forms) in record.Forms ?? new Dictionary<string, IReadOnlyDictionary<string, string>>())
        {
            foreach (var (grammaticalCase, form) in forms)
            {
                if (!GrammaticalCases.All.Contains(grammaticalCase))
                {
                    throw new InvalidDataException(
                        $"{record.Slug} gives a {language} form in the \"{grammaticalCase}\" case, which is not one " +
                        $"a phrase asks for. Use one of {string.Join(", ", GrammaticalCases.All)}.");
                }

                wanted[(language, grammaticalCase)] = form;
            }
        }

        var held = await db.EntityNameForms
            .Where(f => f.EntityId == entity.Id)
            .ToListAsync(cancellationToken);
        foreach (var form in held.Where(f => !wanted.ContainsKey((f.Language, f.GrammaticalCase))))
        {
            db.EntityNameForms.Remove(form);
        }

        foreach (var ((language, grammaticalCase), form) in wanted)
        {
            var existing = held.FirstOrDefault(f => f.Language == language && f.GrammaticalCase == grammaticalCase);
            if (existing is null)
            {
                db.EntityNameForms.Add(new EntityNameForm
                {
                    EntityId = entity.Id,
                    Language = language,
                    GrammaticalCase = grammaticalCase,
                    Form = form,
                    Method = MethodOf(reviewed),
                    Confidence = ConfidenceOf(reviewed),
                    Source = _set.SourceOf(reviewed),
                });
            }
            else if (existing.Form != form || existing.Source != _set.SourceOf(reviewed))
            {
                existing.Form = form;
                existing.Method = MethodOf(reviewed);
                existing.Confidence = ConfidenceOf(reviewed);
                existing.Source = _set.SourceOf(reviewed);
            }
        }
    }

    /// <summary>
    /// Which witness words the rules give to which record. A word two records' rules both claim is
    /// given to neither and counted, because the file then says two things about it and the reader
    /// would be shown whichever was read first. The words the list names for records it does not
    /// write are settled the same way, on those records; one the encyclopedia does not hold is counted.
    /// </summary>
    private async Task<(IReadOnlyDictionary<long, Seeding> Seed, int Refused, int Unheld)> Settled(
        IReadOnlyList<ThingRecord> records,
        IReadOnlyDictionary<string, int> held,
        IReadOnlyList<ThingReference> refers,
        CancellationToken cancellationToken)
    {
        var elsewhere = refers.Select(r => r.Slug).Distinct(StringComparer.Ordinal).ToList();
        var others = await db.Entities
            .Where(e => elsewhere.Contains(e.Slug))
            .Select(e => new { e.Slug, e.Id, e.Name })
            .ToDictionaryAsync(e => e.Slug, StringComparer.Ordinal, cancellationToken);

        var named = records
            .Where(r => held.ContainsKey(r.Slug))
            .Select(r => (Entity: held[r.Slug], r.Name, Rules: r.Occurrences ?? []))
            .ToList();
        var unheld = 0;
        foreach (var reference in refers)
        {
            if (!others.TryGetValue(reference.Slug, out var other))
            {
                logger.LogWarning(
                    "{About} name words for the record {Slug}, which the encyclopedia does not hold, so they were " +
                    "not written. The file has to follow the record's slug",
                    _set.About, reference.Slug);
                unheld++;
                continue;
            }

            named.Add((other.Id, other.Name, reference.Occurrences));
        }

        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var claimed = new Dictionary<long, Seeding>();
        var disputed = new HashSet<long>();
        foreach (var (entityId, name, rules) in named)
        {
            foreach (var rule in rules)
            {
                foreach (var (wordId, reference) in await Words(connection, rule, cancellationToken))
                {
                    var note = $"{rule.Strong} at {reference}, which the record file reads as {name}";
                    if (claimed.TryGetValue(wordId, out var first) && first.Entity != entityId)
                    {
                        disputed.Add(wordId);
                        logger.LogWarning(
                            "Two records' rules claim the word {Number} at {Reference}; it is given to neither",
                            rule.Strong, reference);
                        continue;
                    }

                    claimed[wordId] = new Seeding(entityId, note, rule.Reviewed is not null);
                }
            }
        }

        foreach (var word in disputed)
        {
            claimed.Remove(word);
        }

        return (claimed, disputed.Count, unheld);
    }

    private static async Task<List<(long WordId, string Reference)>> Words(
        NpgsqlConnection connection,
        OccurrenceRule rule,
        CancellationToken cancellationToken)
    {
        var witnesses = rule.Strong.StartsWith('H') ? [EntityCandidates.Witness] : EntityCandidates.GreekWitnesses;
        await using var command = new NpgsqlCommand(Occurrences, connection);
        command.Parameters.AddWithValue("witnesses", witnesses);
        command.Parameters.AddWithValue("strong", rule.Strong);
        command.Parameters.AddWithValue("with", rule.With ?? string.Empty);
        command.Parameters.AddWithValue("reach", Reach);

        var words = new List<(long, string)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var (book, chapter, verse, nth) = (reader.GetInt32(1), reader.GetInt32(2), reader.GetInt32(3), reader.GetInt32(4));
            if (rule.Admits(book, chapter, verse, nth))
            {
                words.Add((reader.GetInt64(0), $"{BookReferences.Name(book)} {chapter}:{verse}"));
            }
        }

        return words;
    }

    /// <summary>The references the commanding passages give, brought to the passages; how many changed.</summary>
    private async Task<int> ReferThePassagesThatCommandThem(CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        await using var command = new NpgsqlCommand(CommandedVerses, connection);
        command.Parameters.AddWithValue("command", PassageRoles.Command);
        command.Parameters.AddWithValue("passages", _set.Source);
        command.Parameters.AddWithValue("source", _set.PassageSource);
        return (int)(long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    /// <summary>One word the rules give a record, and whether a person has reviewed the rule that gave it.</summary>
    private sealed record Seeding(int Entity, string Note, bool Reviewed);

    /// <summary>
    /// The seed written as annotations and carried along the links, unless it is exactly what was
    /// seeded last time. The readings and the reviewed rules are written as two batches under their
    /// own methods and sources, because a carried row takes its method from the batch it was carried
    /// in. Null where nothing had to be written.
    /// </summary>
    private async Task<IReadOnlyList<(string Text, int Words)>?> Annotate(
        IReadOnlyDictionary<long, Seeding> seed,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var before = new HashSet<(long, int, bool)>();
        await using (var command = new NpgsqlCommand(Seeded, connection))
        {
            command.Parameters.AddWithValue("source", _set.Source);
            command.Parameters.AddWithValue("reviewed", _set.ReviewedSource);
            command.Parameters.AddWithValue("carried", Annotating.CarriedNote);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                before.Add((reader.GetInt64(0), reader.GetInt32(1), reader.GetBoolean(2)));
            }
        }

        if (before.SetEquals(seed.Select(s => (s.Key, s.Value.Entity, s.Value.Reviewed))))
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, "DELETE FROM word_entity WHERE source IN (@source, @reviewed)",
            cancellationToken, ("source", _set.Source), ("reviewed", _set.ReviewedSource));

        var byText = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var reviewed in new[] { false, true })
        {
            var batch = seed.Where(s => s.Value.Reviewed == reviewed).ToList();
            if (batch.Count == 0)
            {
                continue;
            }

            await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
            await Annotating.Seed(
                connection,
                batch.Select(s => (s.Key, s.Value.Entity, ConfidenceOf(reviewed), false, s.Value.Note)),
                cancellationToken);
            await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

            var method = EnumSpelling.Of(MethodOf(reviewed));
            await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
                ("method", method), ("source", _set.SourceOf(reviewed)));
            await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
                ("method", method), ("source", _set.SourceOf(reviewed)));
            await Annotating.Run(connection, transaction, "DROP TABLE pending_annotation", cancellationToken);

            foreach (var (text, words) in await Annotating.ByText(connection, transaction, _set.SourceOf(reviewed), cancellationToken))
            {
                byText[text] = byText.GetValueOrDefault(text) + words;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return [.. byText.OrderByDescending(t => t.Value).Select(t => (t.Key, t.Value))];
    }
}
