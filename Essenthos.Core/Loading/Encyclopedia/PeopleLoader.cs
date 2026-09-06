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
internal sealed record PeopleOutcome(
    bool AlreadyLoaded,
    int FromStrong,
    int Tribes,
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
            : $"{FromStrong + Tribes} peoples — {FromStrong} from the gentilics Strong derives and " +
              $"{Tribes} tribes of Israel no lexicon names — of which {WithOrigin} name an ancestor " +
              $"or a homeland the encyclopedia holds, in {Elapsed}. {Gentilics} gentilic rows now " +
              $"reach the people they were always about. {Annotated} words name a people: from " +
              $"{Ruled} occurrences a review decided and {Read} a model described as a people, plus " +
              $"every word carrying a gentilic. {Undecided} further readings name a people and a " +
              $"territory in one breath and are left alone. {Referenced} verse references written.";
}

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
/// Idempotent on the kind existing at all, which is the one thing that cannot be true before this
/// has run.
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
        if (await db.Entities.AnyAsync(e => e.Kind == EntityKind.People, cancellationToken))
        {
            logger.LogInformation("The peoples are already there; nothing to do");
            return new PeopleOutcome(true, 0, 0, 0, 0, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var file = PeopleFiles.Read();
        var peoples = await Write(file, cancellationToken);

        var gentilics = await JoinTheGentilics(cancellationToken);
        var annotated = await AnnotateTheGentilics(cancellationToken);

        var ruled = await Rulings(file, peoples, cancellationToken);
        annotated += ruled.Words;

        var read = await Readings(file, resources, peoples, cancellationToken);
        annotated += read.Words;

        var referenced = await Reference(cancellationToken);

        var outcome = new PeopleOutcome(
            false,
            peoples.Count - file.Tribes.Count,
            file.Tribes.Count,
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
    /// The records themselves: the tribes first, so that a tribe claims its own gentilic entry
    /// rather than a second record being made from the same word.
    /// </summary>
    private async Task<Dictionary<string, Entity>> Write(
        PeopleFile file,
        CancellationToken cancellationToken)
    {
        var origins = await db.Entities
            .Where(e => e.Kind != EntityKind.People)
            .Select(e => new { e.Slug, e.Id })
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);

        var peoples = new Dictionary<string, Entity>(StringComparer.Ordinal);
        var claimed = new Dictionary<string, Entity>(StringComparer.Ordinal);

        foreach (var tribe in file.Tribes)
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
        var entries = await db.StrongEntries
            .Select(e => new { e.StrongNumber, e.Definition, e.KjvDefinition })
            .ToDictionaryAsync(e => e.StrongNumber, StringComparer.Ordinal, cancellationToken);

        foreach (var gentilic in await db.StrongGentilics
                     .OrderBy(g => g.StrongNumber)
                     .ToListAsync(cancellationToken))
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

            var entity = Record(
                PeopleFiles.Slug(name, gentilic.StrongNumber, peoples.ContainsKey),
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

        await db.SaveChangesAsync(cancellationToken);
        return peoples;
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
        JOIN entity e ON e.id = n.entity_id AND e.kind = 'people'
        LEFT JOIN strong_gentilic g ON g.strong_number = w.strong_number
        WHERE coalesce(w.morphology->>'nameType', '') NOT IN ('pers', 'topo')
        ORDER BY w.id, n.entity_id
        """;

    private async Task<int> AnnotateTheGentilics(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await Annotating.Run(connection, transaction, Annotating.Workspace, cancellationToken);
        await Annotating.Run(connection, transaction, TheGentilicWords, cancellationToken,
            ("confidence", GentilicResolution), ("witness", EntityCandidates.Witness),
            ("label", GentilicName));
        await Annotating.Run(connection, transaction, Annotating.Carry, cancellationToken,
            ("witness", EntityCandidates.Witness));

        var spelled = EnumSpelling.Of(LinkMethod.StrongNumber);
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
        foreach (var ruling in file.Rulings)
        {
            if (!peoples.TryGetValue(ruling.People, out var people))
            {
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

        var collectives = file.Tribes
            .Where(t => peoples.ContainsKey(t.Slug))
            .ToDictionary(t => t.CollectiveNumber, t => peoples[t.Slug].Id, StringComparer.Ordinal);

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
    private async Task<int> Reference(CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await using var command = new NpgsqlCommand(
            """
            INSERT INTO entity_verse (entity_id, canonical_book, canonical_chapter, canonical_verse,
                                      label, disputed, source)
            SELECT DISTINCT a.entity_id, r.canonical_book, r.canonical_chapter, r.canonical_verse,
                   e.name, FALSE, @source
            FROM word_entity a
            JOIN entity e ON e.id = a.entity_id AND e.kind = 'people'
            JOIN word w ON w.id = a.word_id
            JOIN text t ON t.id = w.text_id AND t.slug = @witness
            JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
            """,
            connection);
        command.Parameters.AddWithValue("source", FromOurOwnWords);
        command.Parameters.AddWithValue("witness", EntityCandidates.Witness);
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
        await Annotating.Run(connection, transaction, Annotating.Carry, cancellationToken,
            ("witness", EntityCandidates.Witness));

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
