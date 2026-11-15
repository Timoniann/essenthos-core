using Essenthos.Core.Configuration;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What one chapter speaks of: the people, places, peoples, titles and terms it names, the events
/// the sources set in it, and the periods it falls in — drawn from records the encyclopedia already
/// holds, for a panel beside the reader.
///
/// A record is here because a word of some text in the chapter names it, or because the record's
/// own list of verses says it is named here. Both, not either: the word layer reaches every text
/// the corpus aligns, so a man the English calls <em>he</em> and the Hebrew names is still found,
/// and the verse lists reach the places the word layer has not resolved. Both are addressed in the
/// shared canonical frame, which is the numbering the reader's rows already use.
///
/// <para>
/// None of that changes while a release is served, so the serving build stores it (TSK-1669): each chapter's skeleton
/// (<see cref="ChapterSkeleton"/>) and each record's line in each pair of languages (<see cref="ContextLineBody"/>). A
/// request reads the two, the names its open texts add and the pictures, and puts them together here; a chapter or a pair
/// of languages the build did not store is composed from the served tables the same way.
/// </para>
/// </summary>
internal static class ContextEndpoints
{
    public static void MapContext(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/context/{book}/{chapter:int}", async (
            string book,
            int chapter,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            [FromQuery] string? texts,
            [FromQuery(Name = "canon")] string? mode,
            AppDbContext db,
            ICanonIndex canon,
            ContextWeightsCache weights,
            SiteSettingsFile settings,
            CancellationToken cancellationToken) =>
        {
            var (ordinal, refusal) = await Chapter(canon, book, chapter, cancellationToken);
            if (refusal is not null)
            {
                return refusal;
            }

            if (!CanonAttestation.TryMode(mode, out var shown))
            {
                return ApiResults.NotFound($"There is no canon \"{mode}\". Try one of: {Canons.Names}.");
            }

            List<TextEntry>? open = null;
            if (texts is not null)
            {
                open = [];
                foreach (var slug in texts.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (await canon.Text(slug, cancellationToken) is not { } text)
                    {
                        return ApiResults.NotFound($"There is no text \"{slug}\".");
                    }

                    open.Add(text);
                }
            }

            return Results.Ok(await Context(
                db, ordinal, chapter, language, await weights.Get(cancellationToken), cancellationToken,
                settings.Is(SiteSettings.GeneratedImages), prose, open, shown, stored: true));
        });

        // The families among the chapter's people, apart from the rest because a genealogy's trees
        // are the one part of the panel that can run to hundreds of people.
        routes.MapGet("/context/{book}/{chapter:int}/family", async (
            string book,
            int chapter,
            [FromQuery] string? language,
            [FromQuery] string? prose,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var (ordinal, refusal) = await Chapter(canon, book, chapter, cancellationToken);
            return refusal ?? Results.Ok(await ChapterFamilies.Of(db, ordinal, chapter, language, cancellationToken, prose, stored: true));
        });
    }

    /// <summary>The book's ordinal where the address names a chapter the shared numbering has, or why not.</summary>
    private static async Task<(int Ordinal, IResult? Refusal)> Chapter(
        ICanonIndex canon,
        string book,
        int chapter,
        CancellationToken cancellationToken)
    {
        var ordinal = BookReferences.ResolveOrdinal(book);
        if (ordinal is null)
        {
            return (0, ApiResults.NotFound(BookReferences.FormatHint(book)));
        }

        var chapterCount = await canon.ChapterCount(ordinal.Value, cancellationToken);
        return chapter < 1 || chapter > chapterCount
            ? (0, ApiResults.NotFound(
                $"{BookReferences.Name(ordinal.Value)} has {chapterCount} chapters in the shared numbering, " +
                $"so there is no chapter {chapter}."))
            : (ordinal.Value, null);
    }

    /// <summary>The context with the weights counted afresh, for a caller that holds none.</summary>
    internal static async Task<ChapterContextResponse> Context(
        AppDbContext db,
        int book,
        int chapter,
        string? language,
        CancellationToken cancellationToken) =>
        await Context(db, book, chapter, language, await ContextWeights.Count(db, cancellationToken), cancellationToken);

    /// <param name="stored">
    /// Whether the chapter and the lines the build stored are read where it stored them. The context is composed from the
    /// served tables otherwise, and also where the build stored no such chapter or pair of languages, or where an open text
    /// brings a record the chapter itself does not name.
    /// </param>
    internal static async Task<ChapterContextResponse> Context(
        AppDbContext db,
        int book,
        int chapter,
        string? language,
        ContextWeights weights,
        CancellationToken cancellationToken,
        bool generated = true,
        string? prose = null,
        IReadOnlyList<TextEntry>? open = null,
        CanonDefinition? mode = null,
        bool stored = false)
    {
        var exceptions = await ChapterSkeletons.Exceptions(db, book, chapter, open ?? [], cancellationToken);
        ChapterSkeleton? skeleton = null;
        IReadOnlyDictionary<string, ContextLineBody>? lines = null;
        if (stored && await Stored(db, book, chapter, cancellationToken) is { } held)
        {
            var named = held.Records.Select(r => r.Slug).ToHashSet(StringComparer.Ordinal);
            if (exceptions.All(x => named.Contains(x.Slug)))
            {
                skeleton = held;
                lines = await StoredLines(db, held, language, prose, cancellationToken);
            }
        }

        if (skeleton is null || lines is null)
        {
            skeleton = await ChapterSkeletons.Gather(db, book, chapter, cancellationToken, [.. exceptions.Select(x => x.Slug)]);
            lines = await ContextLines.Gather(db, [.. skeleton.Records.Select(r => r.Slug)], language, prose, cancellationToken);
        }

        return await Assemble(db, skeleton, lines, exceptions, weights, cancellationToken, generated, language, prose, open is not null, mode);
    }

    /// <summary>The chapter's skeleton as the build stored it, in one statement; null where it stored none.</summary>
    internal static async Task<ChapterSkeleton?> Stored(AppDbContext db, int book, int chapter, CancellationToken cancellationToken) =>
        await db.ContextChapters
                .Where(c => c.CanonicalBook == book && c.CanonicalChapter == chapter)
                .Select(c => c.Body)
                .FirstOrDefaultAsync(cancellationToken) is { } body
            ? ChapterSkeletons.Read(body)
            : null;

    /// <summary>
    /// The lines of every record of the skeleton as the build stored them for this pair of languages, in one statement;
    /// null where it did not store this pair, so the request composes them.
    /// </summary>
    private static async Task<IReadOnlyDictionary<string, ContextLineBody>?> StoredLines(
        AppDbContext db,
        ChapterSkeleton skeleton,
        string? language,
        string? prose,
        CancellationToken cancellationToken)
    {
        var words = ReaderLanguages.Prose(prose, language);
        if (language is not { Length: > 0 } || words is not { Length: > 0 })
        {
            return null;
        }

        var slugs = skeleton.Records.Select(r => r.Slug).ToList();
        if (slugs.Count == 0)
        {
            return new Dictionary<string, ContextLineBody>();
        }

        var rows = await db.ContextLines
            .Where(l => l.Prose == words && l.Names == language && slugs.Contains(l.Slug))
            .Select(l => new { l.Slug, l.Body })
            .ToListAsync(cancellationToken);
        return rows.Count == slugs.Count
            ? rows.ToDictionary(r => r.Slug, r => ContextLines.Read(r.Body), StringComparer.Ordinal)
            : null;
    }

    /// <summary>
    /// The answer put together from what the chapter is made of and the records' lines: the names the open texts add, the
    /// words for God settled, the records of the canon mode, their pictures, the order of the rows, and the topics weighed
    /// against the rows shown.
    /// </summary>
    private static async Task<ChapterContextResponse> Assemble(
        AppDbContext db,
        ChapterSkeleton skeleton,
        IReadOnlyDictionary<string, ContextLineBody> lines,
        IReadOnlyList<ContextException> exceptions,
        ContextWeights weights,
        CancellationToken cancellationToken,
        bool generated,
        string? language,
        string? prose,
        bool asked,
        CanonDefinition? mode)
    {
        var verses = skeleton.Verses.ToDictionary(v => v.Key, v => new SortedSet<int>(v.Value), StringComparer.Ordinal);
        var how = skeleton.How.ToDictionary(h => h.Key, h => new SortedSet<string>(h.Value, StringComparer.Ordinal), StringComparer.Ordinal);
        var spoken = skeleton.Spoken.ToDictionary(s => s.Key, s => new SortedSet<int>(s.Value), StringComparer.Ordinal);
        foreach (var exception in exceptions)
        {
            if (!verses.TryGetValue(exception.Slug, out var at))
            {
                verses[exception.Slug] = at = [];
            }

            at.UnionWith(exception.Verses);
            ChapterSkeletons.Route(how, exception.Slug, ChapterSkeletons.Excepted);
        }

        ChapterSalience.SettleWordsForGod(verses, how, spoken);
        var received = mode is null ? null : CanonAttestation.Received(mode).ToHashSet(StringComparer.Ordinal);
        var named = skeleton.Records.Where(r => verses.ContainsKey(r.Slug)).ToList();
        var records = named
            .Where(r => received is null || r.Canons.Length == 0 || r.Canons.Any(received.Contains))
            .ToList();
        ContextLineBody Line(string slug) => lines.GetValueOrDefault(slug) ?? ContextLineBody.Empty;

        // Every record a line under any of the chapter's names points at, those the canon mode hides included.
        var targets = new SortedDictionary<string, ContextRecordResponse>(StringComparer.Ordinal);
        foreach (var record in named)
        {
            foreach (var (slug, target) in Line(record.Slug).Records ?? [])
            {
                targets.TryAdd(slug, target);
            }
        }

        // A face for the people only: a place's or a thing's picture is its page's, and a row of the
        // words for God carries the one picture God's record leads with, which is light and not a face.
        var person = EnumSpelling.Of(EntityKind.Person);
        var pictured = await ImageEndpoints.Leading(
            db,
            [.. records.Where(r => r.Kind == person).Select(r => r.Slug)],
            cancellationToken,
            generated);

        var entities = records
            .Select(r => new ContextEntityResponse(r.Slug, r.Kind, r.Name, r.Distinguisher, [.. verses[r.Slug]])
            {
                LocalName = Line(r.Slug).LocalName,
                LocalDistinguisher = Line(r.Slug).LocalDistinguisher,
                Descriptor = Line(r.Slug).Descriptor,
                Meaning = r.Meaning,
                PlaceKind = r.PlaceKind,
                Location = r.Location is { } at ? new EntityLocationResponse(at.Lon, at.Lat, at.Kind, at.Confidence) : null,
                How = [.. how.GetValueOrDefault(r.Slug) ?? []],
                SourceId = Datasets.Own,
                Group = ChapterSalience.WordsForGod.Contains(r.Slug) ? ChapterSalience.GodGroup : null,
                ChapterName = r.ChapterName,
                Thumbnail = pictured.GetValueOrDefault(r.Slug),
                Exceptions = asked
                    ? [.. exceptions.Where(x => x.Slug == r.Slug).Select(x => new ContextExceptionResponse(x.Text, x.Origin, [.. x.Verses]))]
                    : null,
            })
            .OrderByDescending(e => ChapterSalience.Of(e.Slug, verses, how, spoken))
            .ThenBy(e => weights.EntityChapters.GetValueOrDefault(e.Slug, int.MaxValue))
            .ThenBy(e => e.LocalName ?? e.Name, StringComparer.CurrentCulture)
            .ThenBy(e => e.Slug, StringComparer.Ordinal)
            .ToList();

        // An event's title in the reader's language is a machine translation into the language of words, where one is held.
        var local = EntityNames.Local(ReaderLanguages.Prose(prose, language));
        return new ChapterContextResponse(
            new BookRefResponse(skeleton.Book, BookReferences.Name(skeleton.Book), BookReferences.Slug(skeleton.Book)),
            skeleton.Chapter,
            entities,
            [
                .. skeleton.Events.Select(e => new ContextEventResponse(e.Slug, e.Name, e.Reference, e.Dates)
                {
                    LocalName = local is null ? null : e.LocalNames?.GetValueOrDefault(local),
                }),
            ],
            skeleton.Periods,
            skeleton.PeriodsFrom,
            skeleton.Measures,
            skeleton.Commandments,
            ChapterTopics.Ranked(skeleton, weights, entities),
            targets);
    }
}

