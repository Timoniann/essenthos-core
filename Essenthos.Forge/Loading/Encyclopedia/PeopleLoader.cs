using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="FromStrong">
/// Records made from a gentilic entry — one per lexeme Strong derives, carrying his clause verbatim.
/// </param>
/// <param name="Tribes">Records written here because no lexicon names a tribe of Israel.</param>
/// <param name="WithOrigin">
/// Records whose ancestor or homeland is a page a reader can open. The rest still say whom they are
/// named after, in the words of whoever said it; only the link is withheld.
/// </param>
/// <param name="Ruled">
/// Occurrences a person or a review decided name a people, which is the eight the loader refused to
/// write when there was no kind for them.
/// </param>
/// <param name="Read">
/// Occurrences a model answered <c>unlisted</c> and described as a people, now that there is one to
/// point at.
/// </param>
/// <param name="Undecided">
/// Occurrences a model answered <c>unlisted</c> whose description names a people and a place at
/// once, or a place alone. Counted rather than resolved: <em>the kingdom/tribe of Judah</em> is a
/// question about the verse and not about the encyclopedia, and this pass does not read verses.
/// </param>
/// <param name="Analysed">
/// Records written for a lexeme BHSA analyses as a gentilic and Strong derives from no word he
/// numbers — the Egyptians, the Perizzites and the rest his derivation refuses.
/// </param>
/// <param name="Joined">
/// Numbers of that kind that named a people the encyclopedia already holds, and were written onto
/// it as a second gentilic rather than as a second record.
/// </param>
/// <param name="Unnamed">
/// Numbers of that kind refused because his King James renderings yield no gentilic word — an
/// adverb, a woman's designation or a man's name, none of which is a people.
/// </param>
internal sealed record PeopleOutcome(
    bool AlreadyLoaded,
    int FromStrong,
    int Tribes,
    int Analysed,
    int Joined,
    int Unnamed,
    int WithOrigin,
    int Gentilics,
    int Ruled,
    int Read,
    int Undecided,
    int Annotated,
    int Referenced,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the peoples are already there"
            : $"{FromStrong + Tribes + Analysed} peoples — {FromStrong} from the gentilics Strong " +
              $"derives, {Tribes} tribes of Israel no lexicon names and {Analysed} lexemes BHSA " +
              $"analyses as gentilics that he derives from nothing — of which {WithOrigin} name an " +
              $"ancestor or a homeland the encyclopedia holds, in {Elapsed}. {Joined} further " +
              $"numbers joined a people already held and {Unnamed} were refused for want of a name " +
              $"in his renderings. {Gentilics} gentilic rows now reach the people they were always " +
              $"about. {Annotated} words name a people: from {Ruled} occurrences a review decided " +
              $"and {Read} a model described as a people, plus every word carrying a gentilic. " +
              $"{Undecided} further readings name a people and a territory in one breath and are " +
              $"left alone. {Referenced} verse references written.";
}

/// <param name="Records">The records this run wrote, by slug.</param>
/// <param name="Joined">
/// Records the encyclopedia already held that gained a number this run, which have words to
/// annotate even though nothing was written for them.
/// </param>
internal sealed record PeopleWriting(
    Dictionary<string, Entity> Records,
    List<Entity> Joined,
    int Analysed,
    int Unnamed);

