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
internal sealed record ThingOutcome(
    bool AlreadyLoaded,
    int Written,
    int Revised,
    int Missing,
    int Words,
    int Refused,
    IReadOnlyList<(string Text, int Words)> ByText,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the objects and the appointed times are already records, with the words that name them"
            : $"{Written} objects and appointed times written and {Revised} revised; {Words} witness words " +
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
/// **The rules speak with a person's standing, and say so.** Each word is decided the way a ruling
/// decides a person's name, so the annotation is <see cref="LinkMethod.Manual"/> and outranks what a
/// number resolved: the bronze pillar Boaz of 1 Kings 7:21 had been read onto Ruth's husband, and
/// the pillar's record now answers for that word while the resolution's row stays as the record of
/// what it said. The rules seed the Hebrew witness and the Greek witnesses, and the translations
/// reach the record through the links, by the one carrying rule every annotation follows.
/// </para>
///
/// <para>
/// **The file is the record.** A record already held is brought to what the file now says — its
/// fields, names, passages, times and relations — and the annotations are written again only when
/// the words the rules settle have changed, so a boot after the first writes nothing.
/// </para>
/// </summary>
internal sealed class ThingLoader(AppDbContext db, ILogger<ThingLoader> logger)
{
    public const string Source =
        "Essenthos, on the project owner's decision of 2026-09-23 that objects and appointed times are " +
        "records of their own, each occurrence decided by its sense from the verse";

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

    /// <summary>The words these rules seeded last time, which is what decides whether to write again.</summary>
    private const string Seeded =
        """
        SELECT word_id, entity_id FROM word_entity
        WHERE source = @source AND coalesce(note, '') NOT LIKE @carried
        """;

    public async Task<ThingOutcome> Load(CancellationToken cancellationToken = default) =>
        await Load(ThingFiles.Read(), cancellationToken);

    internal async Task<ThingOutcome> Load(IReadOnlyList<ThingRecord> records, CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var (written, revised, missing, held) = await Write(records, cancellationToken);

        var (seed, refused) = await Settled(records, held, cancellationToken);
        var byText = await Annotate(seed, cancellationToken);

        var outcome = new ThingOutcome(
            written == 0 && revised == 0 && missing == 0 && byText is null,
            written, revised, missing, seed.Count, refused, byText ?? [], started.Elapsed);
        logger.LogInformation("The objects and the appointed times: {Outcome}", outcome);
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
                    Source = Source,
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
    /// The Strong numbers a record outside these files is named by. A word's number goes on one of
    /// these records' name rows only where nobody else is named by it: the pillar Boaz is H1162, and
    /// so is Ruth's husband, and a second record under that number would make every word of it in
    /// Ruth a name several records bear, which no resolution may then settle. The pillar keeps its
    /// Hebrew and its words keep their rulings; only the number is left off its name.
    /// </summary>
    private async Task<IReadOnlySet<string>> NumbersElsewhere(CancellationToken cancellationToken)
    {
        var named = await db.EntityNames
            .Where(n => !n.Entity!.SourceId.StartsWith(SourceIdPrefix)
                        && (n.HebrewStrongNumber != null || n.GreekStrongNumber != null))
            .Select(n => new { n.HebrewStrongNumber, n.GreekStrongNumber })
            .ToListAsync(cancellationToken);
        return named
            .SelectMany(n => $"{n.HebrewStrongNumber},{n.GreekStrongNumber}".Split(',', StringSplitOptions.RemoveEmptyEntries))
            .Select(number => number.Trim())
            .ToHashSet(StringComparer.Ordinal);
    }

    private static string? OnlyOurs(string? number, IReadOnlySet<string> elsewhere) =>
        number is not null && elsewhere.Contains(number) ? null : number;

    /// <summary>Brings a record to the file's entry, and says whether anything had to change.</summary>
    private static bool Revise(Entity entity, ThingRecord record, IReadOnlySet<string> elsewhere)
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

        var names = (record.Words ?? []).Select(w => new EntityName
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

        if (!entity.Claims.Any(c => c.Source == Source && c.Note == record.Why))
        {
            entity.Claims.Clear();
            entity.Claims.Add(new EntityClaim
            {
                Method = LinkMethod.Manual,
                Confidence = null,
                Source = Source,
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

    private static EntityPassage Passage(ThingPassage passage, int ordinal)
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
            Source = Source,
        };
    }

    private static ObservanceTime Time(ThingTime time)
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
            Source = Source,
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
                Method = LinkMethod.Manual,
                Confidence = null,
                Source = Source,
                Notes = relation.Note,
            });
        }

        var held = await db.EntityRelationships
            .Where(r => r.FromEntityId == entity.Id && r.Source == Source)
            .ToListAsync(cancellationToken);
        if (!held.Select(RelationKey).Order().SequenceEqual(wanted.Select(RelationKey).Order()))
        {
            db.EntityRelationships.RemoveRange(held);
            db.EntityRelationships.AddRange(wanted);
        }

        return missing;
    }

    private static string RelationKey(EntityRelationship r) =>
        $"{r.ToEntityId}|{r.Type}|{r.CanonicalBook}|{r.CanonicalChapter}|{r.CanonicalVerse}|{r.Notes}";

    /// <summary>The name in each reader's language, and in the cases a phrase needs where the file gives them.</summary>
    private async Task Name(ThingRecord record, Entity entity, CancellationToken cancellationToken)
    {
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
                    Method = LinkMethod.Manual,
                    Confidence = null,
                    Source = Source,
                });
            }
            else if (existing.Form != form || existing.Source != Source)
            {
                existing.Form = form;
                existing.Method = LinkMethod.Manual;
                existing.Confidence = null;
                existing.Source = Source;
            }
        }
    }

    /// <summary>
    /// Which witness words the rules give to which record. A word two records' rules both claim is
    /// given to neither and counted, because the file then says two things about it and the reader
    /// would be shown whichever was read first.
    /// </summary>
    private async Task<(IReadOnlyDictionary<long, (int Entity, string Note)> Seed, int Refused)> Settled(
        IReadOnlyList<ThingRecord> records,
        IReadOnlyDictionary<string, int> held,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var claimed = new Dictionary<long, (int Entity, string Note)>();
        var disputed = new HashSet<long>();
        foreach (var record in records)
        {
            if (!held.TryGetValue(record.Slug, out var entityId))
            {
                continue;
            }

            foreach (var rule in record.Occurrences ?? [])
            {
                foreach (var (wordId, reference) in await Words(connection, rule, cancellationToken))
                {
                    var note = $"{rule.Strong} at {reference}, which the record file reads as {record.Name}";
                    if (claimed.TryGetValue(wordId, out var first) && first.Entity != entityId)
                    {
                        disputed.Add(wordId);
                        logger.LogWarning(
                            "Two records' rules claim the word {Number} at {Reference}; it is given to neither",
                            rule.Strong, reference);
                        continue;
                    }

                    claimed[wordId] = (entityId, note);
                }
            }
        }

        foreach (var word in disputed)
        {
            claimed.Remove(word);
        }

        return (claimed, disputed.Count);
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
        command.CommandTimeout = Annotating.Patient;

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

    /// <summary>
    /// The seed written as annotations and carried along the links, unless it is exactly what was
    /// seeded last time. Null where nothing had to be written.
    /// </summary>
    private async Task<IReadOnlyList<(string Text, int Words)>?> Annotate(
        IReadOnlyDictionary<long, (int Entity, string Note)> seed,
        CancellationToken cancellationToken)
    {
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();
        var before = new HashSet<(long, int)>();
        await using (var command = new NpgsqlCommand(Seeded, connection))
        {
            command.Parameters.AddWithValue("source", Source);
            command.Parameters.AddWithValue("carried", Annotating.CarriedNote);
            command.CommandTimeout = Annotating.Patient;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                before.Add((reader.GetInt64(0), reader.GetInt32(1)));
            }
        }

        if (before.SetEquals(seed.Select(s => (s.Key, s.Value.Entity))))
        {
            return null;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, "DELETE FROM word_entity WHERE source = @source",
            cancellationToken, ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Seed(
            connection,
            seed.Select(s => (s.Key, s.Value.Entity, (double?)null, false, s.Value.Note)),
            cancellationToken);
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

        var method = EnumSpelling.Of(LinkMethod.Manual);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", method), ("source", Source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", method), ("source", Source));

        var byText = await Annotating.ByText(connection, transaction, Source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return byText;
    }
}