/// <summary>
/// One event the sources set in the chapter, as the panel lists it: its name, its verse, and its
/// year in each reckoning that dates it. The description, the notes and the working are the event's
/// own page's.
/// </summary>
internal record ContextEventResponse(
    string Slug,
    string Name,
    VerseRefResponse? Reference,
    IList<ContextEventDateResponse> Dates)
{
    /// <summary>The title in the reader's language, a machine translation; null for an English reader and where none is held.</summary>
    public string? LocalName { get; init; }
}

/// <param name="Entities">
/// Every record the chapter names, the most present first: by the verses whose words name it, a
/// verse only a source's list gives counting half; then the record found in fewer chapters of the
/// Bible first, then by the name shown.
/// </param>
/// <param name="Events">The events the sources set in this chapter, in the order of its verses.</param>
/// <param name="Periods">The periods the chapter falls in, the widest first.</param>
/// <param name="Measures">
/// The units of measure and money the chapter's words carry, by lexicon entry, in the order the
/// chapter first uses them.
/// </param>
/// <param name="Commandments">
/// The commandments of Maimonides' count that rest on a verse of this chapter, in the order the
/// chapter gives them. Empty outside the Torah.
/// </param>
/// <param name="Topics">
/// The subjects of Nave's Topical Bible filing verses of this chapter: its themes, the most particular
/// to it first, then the index's readings of it. A person's own entry is not among them.
/// </param>
/// <param name="PeriodsFrom">
/// <c>events</c> where the chapter's own events placed it, <c>neighbours</c> where it has none and
/// the dated events on either side of it in the book did, and null where nothing placed it.
/// </param>
/// <param name="Records">
/// Every record a line under a name points at, by slug, said once for the whole answer: a claim names
/// its target by slug, and David is the target of dozens of claims in Matthew 1.
/// </param>
internal record ChapterContextResponse(
    BookRefResponse Book,
    int Chapter,
    IList<ContextEntityResponse> Entities,
    IList<ContextEventResponse> Events,
    IList<ContextPeriodResponse> Periods,
    string? PeriodsFrom,
    IList<ContextMeasureResponse> Measures,
    IList<ContextCommandmentResponse> Commandments,
    IList<ContextTopicResponse> Topics,
    IDictionary<string, ContextRecordResponse> Records);


