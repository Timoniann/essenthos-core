using System.Diagnostics;
using Essenthos.Core.BetaMasaheft;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Products;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading;

internal sealed record FrameOutcome(string Slug, bool AlreadyPlaced, int Verses, int References, int Moved)
{
    public override string ToString() =>
        AlreadyPlaced
            ? $"{Slug} is already placed in the frame"
            : $"{Slug}: {Verses} verses placed as {References} references, {Moved} of them at an address " +
              "other than their own";
}

internal sealed record ReferenceDraft(int VerseId, int Book, int Chapter, int Verse, bool IsPrimary);

/// <summary>A verse as the frame needs it: its own address, its letter and how much text it holds.</summary>
internal sealed record PlacedVerse(int Id, int Book, int Chapter, int Number, string Label, int Length);

/// <summary>
/// Puts every verse of a text into the shared address space, so that a chapter asked for by its
/// canonical name resolves in any text.
///
/// This is not a link. A reference is where a verse sits in one frame; a link is a correspondence
/// between the words of two texts. Pairing two texts by verse number instead of through the frame
/// is what made the reader show different passages in its two panes.
/// </summary>
internal sealed class CanonicalFrameLoader(AppDbContext db, ILogger<CanonicalFrameLoader> logger)
{
    private const string ReferenceImport =
        """
        COPY verse_reference (verse_id, canonical_book, canonical_chapter, canonical_verse, is_primary)
        FROM STDIN (FORMAT BINARY)
        """;

    /// <summary>
    /// Places the text in the frame: computes where each verse stands, writes it as the text's frame file
    /// when a product store is given (<see cref="FrameFile"/>), and fills the text's rows of
    /// <c>verse_reference</c> from what the file says.
    /// </summary>
    /// <param name="source">What the verses were read from, for the file's header.</param>
    public async Task<FrameOutcome> Place(
        Text text,
        VersificationRules rules,
        CancellationToken cancellationToken = default,
        ProductStore? store = null,
        string? source = null)
    {
        if (!rules.Covers(text.Versification))
        {
            throw new InvalidOperationException(
                $"The text \"{text.Slug}\" follows {text.Versification} numbering, which the versification " +
                "data does not describe. Placing it by another tradition's rules would put every verse of it " +
                "at a plausible and wrong address; leave it out of the frame instead.");
        }

        var started = Stopwatch.StartNew();
        var framed = await Framed(text, rules, cancellationToken);
        var rows = framed.Rows;
        if (store is not null)
        {
            Write(text, rules, store, framed, source ?? db.Database.GetDbConnection().Database);
            using var reader = store.Open(FrameFile.Product(text.Slug), FrameRow.Schema);
            rows = reader.ReadAll();
        }

        return await Fill(text, framed.Verses, framed.Existing, rows, started, cancellationToken);
    }

    /// <summary>Fills the text's rows of <c>verse_reference</c> from its frame file.</summary>
    public async Task<FrameOutcome> Fill(Text text, ProductStore store, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var (verses, existing) = await Held(text, cancellationToken, lengths: false);
        using var reader = store.Open(FrameFile.Product(text.Slug), FrameRow.Schema);
        return await Fill(text, verses, existing, reader.ReadAll(), started, cancellationToken);
    }

    /// <summary>The file's rows: every verse it lists at its addresses, every other verse at its own.</summary>
    internal static List<ReferenceDraft> Drafts(IReadOnlyList<PlacedVerse> verses, IReadOnlyList<FrameRow> rows)
    {
        var listed = rows.SelectMany(row => row.Verses()).ToDictionary(verse => verse.Own, verse => verse.Canonical);
        var drafts = new List<ReferenceDraft>(verses.Count + listed.Count);
        foreach (var verse in verses)
        {
            var own = new VerseKey(verse.Book, verse.Chapter, verse.Number, verse.Label);
            if (!listed.Remove(own, out var canonical))
            {
                canonical = [FrameFile.AtOwn(own)];
            }

            drafts.AddRange(canonical.Select((address, index) =>
                new ReferenceDraft(verse.Id, address.Book, address.Chapter, address.Verse, index == 0)));
        }

        return listed.Count == 0
            ? drafts
            : throw new InvalidOperationException(
                $"The frame file lists {listed.Count} verses the text does not hold, the first {listed.Keys.Min()}; " +
                "it was written from other verses than these and has to be written again.");
    }