/// <summary>
/// The peoples: a nation, a tribe or a clan the text speaks of as one.
///
/// The encyclopedia held persons and places, and the corpus was carrying the gap in two places at
/// once. Every gentilic Strong derives had a lexeme where its near end should be — <em>the
/// Moabites</em> had nothing to be — and the largest class of dissent in an 8,092-occurrence audit
/// was a tribal name forced onto its eponym, so <em>the tribe of Judah could not drive out the
/// Jebusites</em> was annotated to Jacob's fourth son and the reader was offered a man four hundred
/// years dead.
///
/// <para>
/// <strong>What a record is, and what it is not.</strong> It is written where a source states that
/// a collective exists and says what it is named after. Strong states it a hundred and sixty times,
/// in his own sentence, and those records are his entries with a page attached; where the origin he
/// names is somebody the encyclopedia holds, the record links to them, and where it is not, the
/// claim still says whom. It is never written because BHSA marked a word <c>gens</c> and nothing
/// else said who they are: that marking is on 5,080 words and on 83 Strong numbers, several of
/// which are ordinary personal names, and a record whose only evidence is a marking is a record
/// with nothing on its page.
/// </para>
///
/// <para>
/// <strong>BHSA's analysis of the lexeme is a different statement, and it is read.</strong>
/// <c>lexicalSet = gntl</c> says what class of word this is — <em>a person of somewhere</em> — and
/// it stands on the derived word rather than on the name, so it never confuses the people with
/// their eponym. Strong's derivation refuses 78 of the 316 lexemes it marks, for saying
/// <em>from an unused name</em>, hedging, or naming no number at all, and those 78 include the
/// Egyptians, the Perizzites, the Kerethites and the Horites. They are written from the analysis
/// and named from his King James renderings, which is the one half of the naming rule that can
/// answer nothing, so an entry that is an adverb or a man's name is refused rather than made a
/// nation.
/// </para>
///
/// <para>
/// <strong>The tribes are the exception and they are named by hand.</strong> Hebrew names a tribe
/// with the ancestor's own word, so the collective and the man are one lexeme and one Strong number
/// and a dictionary of words cannot separate them. Fourteen records therefore rest on the corpus's
/// own witness — the eponym is already a page, and BHSA marks that name <c>gens</c> where it stands
/// for the people — and each says so in its own claim rather than borrowing Strong's authority.
/// </para>
///
/// <para>
/// <strong>What it will not do is absorb a name.</strong> Eight hundred words carry H3063 and BHSA
/// marks nearly all of them <c>pers,gens,topo</c> together, which decides nothing; annotating them
/// wholesale to the Judahites would be a worse encyclopedia than none. So a collective is annotated
/// only where something read that occurrence and said so — a review's ruling, or a model's own
/// sentence naming a people and no territory. The 483 readings that describe a kingdom or a
/// territory are left where they are, because a polity and a land are two more kinds this
/// encyclopedia does not have.
/// </para>
///
/// <para>
/// Idempotent per record. A record the file names and the corpus lacks is written, with its words,
/// its rulings and its verses, on whichever boot first finds it missing; everything already written
/// is left as it is.
/// </para>
/// </summary>
internal sealed class PeopleLoader(
    AppDbContext db,
    IConfiguration configuration,
    ILogger<PeopleLoader> logger)
{
    /// <summary>
    /// What a record made from a gentilic entry says about itself. The record is ours — Strong
    /// wrote a dictionary of words and not an encyclopedia of nations — and the claim on it is his.
    /// </summary>
    private const string FromTheDictionary =
        "Essenthos, from the gentilics Strong's Dictionary derives";

    private const string SourceIdPrefix = "essenthos:";

    /// <summary>
    /// How the words carrying a gentilic are annotated: the lexeme names the people, so the number
    /// resolves the way a proper noun's does.
    /// </summary>
    private const string ByTheGentilic =
        "the gentilic Strong's Dictionary derives, resolving to exactly one people";

    /// <summary>
    /// What established it, and the reason it is not the method a proper noun's resolution carries.
    ///
    /// <see cref="LinkMethod.StrongNumber"/> means that nothing had to be chosen — the number named
    /// one record and the occurrence resolved without anybody weighing anything. That is not what
    /// happens here. A gentilic shares its number with the person or the place it is derived from:
    /// H6430 is Goliath as well as the Philistines and H3778 is Chaldea as well as the Chaldeans,
    /// so the number alone leaves the answer open and something else closes it. What closes it is
    /// the word's own form — Strong states the derivation and BHSA marks the lexeme a gentilic —
    /// and <see cref="LinkMethod.Lexical"/> is the method that says exactly that, which the reader
    /// is shown as <em>by the form of the word</em>.
    ///
    /// <para>
    /// It carries a confidence either way, so the provenance constraints are indifferent between
    /// the two: only a source's testimony and a person's hand may leave that column null. What
    /// moves is the standing, from above a reading of the verse to below one, and that is the right
    /// way round — a gentilic tells you the kind of thing a word names and not which occurrence of
    /// it, and somebody who read the sentence knows more than the form does.
    /// </para>
    /// </summary>
    private const LinkMethod ByTheForm = LinkMethod.Lexical;

    /// <summary>
    /// The same room the ordinary name resolution leaves, and for the same reason: nothing in the
    /// data contradicts the annotation, and what is short of certainty is that this layer is a
    /// hundred and seventy-four peoples and the text names more than that.
    /// </summary>
    private const double GentilicResolution = 0.9;

    /// <summary>
    /// The measured survival of the model's own bands, which is where these readings' numbers come
    /// from. They are the readings' numbers and not this pass's: what this pass adds is a record to
    /// point at, not a better answer.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, double> Measured =
        new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["high"] = 0.99,
            ["medium"] = 0.93,
        };

    /// <summary>Whose reading of the verse a people annotation rests on, where a model read it.</summary>
    private const string FromAReading =
        "a reading of the verse naming a people the encyclopedia did not hold";

    /// <summary>Where a people's verse list comes from, which is this corpus and not a dataset.</summary>
    private const string FromOurOwnWords =
        "Essenthos, from the words this corpus annotates to the people";

    public async Task<PeopleOutcome> Load(
        string resources,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var file = PeopleFiles.Read();
        var writing = await Write(file, cancellationToken);
        var peoples = writing.Records;

        var descended = await ReferenceTheDescent(cancellationToken);
        if (descended > 0)
        {
            logger.LogInformation("{Descended} verses stating a people's descent were added to its list", descended);
        }

        if (peoples.Count == 0 && writing.Joined.Count == 0)
        {
            logger.LogInformation("The peoples are already there; nothing to do");
            return new PeopleOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        // A record that gained a number this run has words nothing has annotated either, so the
        // two belong in one array: what follows asks which peoples are new work, not which are new
        // rows.
        var written = peoples.Values.Concat(writing.Joined).Select(p => p.Id).ToArray();
        var tribes = file.Tribes.Count(t => peoples.ContainsKey(t.Slug));

        var gentilics = await JoinTheGentilics(cancellationToken);
        var annotated = await AnnotateTheGentilics(written, cancellationToken);

        var ruled = await Rulings(file, peoples, cancellationToken);
        annotated += ruled.Words;

        var read = await Readings(file, resources, peoples, cancellationToken);
        annotated += read.Words;

        var referenced = await Reference(written, cancellationToken);

        var outcome = new PeopleOutcome(
            false,
            peoples.Count - tribes - writing.Analysed,
            tribes,
            writing.Analysed,
            writing.Joined.Count,
            writing.Unnamed,
            peoples.Values.Count(p => p.OriginEntityId is not null),
            gentilics,
            ruled.Occurrences,
            read.Occurrences,
            read.Undecided,
            annotated,
            referenced,
            started.Elapsed);

        logger.LogInformation("Named: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// The records not yet written: the tribes first, so that a tribe claims its own gentilic entry
    /// rather than a second record being made from the same word, then the gentilics, then the
    /// nations the dictionary describes without deriving.
    ///
    /// <para>
    /// Asked per record, because the file grows and the corpus it grows on is already loaded: a pass
    /// that asked only whether any people existed would never write a record added to the file after
    /// the first boot. On a cold corpus every record is unwritten and this is the whole layer.
    /// </para>
    /// </summary>
    private async Task<PeopleWriting> Write(
        PeopleFile file,
        CancellationToken cancellationToken)
    {
        var slugs = await db.Entities
            .Select(e => new { e.Slug, e.Id, e.Kind })
            .ToListAsync(cancellationToken);
        var origins = slugs
            .Where(e => e.Kind != EntityKind.People)
            .ToDictionary(e => e.Slug, e => e.Id, StringComparer.Ordinal);
        var taken = slugs.Select(e => e.Slug).ToHashSet(StringComparer.Ordinal);

        var peoples = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var claimed = new Dictionary<string, Entity>(StringComparer.Ordinal);

        foreach (var tribe in file.Tribes.Where(t => !taken.Contains(t.Slug)))
        {
            int? origin = origins.TryGetValue(tribe.Origin, out var held) ? held : null;
            if (origin is null)
            {
                logger.LogWarning(
                    "The tribe \"{Slug}\" is named after \"{Origin}\", which the encyclopedia does not "
                    + "hold, so the record was written without the link. Either the record was renamed "
                    + "or the encyclopedia has not been loaded yet",
                    tribe.Slug,
                    tribe.Origin);
            }

            var entity = Record(
                tribe.Slug, tribe.Name, tribe.Distinguisher, tribe.Notes, origin, file.Source);

            entity.Claims.Add(new EntityClaim
            {
                Method = LinkMethod.Manual,
                Confidence = null,
                Source = file.Source,
                Note = tribe.Why,
            });

            entity.Names.Add(new EntityName
            {
                Label = tribe.Name,
                HebrewStrongNumber = tribe.CollectiveNumber,
                Kind = CollectiveName,
            });

            peoples[tribe.Slug] = entity;
            foreach (var number in tribe.GentilicNumbers ?? [])
            {
                claimed[number] = entity;
                entity.Names.Add(new EntityName
                {
                    Label = tribe.Name,
                    HebrewStrongNumber = number,
                    Kind = GentilicName,
                });
            }
        }

        var namings = file.Namings.ToDictionary(n => n.Number, StringComparer.Ordinal);
        var unjoined = await db.StrongGentilics
            .Where(g => g.PeopleEntityId == null)
            .OrderBy(g => g.StrongNumber)
            .ToListAsync(cancellationToken);

        var named = await db.EntityNames
            .Where(n => n.Kind == GentilicName && n.Entity!.Kind == EntityKind.People && n.HebrewStrongNumber != null)
            .Select(n => n.HebrewStrongNumber!)
            .ToListAsync(cancellationToken);
        var nations = (file.Nations ?? [])
            .Where(n => !named.Contains(n.Number, StringComparer.Ordinal) && !claimed.ContainsKey(n.Number))
            .ToList();

        var analysed = await Analysed(named, claimed, cancellationToken);
        if (peoples.Count == 0 && unjoined.Count == 0 && nations.Count == 0 && analysed.Count == 0)
        {
            return new PeopleWriting(peoples, [], 0, 0);
        }

        var wanted = unjoined.Select(g => g.StrongNumber)
            .Concat(nations.Select(n => n.Number))
            .Concat(analysed)
            .ToList();
        var entries = await db.StrongEntries
            .Where(e => wanted.Contains(e.StrongNumber))
            .Select(e => new LexiconEntry(e.StrongNumber, e.Definition, e.KjvDefinition))
            .ToDictionaryAsync(e => e.StrongNumber, StringComparer.Ordinal, cancellationToken);

        foreach (var gentilic in unjoined)
        {
            if (claimed.TryGetValue(gentilic.StrongNumber, out var already))
            {
                gentilic.People = already;
                continue;
            }

            var entry = entries.GetValueOrDefault(gentilic.StrongNumber);
            var correction = namings.GetValueOrDefault(gentilic.StrongNumber);
            var name = correction?.Name ?? GentilicNaming.Of(entry?.KjvDefinition, entry?.Definition);
            if (name is null)
            {
                logger.LogWarning(
                    "No name could be read for the people of {Number}: neither the King James "
                    + "renderings nor the definition yields a gentilic. Add it to the namings in "
                    + "Peoples.json with the reason, and the record will be written",
                    gentilic.StrongNumber);
                continue;
            }

            // Against every slug already in the encyclopedia and not only against this run's, since
            // the address has to be unique across the table and a clash there is a failed save at
            // the end of a long load rather than a warning where it happened.
            var entity = Record(
                PeopleFiles.Slug(
                    name,
                    gentilic.StrongNumber,
                    slug => peoples.ContainsKey(slug) || taken.Contains(slug)),
                name,
                Distinguisher(gentilic),
                correction is null ? null : $"Named here as {name}. {correction.Why}",
                gentilic.OriginEntityId,
                FromTheDictionary);

            entity.Claims.Add(new EntityClaim
            {
                Method = LinkMethod.StatedBySource,
                Confidence = null,
                Source = gentilic.Source,
                Note = $"{gentilic.StrongNumber}, {gentilic.Kind} of {gentilic.OriginNumber}: "
                       + $"\"{gentilic.Statement}\"",
            });

            entity.Names.Add(new EntityName
            {
                Label = name,
                HebrewStrongNumber = gentilic.StrongNumber,
                Kind = GentilicName,
            });

            peoples[entity.Slug] = entity;
            gentilic.People = entity;
        }

        foreach (var nation in nations)
        {
            if (entries.GetValueOrDefault(nation.Number)?.Definition is not { Length: > 0 } definition)
            {
                logger.LogWarning(
                    "The people \"{Name}\" rests on Strong's entry {Number}, which the lexicon does not "
                    + "hold or holds without a definition, so the record was not written. Load the "
                    + "lexicon first, or correct the number in Peoples.json",
                    nation.Name,
                    nation.Number);
                continue;
            }

            int? origin = nation.Origin is { } eponym && origins.TryGetValue(eponym, out var held) ? held : null;
            var entity = Record(
                PeopleFiles.Slug(nation.Name, nation.Number, slug => peoples.ContainsKey(slug) || taken.Contains(slug)),
                nation.Name,
                $"{definition}, as Strong's Dictionary describes them",
                null,
                origin,
                file.NationSource ?? file.Source);

            entity.Claims.Add(new EntityClaim
            {
                Method = LinkMethod.StatedBySource,
                Confidence = null,
                Source = StrongGentilicLoader.Source,
                Note = $"{nation.Number}: \"{definition}\"",
            });

            entity.Claims.Add(new EntityClaim
            {
                Method = LinkMethod.Manual,
                Confidence = null,
                Source = file.NationSource ?? file.Source,
                Note = nation.Why,
            });

            entity.Names.Add(new EntityName
            {
                Label = nation.Name,
                HebrewStrongNumber = nation.Number,
                Kind = GentilicName,
            });

            peoples[entity.Slug] = entity;
        }

        var (analysedRecords, joined, unnamed) = Analyse(analysed, entries, peoples, taken, file);

        await db.SaveChangesAsync(cancellationToken);
        return new PeopleWriting(peoples, joined, analysedRecords, unnamed);
    }

    /// <summary>What the lexicon says about one number, as much of it as the naming rule reads.</summary>
    private sealed record LexiconEntry(string StrongNumber, string? Definition, string? KjvDefinition);

    /// <summary>
    /// What a record made from BHSA's analysis says about itself. The record is ours; the two
    /// claims on it are the witness's analysis of the lexeme and the dictionary's account of what
    /// the people is.
    /// </summary>
    private const string Analysis =
        "Essenthos, from the lexemes BHSA analyses as gentilics, named as the King James renders them";

    /// <summary>
    /// The lexemes BHSA analyses as gentilics: <c>lexicalSet</c> is what class of word it is, and
    /// <c>gntl</c> is <em>a person of somewhere</em>.
    ///
    /// <para>
    /// It is not the <c>gens</c> marking this loader has always refused. That one is on a name that
    /// stands for the descent group as well as for the man — Moab, Ammon, Midian, Ham — so a record
    /// resting on it alone would be a page for a personal name. <c>gntl</c> is on the derived word
    /// and not on the name, and it is a statement of the same order as the proper-noun marking the
    /// name resolution already reads: this lexeme means a member of a people.
    /// </para>
    /// </summary>
    private const string Gentilics =
        """
        SELECT DISTINCT w.strong_number
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.slug = @witness
        WHERE w.strong_number IS NOT NULL AND w.morphology->>'lexicalSet' LIKE '%gntl%'
        ORDER BY 1
        """;

    /// <summary>
    /// Those of them no people bears and no tribe has claimed. 316 numbers carry the analysis and
    /// 78 arrive here: Strong states their origin in words the derivation parse refuses — an unused
    /// name, a hedge, a plain <em>from</em>, a comparison, or nothing — so the Egyptians, the
    /// Perizzites, the Kerethites and the Horites had no record at all.
    /// </summary>
    private async Task<List<string>> Analysed(
        List<string> named,
        Dictionary<string, Entity> claimed,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(Gentilics, connection);
        command.Parameters.AddWithValue("witness", EntityCandidates.Witness);
        command.CommandTimeout = Annotating.Patient;

        var held = named.ToHashSet(StringComparer.Ordinal);
        var numbers = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var number = reader.GetString(0);
            if (!held.Contains(number) && !claimed.ContainsKey(number))
            {
                numbers.Add(number);
            }
        }

        return numbers;
    }

    /// <summary>
    /// A record for each of those, named from <see cref="GentilicNaming.Renders"/> — the King James
    /// half of the naming rule, and the half that can answer nothing.
    ///
    /// <para>
    /// The other half takes whatever noun Strong's definition opens with, and that is safe only
    /// once a derivation has established the entry is a gentilic. Here nothing has: BHSA's analysis
    /// says the lexeme is one and the entry may still be <em>the Jewish (used adverbially)
    /// language</em>, <em>right</em>, <em>the heart</em> or <em>Matri, an Israelite</em>, whose
    /// leading nouns would be written as peoples. What the renderings hold is what the King James
    /// prints for the word, and an entry with no gentilic among them is refused and counted —
    /// seventeen of the seventy-eight, every one of them an adverb, a woman's designation or a
    /// man's name.
    /// </para>
    ///
    /// <para>
    /// A name the encyclopedia already holds a people under is that people. H3779 is the Aramaic
    /// Chaldean beside H3778's Hebrew one, H5985 the Ammonitess beside H5984's Ammonite, H3879 the
    /// Aramaic Levite beside the tribe: one nation the dictionary writes twice, and a second record
    /// each would be two pages nobody could tell apart. The number is written onto the record as
    /// another gentilic name instead, which is what puts its words on the page.
    /// </para>
    /// </summary>
    private (int Written, List<Entity> Joined, int Unnamed) Analyse(
        List<string> numbers,
        IReadOnlyDictionary<string, LexiconEntry> entries,
        Dictionary<string, Entity> peoples,
        HashSet<string> taken,
        PeopleFile file)
    {
        var namings = file.Namings.ToDictionary(n => n.Number, StringComparer.Ordinal);
        // The records this run has already built are held too, and they are not in the database
        // yet: the Moabitess arrives in the same pass as the Moabite.
        var held = db.Entities
            .Where(e => e.Kind == EntityKind.People)
            .AsEnumerable()
            .Concat(peoples.Values)
            .GroupBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        var joined = new List<Entity>();
        int written = 0, unnamed = 0;

        foreach (var number in numbers)
        {
            var entry = entries.GetValueOrDefault(number);
            var name = namings.GetValueOrDefault(number)?.Name
                       ?? GentilicNaming.Renders(entry?.KjvDefinition);
            if (name is null)
            {
                unnamed++;
                continue;
            }

            if (held.TryGetValue(name, out var already))
            {
                already.Names.Add(new EntityName
                {
                    Label = name, HebrewStrongNumber = number, Kind = GentilicName,
                });
                joined.Add(already);
                continue;
            }

            var entity = Record(
                PeopleFiles.Slug(name, number, slug => peoples.ContainsKey(slug) || taken.Contains(slug)),
                name,
                entry?.Definition is { Length: > 0 } said
                    ? $"{said}, as Strong's Dictionary describes them"
                    : null,
                null,
                null,
                Analysis);

            entity.Claims.Add(new EntityClaim
            {
                Method = LinkMethod.StatedBySource,
                Confidence = null,
                Source = BhsaTextSource.Definition.Name,
                Note = $"{number}, a lexeme BHSA analyses as a gentilic",
            });

            if (entry?.Definition is { Length: > 0 } quoted)
            {
                entity.Claims.Add(new EntityClaim
                {
                    Method = LinkMethod.StatedBySource,
                    Confidence = null,
                    Source = StrongGentilicLoader.Source,
                    Note = $"{number}: \"{quoted}\"",
                });
            }

            entity.Names.Add(new EntityName
            {
                Label = name, HebrewStrongNumber = number, Kind = GentilicName,
            });

            peoples[entity.Slug] = entity;
            held[name] = entity;
            written++;
        }

        return (written, joined, unnamed);
    }

    /// <summary>What kind of label a name row is, in the vocabulary the encyclopedia already uses.</summary>
    private const string GentilicName = "gentilic";

    private const string CollectiveName = "collective";

    /// <summary>
    /// What tells one people from another of the same name, in the shape the rest of the
    /// encyclopedia uses. It is Strong's own distinction and not a description of them: two peoples
    /// the King James alike calls Sabeans are told apart by which number he derives them from.
    /// </summary>
    private static string Distinguisher(StrongGentilic gentilic) =>
        $"{gentilic.Kind} of {gentilic.OriginNumber}, as Strong's Dictionary derives it";

    private Entity Record(
        string slug,
        string name,
        string? distinguisher,
        string? notes,
        int? origin,
        string source)
    {
        var entity = new Entity
        {
            Kind = EntityKind.People,
            Slug = slug,
            Name = name,
            Distinguisher = distinguisher,
            Notes = notes,
            OriginEntityId = origin,
            SourceId = SourceIdPrefix + slug,
            Source = source,
        };

        db.Entities.Add(entity);
        return entity;
    }

    /// <summary>
    /// How many gentilic rows now reach a page for the people they describe. Counted from the table
    /// after the save rather than while writing, because a row whose people failed to be written is
    /// the number worth knowing.
    /// </summary>
    private async Task<int> JoinTheGentilics(CancellationToken cancellationToken) =>
        await db.StrongGentilics.CountAsync(g => g.PeopleEntityId != null, cancellationToken);

    /// <summary>
    /// Every Hebrew word carrying a gentilic, annotated to the people that lexeme names.
    ///
    /// The word <em>is</em> the people's name, so this is the resolution a proper noun gets: one
    /// number, one record, nobody choosing. What it needs is the one guard BHSA can give — a word it
    /// marks <c>pers</c> or <c>topo</c> is the gentilic standing as a man's name or a town's, and
    /// there are eighteen of those against fourteen hundred that are the people. The two the corpus
    /// already annotated that way, Arodi and Machbannai, are exactly those eighteen, so nothing here
    /// contests an answer already given.
    /// </summary>
    private const string TheGentilicWords =
        """
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note)
        SELECT DISTINCT ON (w.id) w.id, n.entity_id, @confidence, FALSE,
               w.strong_number || CASE WHEN g.statement IS NULL
                   THEN ', which this record names as its own gentilic'
                   ELSE ', which Strong''s Dictionary derives from ' || g.origin_number
                        || ': "' || g.statement || '"' END
        FROM word w
        JOIN text t ON t.id = w.text_id AND t.slug = @witness
        JOIN entity_name n ON n.hebrew_strong_number = w.strong_number AND n.kind = @label
        JOIN entity e ON e.id = n.entity_id AND e.kind = 'people' AND e.id = ANY(@peoples)
        LEFT JOIN strong_gentilic g ON g.strong_number = w.strong_number
        WHERE coalesce(w.morphology->>'nameType', '') NOT IN ('pers', 'topo')
        ORDER BY w.id, n.entity_id
        """;

    /// <summary>The words of the peoples this run wrote, which on a later boot are the only new ones.</summary>
    private async Task<int> AnnotateTheGentilics(int[] peoples, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, TheGentilicWords, cancellationToken,
            ("confidence", GentilicResolution), ("witness", EntityCandidates.Witness),
            ("label", GentilicName), ("peoples", peoples));
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

        var spelled = EnumSpelling.Of(ByTheForm);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", spelled), ("source", ByTheGentilic));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", spelled), ("source", ByTheGentilic));

        var byText = await Annotating.ByText(connection, transaction, ByTheGentilic, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return byText.Sum(t => t.Words);
    }

    private async Task<(int Occurrences, int Words)> Rulings(
        PeopleFile file,
        Dictionary<string, Entity> peoples,
        CancellationToken cancellationToken)
    {
        var seed = new List<(long, int, double?, bool, string)>(file.Rulings.Count);
        var before = await db.Entities
            .Where(e => e.Kind == EntityKind.People)
            .Select(e => e.Slug)
            .ToListAsync(cancellationToken);

        foreach (var ruling in file.Rulings)
        {
            if (!peoples.TryGetValue(ruling.People, out var people))
            {
                // A people an earlier boot wrote had its rulings applied on that boot.
                if (before.Contains(ruling.People, StringComparer.Ordinal))
                {
                    continue;
                }

                logger.LogWarning(
                    "The ruling on {Reference} names the people \"{Slug}\", which was not written, so "
                    + "the word was left unannotated",
                    ruling.Reference,
                    ruling.People);
                continue;
            }

            seed.Add((ruling.WordId, people.Id, null, false, ruling.Why));
        }

        var words = seed.Count == 0
            ? 0
            : await Annotate(seed, LinkMethod.Manual, file.Source, cancellationToken);

        return (seed.Count, words);
    }

    private async Task<(int Occurrences, int Undecided, int Words)> Readings(
        PeopleFile file,
        string resources,
        Dictionary<string, Entity> peoples,
        CancellationToken cancellationToken)
    {
        var collectives = file.Tribes
            .Where(t => peoples.ContainsKey(t.Slug))
            .ToDictionary(t => t.CollectiveNumber, t => peoples[t.Slug].Id, StringComparer.Ordinal);

        if (collectives.Count == 0)
        {
            return (0, 0, 0);
        }

        var directory = configuration[SenseReadingFiles.ConfigurationKey] is { Length: > 0 } configured
            ? configured
            : Path.Combine(resources, SenseReadingFiles.DefaultFolder);

        if (!Directory.Exists(directory))
        {
            logger.LogInformation(
                "No model readings at {Directory}, so no occurrence of a collective name was "
                + "annotated from one. The peoples themselves are written either way",
                directory);
            return (0, 0, 0);
        }

        var (readings, _, _, _, _) = SenseReadingFiles.Read(directory);
        var seed = new List<(long, int, double?, bool, string)>();
        var undecided = 0;

        foreach (var reading in readings)
        {
            if (reading.Referent != SenseReading.Unlisted
                || !collectives.TryGetValue(reading.StrongNumber, out var people))
            {
                continue;
            }

            // The description, or the sentence that stands where a re-ask replaced the answer. The
            // supersession records what the later run said and not what it called the referent, so
            // the 69 Judah occurrences the re-ask moved to unlisted have no description at all —
            // and those are precisely the ones an audit named the tribe for. Their reason says the
            // same thing in the same words and is read under exactly the same two tests.
            var describes = string.IsNullOrWhiteSpace(reading.Names) ? reading.Reason : reading.Names;

            if (!CollectiveReadings.NamesAPeople(describes)
                || !Measured.TryGetValue(reading.Confidence, out var confidence))
            {
                undecided++;
                continue;
            }

            seed.Add((
                reading.WordId,
                people,
                confidence,
                false,
                $"{reading.StrongNumber}, read as naming a collective the encyclopedia did not hold "
                + $"— \"{describes}\" — with {reading.Confidence} confidence"));
        }

        var words = seed.Count == 0
            ? 0
            : await Annotate(seed, LinkMethod.ModelReading, FromAReading, cancellationToken);

        return (seed.Count, undecided, words);
    }

    /// <summary>
    /// A people's own list of the verses it is named in, derived from the words this pass annotated
    /// rather than supplied by anybody.
    ///
    /// Every other layer's list comes from a dataset and the source column says which. There is no
    /// dataset here, so the source says what it actually is — our own annotation, read back to the
    /// verse — and a reader can weigh it accordingly. It is written after the annotations rather
    /// than beside them so that nothing in this run can be corroborated by a list this run derived
    /// from it.
    /// </summary>
    /// <summary>Where the verse stating a people's descent comes from.</summary>
    internal const string FromTheStatedDescent =
        "Essenthos, from the verse that calls the people's ancestor the father of it";

    /// <summary>
    /// The verse that states where a people comes from, which its own words never reach.
    ///
    /// <em>He is the father of the Moabites unto this day</em> writes the people with the ancestor's
    /// own name — אֲבִי מוֹאָב, <em>father of Moab</em> — so the gentilic the people's list is built
    /// from is not in it, and the man's word is annotated to the man. What marks the people there is
    /// the construct: the name standing as what somebody is <em>father of</em>, directly or as
    /// <em>father of the sons of</em>, as Genesis 19:38 writes the Ammonites. A verse whose BHSA text
    /// has one of the people's own numbers, or its ancestor's name, in that position is listed on
    /// the people, which is the citation a <em>descendants of</em> relation needs.
    /// </summary>
    internal const string StatedDescent =
        """
        WITH numbers AS (
            SELECT DISTINCT p.id AS entity_id, n.hebrew_strong_number AS number
            FROM entity p
            JOIN entity_name n ON n.entity_id IN (p.id, p.origin_entity_id)
            WHERE p.kind = 'people' AND p.origin_entity_id IS NOT NULL
              AND n.hebrew_strong_number IS NOT NULL AND position(',' IN n.hebrew_strong_number) = 0),
        stated AS (
            SELECT DISTINCT n.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse
            FROM numbers n
            JOIN word w ON w.strong_number = n.number
            JOIN text t ON t.id = w.text_id AND t.slug = @witness
            JOIN word father ON father.text_id = w.text_id AND father.verse_id = w.verse_id
                 AND father.strong_number = 'H1' AND father.morphology->>'state' = 'c'
            LEFT JOIN word sons ON sons.text_id = w.text_id AND sons.verse_id = w.verse_id
                 AND sons.position = w.position - 1
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE father.position = w.position - 1
               OR (father.position = w.position - 2 AND sons.strong_number = 'H1121'
                   AND sons.morphology->>'state' = 'c'))
        INSERT INTO entity_verse (entity_id, canonical_book, canonical_chapter, canonical_verse,
                                  label, disputed, source)
        SELECT s.entity_id, s.canonical_book, s.canonical_chapter, s.canonical_verse, NULL, FALSE, @source
        FROM stated s
        WHERE NOT EXISTS (
            SELECT 1 FROM entity_verse cited
            WHERE cited.entity_id = s.entity_id AND cited.source = @source
              AND (cited.canonical_book, cited.canonical_chapter, cited.canonical_verse)
                  = (s.canonical_book, s.canonical_chapter, s.canonical_verse))
        """;

    private async Task<int> ReferenceTheDescent(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(StatedDescent, connection);
        command.Parameters.AddWithValue("source", FromTheStatedDescent);
        command.Parameters.AddWithValue("witness", EntityCandidates.Witness);
        command.CommandTimeout = Annotating.Patient;
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> Reference(int[] peoples, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(
            $"""
            WITH {Annotating.Settled}
            INSERT INTO entity_verse (entity_id, canonical_book, canonical_chapter, canonical_verse,
                                      label, disputed, source)
            SELECT DISTINCT a.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse,
                   e.name, FALSE, @source
            FROM settled a
            JOIN entity e ON e.id = a.entity_id AND e.kind = 'people' AND e.id = ANY(@peoples)
            JOIN word w ON w.id = a.word_id
            JOIN text t ON t.id = w.text_id AND t.slug = @witness
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            WHERE NOT EXISTS (
                SELECT 1 FROM entity_verse cited
                WHERE cited.entity_id = a.entity_id AND cited.source = @source
                  AND (cited.canonical_book, cited.canonical_chapter, cited.canonical_verse)
                      = (r.canonical_book, r.canonical_chapter, r.canonical_verse))
            """,
            connection);
        command.Parameters.AddWithValue("source", FromOurOwnWords);
        command.Parameters.AddWithValue("witness", EntityCandidates.Witness);
        command.Parameters.AddWithValue("peoples", peoples);
        command.CommandTimeout = Annotating.Patient;

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<int> Annotate(
        List<(long, int, double?, bool, string)> seed,
        LinkMethod method,
        string source,
        CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Seed(connection, seed, cancellationToken);
        await Annotating.CarryAcrossLinks(connection, transaction, cancellationToken);

        var spelled = EnumSpelling.Of(method);
        await Annotating.Run(connection, transaction, Annotating.Settle, cancellationToken,
            ("method", spelled), ("source", source));
        await Annotating.Run(connection, transaction, Annotating.Claim, cancellationToken,
            ("method", spelled), ("source", source));

        var byText = await Annotating.ByText(connection, transaction, source, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return byText.Sum(t => t.Words);
    }
}