/// <param name="Kind">person, place, people, title or term.</param>
/// <param name="Verses">The verses of this chapter that name it, in the shared numbering.</param>
internal record ContextEntityResponse(
    string Slug,
    string Kind,
    string Name,
    string? Distinguisher,
    IList<int> Verses)
{
    /// <summary>The name in the language asked for, where the corpus has one.</summary>
    public string? LocalName { get; init; }

    /// <summary>
    /// Our own line under the name in the language asked for, where this corpus wrote it and rendered
    /// it; null otherwise, and a client shows <see cref="Distinguisher"/> as the source's words.
    /// </summary>
    public string? LocalDistinguisher { get; init; }

    /// <summary>The line under the name, as the claims that make it; see <see cref="ContextDescriptorResponse"/>.</summary>
    public ContextDescriptorResponse? Descriptor { get; init; }

    /// <summary>What its name means, where the corpus holds a meaning for the name; in English, as the sources give it.</summary>
    public string? Meaning { get; init; }

    /// <summary>A place's kind as its source classifies it; null for everything else.</summary>
    public string? PlaceKind { get; init; }

    /// <summary>Where a place is, where the gazetteer identifies it; null for a place it cannot.</summary>
    public EntityLocationResponse? Location { get; init; }

    /// <summary>
    /// How it reaches the chapter: <c>words</c> where a word of some text names it, <c>listed</c>
    /// where its source lists a verse here, <c>passage</c> where a passage about it runs through.
    /// </summary>
    public IList<string> How { get; init; } = [];

    /// <summary>The declared dataset its record comes from, where one claims it.</summary>
    public string? SourceId { get; init; }

    /// <summary>
    /// <c>god</c> for the records of the words for God, which a reader is shown as one row saying
    /// which of them the chapter uses; null for everything else.
    /// </summary>
    public string? Group { get; init; }

    /// <summary>
    /// The name the chapter calls it by where that is not its headword: John 1 calls the man his
    /// record heads Bartholomew <em>Nathanael</em>. English, as the source labels the verse; null
    /// where the chapter uses the headword or labels nothing.
    /// </summary>
    public string? ChapterName { get; init; }

    /// <summary>The picture a person's page leads with, to show small beside the name; null for everything else.</summary>
    public EntityThumbnailResponse? Thumbnail { get; init; }

    /// <summary>
    /// Where a text the reader has open names the record against its base text in this chapter: <c>added</c> where the text
    /// names it and its base does not, <c>differs</c> where the text names it in place of the record its base names. Each
    /// carries the text and the verses. Null where the request named no texts; empty where none of them makes an exception
    /// for the record.
    /// </summary>
    public IList<ContextExceptionResponse>? Exceptions { get; init; }
}

/// <param name="Text">The open text that carries the exception.</param>
/// <param name="Origin"><c>added</c> or <c>differs</c>.</param>
/// <param name="Verses">The verses of the chapter that carry it, in the shared numbering.</param>
internal record ContextExceptionResponse(string Text, string Origin, IList<int> Verses);