    private async Task<FrameOutcome> Fill(
        Text text,
        IReadOnlyList<PlacedVerse> verses,
        IReadOnlyList<ReferenceDraft> existing,
        IReadOnlyList<FrameRow> rows,
        Stopwatch started,
        CancellationToken cancellationToken)
    {
        var expected = Drafts(verses, rows);
        if (existing.Count == expected.Count && existing.ToHashSet().SetEquals(expected))
        {
            logger.LogInformation("Text {Slug} is already placed in the frame", text.Slug);
            return new FrameOutcome(text.Slug, AlreadyPlaced: true, 0, 0, 0);
        }

        // One transaction: a failed replacement leaves the text's previous rows (RUL-0192).
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        await db.VerseReferences
            .Where(reference => reference.Verse!.TextId == text.Id)
            .ExecuteDeleteAsync(cancellationToken);

        await using (var writer = await connection.BeginBinaryImportAsync(ReferenceImport, cancellationToken))
        {
            foreach (var reference in expected)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(reference.VerseId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(reference.Book, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(reference.Chapter, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(reference.Verse, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(reference.IsPrimary, NpgsqlDbType.Boolean, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);

        var versesById = verses.ToDictionary(verse => verse.Id);
        var moved = expected
            .Where(reference => reference.IsPrimary)
            .Count(reference => reference.Chapter != versesById[reference.VerseId].Chapter ||
                                reference.Verse != versesById[reference.VerseId].Number);
        var outcome = new FrameOutcome(text.Slug, AlreadyPlaced: false, verses.Count, expected.Count, moved);
        logger.LogInformation("Placed {Outcome} in {Elapsed}", outcome, started.Elapsed);
        return outcome;
    }

    /// <summary>A text's frame as computed now, with what decided it, and the rows the corpus holds.</summary>
    internal sealed record FramedText(
        List<PlacedVerse> Verses,
        List<ReferenceDraft> Existing,
        List<FrameRow> Rows,
        List<(Versification Tradition, PassageDecision Decision)> Decisions,
        int Titles);

    /// <summary>
    /// The text's frame as its file says it: where the rules put each verse, and the title verse a
    /// psalm's first verse also covers where the corpus holds that row. Those rows are written after the
    /// frame by the passes that read the titles in (<see cref="PsalmOpeningLoader"/>,
    /// <see cref="SuperscriptionFrameLoader"/>) from the verse's words; a row is kept while the verse still
    /// stands first in that psalm, which is what those passes ask.
    /// </summary>
    internal async Task<FramedText> Framed(Text text, VersificationRules rules, CancellationToken cancellationToken)
    {
        var decisions = new List<(Versification, PassageDecision)>();
        var (verses, existing, expected) = await Compute(text, rules, cancellationToken,
            (tradition, decision) => decisions.Add((tradition, decision)));

        var placed = expected.GroupBy(reference => reference.VerseId)
            .ToDictionary(group => group.Key, group => group.Select(reference => new VerseKey(
                reference.Book, reference.Chapter, reference.Verse)).ToList());
        var wanted = expected.ToHashSet();
        var titles = 0;
        foreach (var title in existing.Where(reference => !reference.IsPrimary
                                                          && reference.Verse == CanonicalReference.TitleVerse
                                                          && !wanted.Contains(reference)))
        {
            var addresses = placed[title.VerseId];
            if (addresses[0] == new VerseKey(title.Book, title.Chapter, 1))
            {
                addresses.Add(new VerseKey(title.Book, title.Chapter, CanonicalReference.TitleVerse));
                titles++;
            }
        }

        var rows = FrameFile.Rows(verses.Select(verse => (
            new VerseKey(verse.Book, verse.Chapter, verse.Number, verse.Label),
            (IReadOnlyList<VerseKey>)placed[verse.Id])));
        return new FramedText(verses, existing, rows, decisions, titles);
    }

    /// <summary>Writes the text's frame file, its header naming what it was made from and what decided it.</summary>
    internal static void Write(Text text, VersificationRules rules, ProductStore store, FramedText framed, string source)
    {
        var inputs = store.Inputs([
            "Versification/TVTMS.txt",
            .. VersificationData.Products.Select(product => store.Find(product)
                ?? throw new FileNotFoundException($"{product} is not under {store.ResourcesPath}.")),
            .. text.Slug == GeezTextSource.Slug ? ["Essenthos/versification/geez.tsv"] : Array.Empty<string>(),
        ]);
        var books = rules.Data.Books(text.Slug);

        using var writer = store.Create(FrameFile.Product(text.Slug), ProductKind.Machine, FrameRow.Schema,
            ProductHeader.DescribeProducer($"forge frame {text.Slug}"), inputs, header =>
            {
                header.Notes.Add(new("tradition", string.Join("; ",
                    [text.Versification.ToString().ToLowerInvariant(),
                     .. books.Select(book => $"{BookCodes.Code(book.Key) ?? book.Key.ToString()}={book.Value.ToString().ToLowerInvariant()}")])));
                foreach (var tradition in framed.Decisions.GroupBy(decision => decision.Tradition))
                {
                    var own = rules.SchemeNamedAfter(tradition.Key);
                    var passages = tradition.Where(decision => decision.Decision.Scheme != VersificationData.EditionReadingsProduct).ToList();
                    header.Notes.Add(new("schemes", $"{tradition.Key.ToString().ToLowerInvariant()}: " + string.Join(", ",
                        passages.GroupBy(decision => decision.Decision.Scheme)
                            .OrderByDescending(scheme => scheme.Count())
                            .Select(scheme => $"{scheme.Key} {scheme.Count()}")) + " passages"));
                    foreach (var (_, decision) in tradition.Where(decision => decision.Decision.Scheme == VersificationData.EditionReadingsProduct))
                    {
                        header.Notes.Add(new("readings", $"{decision.Passage}  {decision.Held[0]}"));
                    }

                    foreach (var (_, decision) in passages.Where(decision => decision.Decision.Scheme != own))
                    {
                        header.Notes.Add(new("tests", $"{decision.Passage}  {decision.Scheme}  " +
                            (decision.Held.Count > 0 ? Deciding(decision.Held) : "no test held: the scheme the tests contradict least")));
                    }
                }

                header.Notes.Add(new("verses", $"{framed.Verses.Count}, read from the database {source}"));
                if (framed.Titles > 0)
                {
                    header.Notes.Add(new("titles", $"{framed.Titles} psalm title verses a first verse also covers, as {source} holds them"));
                }

                header.Comments.Add("A verse not listed stands at its own address. own = book.chapter:verse[label] as the edition numbers it;");
                header.Comments.Add("through = the last verse of a run of consecutive verses with one shift (\\N for one verse);");
                header.Comments.Add("canonical = the frame addresses of own, the first primary, each one row further on for each verse of a run;");
                header.Comments.Add(FrameFile.Relations + ".");
                header.Comments.Add("schemes and tests: how many passages of the versification data each scheme placed, and the tests of");
                header.Comments.Add("each passage placed by another scheme than the tradition's own; readings: an edition's verses read one by one.");
            });
        writer.WriteAll(framed.Rows);
    }

    /// <summary>
    /// What the rows of a passage the edition answered have in common, which is what told the scheme apart:
    /// each row also asks that its own verse exists. All of them where they share nothing.
    /// </summary>
    private static string Deciding(IReadOnlyList<string> held)
    {
        var conditions = held.Select(tests => tests.Split(" & ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)).ToList();
        var shared = conditions.Skip(1).Aggregate(conditions[0].AsEnumerable(), (common, next) => common.Intersect(next)).ToList();
        return string.Join(" & ", shared.Count > 0 ? shared : conditions.SelectMany(row => row).Distinct());
    }

    /// <param name="lengths">Whether to count each verse's letters, which the rules read and a frame file does not.</param>
    private async Task<(List<PlacedVerse> Verses, List<ReferenceDraft> Existing)> Held(
        Text text,
        CancellationToken cancellationToken,
        bool lengths = true)
    {
        var held = db.Verses.Where(v => v.TextId == text.Id);
        var verses = lengths
            ? await held
                .Select(v => new PlacedVerse(
                    v.Id,
                    v.Book!.CanonicalOrdinal,
                    v.ChapterNumber,
                    v.Number,
                    v.Label,
                    v.Words.Sum(w => w.Surface.Length)))
                .ToListAsync(cancellationToken)
            : await held
                .Select(v => new PlacedVerse(v.Id, v.Book!.CanonicalOrdinal, v.ChapterNumber, v.Number, v.Label, 0))
                .ToListAsync(cancellationToken);

        var existing = await db.VerseReferences
            .Where(reference => reference.Verse!.TextId == text.Id)
            .Select(reference => new ReferenceDraft(
                reference.VerseId,
                reference.CanonicalBook,
                reference.CanonicalChapter,
                reference.CanonicalVerse,
                reference.IsPrimary))
            .ToListAsync(cancellationToken);
        return (verses, existing);
    }

    /// <summary>
    /// The text's verses, the placements the database holds for them, and where the frame puts them now.
    /// </summary>
    public async Task<(List<PlacedVerse> Verses, List<ReferenceDraft> Existing, List<ReferenceDraft> Expected)> Compute(
        Text text,
        VersificationRules rules,
        CancellationToken cancellationToken = default,
        Action<Versification, PassageDecision>? decided = null)
    {
        // A corpus imported before a new layer can gain a verse afterwards: a source note can sit
        // on a printed \v marker with no words (ASV Matthew 17:21). Do not make an existing frame
        // an all-or-nothing gate, or that new anchor can never reach the parallel reader. The
        // edition's shape still comes from every verse, because that is what decides its scheme.
        var (verses, existing) = await Held(text, cancellationToken);
        var expected = Expected(rules, text.Versification, verses, rules.Data.Books(text.Slug), Read(text.Slug), decided);
        return (verses, existing, expected);
    }

    /// <summary>
    /// Whether the text already stands where the frame puts it. A psalm's first verse that holds its
    /// title also covers the title's row, and that further row is written after the frame by the passes
    /// that read the title in (<see cref="PsalmOpeningLoader"/>, <see cref="SuperscriptionFrameLoader"/>);
    /// it is theirs, so it does not count as a difference, or every load would place those texts again
    /// only for the passes to write the same rows back.
    /// </summary>
    internal static bool Unchanged(IReadOnlyCollection<ReferenceDraft> existing, IReadOnlyCollection<ReferenceDraft> expected)
    {
        var wanted = expected.ToHashSet();
        var framed = existing
            .Where(reference => wanted.Contains(reference)
                                || reference.IsPrimary
                                || reference.Verse != CanonicalReference.TitleVerse)
            .ToHashSet();
        return framed.Count == wanted.Count && framed.SetEquals(wanted);
    }

    /// <summary>
    /// The verses of a text placed by a reading of which Greek verse each answers rather than by the number
    /// it prints: the Ge'ez in the chapters it divides otherwise than the Greek (<see cref="GeezVersification"/>).
    /// </summary>
    internal static IReadOnlyDictionary<(int Book, int Chapter, int Verse), IReadOnlyList<CanonicalReference>>? Read(
        string slug) =>
        slug == GeezTextSource.Slug ? GeezVersification.Placements : null;

    /// <summary>
    /// Where each verse of an edition stands, the first reference of a verse its primary place.
    /// Which scheme of its tradition the edition follows is a question only the edition can answer,
    /// and the versification data states the tests that ask it.
    /// </summary>
    /// <param name="books">Books the edition numbers in another tradition, placed by that tradition's rules.</param>
    /// <param name="read">
    /// Verses placed by a reading of where each answers the Greek, ahead of every scheme: at the rows
    /// listed, or with the verse before it in its chapter where the list is empty, because the Greek has
    /// nothing for it and its own number is some other verse's row.
    /// </param>
    public static List<ReferenceDraft> Expected(
        VersificationRules rules,
        Versification tradition,
        IReadOnlyList<PlacedVerse> verses,
        IReadOnlyDictionary<int, Versification>? books = null,
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), IReadOnlyList<CanonicalReference>>? read = null,
        Action<Versification, PassageDecision>? decided = null)
    {
        books ??= new Dictionary<int, Versification>();
        Versification TraditionOf(PlacedVerse verse) => books.GetValueOrDefault(verse.Book, tradition);

        // A book numbered in no tradition the data describes stands at its own numbers.
        var frames = verses
            .GroupBy(TraditionOf)
            .Where(group => rules.Covers(group.Key))
            .ToDictionary(group => group.Key, group => rules.Frame(group.Key, EditionShape.Of(group.Select(v =>
                (v.Book, v.Chapter, v.Number, v.Label, v.Length))), decided is null ? null : decision => decided(group.Key, decision)));

        // The addresses this text prints as lettered verses, which the frame resolves differently.
        var lettered = verses
            .Where(v => v.Label.Length > 0)
            .Select(v => new CanonicalReference(v.Book, v.Chapter, v.Number))
            .ToHashSet();

        IReadOnlyList<CanonicalReference> Framed(PlacedVerse verse) =>
            frames.TryGetValue(TraditionOf(verse), out var frame)
                ? frame.Resolve(
                    verse.Book,
                    verse.Chapter,
                    verse.Number,
                    lettered.Contains(new CanonicalReference(verse.Book, verse.Chapter, verse.Number)),
                    verse.Label)
                : [new CanonicalReference(verse.Book, verse.Chapter, verse.Number)];

        var placed = new Dictionary<int, IReadOnlyList<CanonicalReference>>(verses.Count);
        PlacedVerse? before = null;
        foreach (var verse in verses
                     .OrderBy(v => v.Book).ThenBy(v => v.Chapter).ThenBy(v => v.Number)
                     .ThenBy(v => v.Label, StringComparer.Ordinal))
        {
            var reading = verse.Label.Length == 0 && read is not null
                                                  && read.TryGetValue((verse.Book, verse.Chapter, verse.Number), out var rows)
                ? rows
                : null;
            placed[verse.Id] = reading switch
            {
                null => Framed(verse),
                [] when before is { } previous && previous.Book == verse.Book && previous.Chapter == verse.Chapter =>
                    [placed[previous.Id][0]],
                [] => Framed(verse),
                _ => reading,
            };
            before = verse;
        }

        return verses
            .SelectMany(verse => AtBothNames(verse.Book, placed[verse.Id])
                .Select((placement, index) => new ReferenceDraft(
                    verse.Id,
                    placement.Book,
                    placement.Chapter,
                    placement.Verse,
                    index == 0)))
            .ToList();
    }

    /// <summary>
    /// A verse of a passage printed under two names (<see cref="TwinPassages"/>) under the name its own
    /// edition prints it, and then under the other: the data places the Greek Letter of Jeremiah at the
    /// standard's Baruch 6 and the King James's Song of the Three in Daniel 3, and an edition stands in
    /// the book it prints the passage in and covers it under the other name. Every other verse stands
    /// where the frame puts it.
    /// </summary>
    private static IReadOnlyList<CanonicalReference> AtBothNames(int book, IReadOnlyList<CanonicalReference> placed)
    {
        static (int Book, int Chapter, int Verse)? Other(CanonicalReference place) =>
            TwinPassages.Other((place.Book, place.Chapter, place.Verse));

        if (!TwinPassages.Books.Contains(book) || !placed.Any(place => Other(place) is not null))
        {
            return placed;
        }

        var own = placed
            .Select(place => Other(place) is { } twin && place.Book != book && twin.Book == book
                ? new CanonicalReference(twin.Book, twin.Chapter, twin.Verse)
                : place)
            .ToList();
        var other = own
            .Select(place => Other(place) is { } twin
                ? new CanonicalReference(twin.Book, twin.Chapter, twin.Verse)
                : (CanonicalReference?)null)
            .OfType<CanonicalReference>();
        return [.. own.Concat(other).Distinct()];
    }
}
