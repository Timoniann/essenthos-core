using Essenthos.Core.Corpus;
﻿using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <param name="Alignment">
/// How this pane's verse was paired with the row. Every pairing goes through the shared frame, so it
/// is <c>original-verse</c> — the contract's name for "a recorded mapping said so" rather than "the
/// numbers matched" — wherever the frame's rows are where the two texts' verses answer each other.
/// It is <c>verse-number</c> for a pane whose verses here answer the reference pane's only by a
/// reading of where each falls: a book divided as no scheme describes stands in the frame at the
/// numbers it prints, so there the row does pair by number, and the reader is told so.
/// </param>
/// <param name="Reference">
/// What this text itself calls the verse, which can differ from the row: canonical Joel 3:1 is
/// Joel 4:1 in BHSA, and a reader comparing them should see both numbers.
/// </param>
/// <param name="Verses">
/// Which of this text's own verses the row holds, as the text numbers them — <c>50</c>, <c>50a</c>.
///
/// Usually one, and then it says nothing new. Where a text divides a passage more finely than the
/// shared frame does it is more than one, and saying so is the difference between a row a reader can
/// trust and a silent merge: Brenton's Genesis 31:50 and 31:50a both belong at canonical 31:50, so
/// the row carried 39 Greek words against 19 Hebrew with nothing to say why. 91 addresses across the
/// Septuagint are like that.
/// </param>
/// <param name="StatedVerses">
/// What the edition itself prints as this verse's address, where its own file says so and that is
/// not the address it is stored under — the Synodal's <c>118:1</c> at Psalm 119:1.
///
/// Usually empty, and empty is silence rather than agreement. bible4u renumbered the Synodal and
/// Ohienko's Ukrainian to the numbering the King James follows, which is what lets either of them
/// be laid beside anything, and then printed the edition's own address in the verse wherever the
/// two disagree: 2,676 verses of the Synodal and 1,928 of the Ukrainian, 2,414 and 1,013 of them in
/// the Psalms. Everywhere else the file states nothing, because there the two numberings are the
/// same — but that inference belongs to whoever wants to draw it, so nothing is written here for a
/// verse the edition said nothing about.
///
/// More than one where the edition divides what this corpus holds as one verse: the Synodal counts
/// Psalm 12's superscription as its own verse, so our 12:1 is its 11:1 and 11:2 both.
///
/// A chapter the edition numbers with a letter keeps it: Swete's Song of the Vineyard is Ode
/// <c>4a</c> on his page and Ode 10 in the corpus, so its verses are stated as <c>4a:1</c> and on.
///
/// It is not <see cref="Verses"/>, and the difference matters: those are verses this text actually
/// holds, and can be asked for. An address here is a statement about a printed page, and no verse
/// of this corpus answers to it.
/// </param>
/// <param name="Strength">
/// How strongly this verse is linked to the same row of the reference pane, or null on the
/// reference pane itself and on a text nothing links to it.
/// </param>
/// <param name="Notes">The source notes printed beside this row's verses, in source order.</param>
internal record ParallelCellResponse(
    IList<TextWordResponse> Words,
    string Alignment,
    VerseRefResponse? Reference,
    IList<string> Verses,
    IList<string> StatedVerses,
    IList<SourceNoteResponse> Notes,
    LinkStrengthResponse? Strength);

/// <param name="Links">
/// How many links join the two verses. A mean over one or two of them says nothing; the
/// verification pass reads no verse's strength under three, and a client showing this should be at
/// least as careful.
/// </param>
/// <param name="Stated">
/// Of those, how many a source or a person asserted. Those carry no number at all — that is what
/// the schema means by a stated link, and defaulting them to 1 would make testimony and a
/// confident guess indistinguishable.
/// </param>
/// <param name="Confidence">
/// The mean confidence of the links that have one, and null where none of them does. A verse pair
/// this is low on is either laid against the wrong verse or a place the two traditions genuinely
/// say different things, and the corpus cannot tell the reader which — but it can say the words on
/// the two sides answer each other faintly, which is the fact a split view exists to show. Of the
/// 154 verse pairs the verification pass calls suspect between Brenton and BHSA, 137 are paired
/// correctly and faint because the Septuagint is translating freely.
/// </param>
internal record LinkStrengthResponse(int Links, int Stated, double? Confidence);

internal record ParallelVerseResponse(int Number, Dictionary<string, ParallelCellResponse?> Texts);

internal record ParallelTextResponse(
    BookRefResponse Book,
    int Chapter,
    int ChapterCount,
    string? ReferenceCorpus,
    IList<CorpusResponse> Corpora,
    IList<ParallelVerseResponse> Verses);

internal static class ParallelEndpoints
{
    private const string PairedThroughTheFrame = "original-verse";

    /// <summary>
    /// A pane whose verses this chapter answers the reference pane's only by a reading of where each
    /// falls — the Ge'ez Psalter, Job, Song and Esther, which divide their verses as no versification
    /// scheme does: the frame lays them at the numbers they print, so a row pairs them by that number.
    /// </summary>
    private const string PairedByItsOwnNumber = "verse-number";

    /// <summary>The contract's separator for the corpus list, and the cap it sets.</summary>
    private const char CorpusSeparator = ',';

    private const int MostCorporaAtOnce = 6;

    public static void MapParallel(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/parallel/{book}/{chapter:int}", async (
            string book,
            int chapter,
            string? corpora,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var ordinal = BookReferences.ResolveOrdinal(book);
            if (ordinal is null)
            {
                return ApiResults.NotFound(BookReferences.FormatHint(book));
            }

            var requested = await Requested(canon, corpora, cancellationToken);
            if (requested.Count == 0)
            {
                return ApiResults.Malformed(
                    "None of the requested texts exist. Ask /v1/corpora for the ones this corpus holds, and name " +
                    $"them in the corpora query as a {CorpusSeparator}-separated list.");
            }

            if (requested.Count > MostCorporaAtOnce)
            {
                return ApiResults.Malformed(
                    $"At most {MostCorporaAtOnce} texts can be read side by side; {requested.Count} were asked for.");
            }

            // When none of the texts holds the book, every pane says so, and the shared frame's count
            // is still the one that lets the reader step through it.
            var chapterCount = await canon.ChapterCountAcross(
                requested.Select(entry => entry.Id), ordinal.Value, cancellationToken);
            if (chapterCount == 0)
            {
                chapterCount = await canon.ChapterCount(ordinal.Value, cancellationToken);
            }

            if (chapter < 1 || chapter > chapterCount)
            {
                return ApiResults.NotFound(
                    $"{BookReferences.Name(ordinal.Value)} has {chapterCount} chapters in the texts asked for, so " +
                    $"there is no chapter {chapter}. That is the shared numbering; a text of its own may divide " +
                    "the book differently, and another text may reach further.");
            }

            var byText = new Dictionary<string, Dictionary<int, List<TextWordResponse>>>();
            var references = new Dictionary<string, Dictionary<int, VerseRefResponse>>();
            var own = new Dictionary<string, Dictionary<int, List<string>>>();
            var stated = new Dictionary<string, Dictionary<int, List<string>>>();
            var notes = new Dictionary<string, Dictionary<int, List<SourceNoteResponse>>>();
            var strength = new Dictionary<string, Dictionary<int, LinkStrengthResponse>>();
            var byNumber = new HashSet<string>();
            var reference = requested[0];
            foreach (var entry in requested)
            {
                byText[entry.Slug] = [];
                references[entry.Slug] = [];
                own[entry.Slug] = [];
                stated[entry.Slug] = [];
                notes[entry.Slug] = [];
                strength[entry.Slug] = [];
                foreach (var (heldBook, heldChapter, first, last, shift) in Held(ordinal.Value, chapter))
                {
                    Merge(byText[entry.Slug], await Texts.ReadByCanonicalVerse(
                        db, entry.Id, heldBook, heldChapter, cancellationToken), first, last, shift);
                    Merge(references[entry.Slug],
                        await OwnReferences(db, entry.Id, heldBook, heldChapter, cancellationToken), first, last, shift);
                    Merge(own[entry.Slug],
                        await OwnVerses(db, entry.Id, heldBook, heldChapter, cancellationToken), first, last, shift);
                    Merge(stated[entry.Slug],
                        await StatedVerses(db, entry.Id, heldBook, heldChapter, cancellationToken), first, last, shift);
                    Merge(notes[entry.Slug],
                        await SourceNotes(db, entry.Id, heldBook, heldChapter, cancellationToken), first, last, shift);
                    if (entry.Id != reference.Id)
                    {
                        Merge(strength[entry.Slug], await Strengths(
                            db, entry.Id, reference.Id, heldBook, heldChapter, cancellationToken), first, last, shift);
                        if (await JoinedOnlyByReading(db, entry.Id, reference.Id, heldBook, heldChapter, cancellationToken))
                        {
                            byNumber.Add(entry.Slug);
                        }
                    }
                }
            }

            var numbers = byText.Values
                .SelectMany(verses => verses.Keys)
                .Concat(notes.Values.SelectMany(notesByVerse => notesByVerse.Keys))
                .Distinct()
                .OrderBy(number => number)
                .ToList();

            var rows = numbers
                .Select(number => new ParallelVerseResponse(
                    number,
                    requested.ToDictionary(
                        entry => entry.Slug,
                        entry => Cell(
                            byText[entry.Slug], references[entry.Slug], own[entry.Slug],
                            stated[entry.Slug], notes[entry.Slug], strength[entry.Slug], number,
                            byNumber.Contains(entry.Slug) ? PairedByItsOwnNumber : PairedThroughTheFrame))))
                .ToList();

            var corpusRows = await CorpusRows(db, canon, requested, cancellationToken);

            return Results.Ok(new ParallelTextResponse(
                new BookRefResponse(ordinal.Value, BookReferences.Name(ordinal.Value),
                    BookReferences.Slug(ordinal.Value)),
                chapter,
                chapterCount,
                requested[0].Slug,
                corpusRows,
                rows));
        });
    }

    /// <summary>
    /// The chapters a chapter's rows are read from: itself, and each chapter holding a passage of it
    /// under another name (<see cref="TwinPassages"/>) — the Letter of Jeremiah, which is Baruch 6 in the
    /// Latin and English Bibles and a book of its own in the Greek, and the Song of the Three, which is
    /// inside Daniel 3 in the Greek and Latin and a book of its own in the King James. A text is read
    /// under whichever name it prints the passage, with the verses of that passage and at the rows they
    /// have here, so either name opens it in every text that has it.
    /// </summary>
    internal static IEnumerable<(int Book, int Chapter, int First, int Last, int Shift)> Held(int book, int chapter) =>
        [(book, chapter, int.MinValue, int.MaxValue, 0), .. TwinPassages.NamedElsewhere(book, chapter)];

    /// <summary>
    /// Rows read under another name, at the rows they have here; a row the chapter already holds for the
    /// text keeps what it has, since a verse stands primarily under one name only.
    /// </summary>
    internal static void Merge<T>(Dictionary<int, T> into, Dictionary<int, T> read, int first, int last, int shift)
    {
        foreach (var (verse, value) in read)
        {
            if (verse >= first && verse <= last)
            {
                into.TryAdd(verse + shift, value);
            }
        }
    }

    /// <summary>
    /// A text with no verse at this address answers null, which is a different fact from a verse
    /// with no words: the first is "this text does not go here", the second would be a defect.
    /// </summary>
    private static ParallelCellResponse? Cell(
        Dictionary<int, List<TextWordResponse>> verses,
        Dictionary<int, VerseRefResponse> references,
        Dictionary<int, List<string>> own,
        Dictionary<int, List<string>> stated,
        Dictionary<int, List<SourceNoteResponse>> notes,
        Dictionary<int, LinkStrengthResponse> strength,
        int number,
        string alignment)
    {
        var hasWords = verses.TryGetValue(number, out var words);
        var hasNotes = notes.TryGetValue(number, out var sourceNotes);
        if (!hasWords && !hasNotes)
        {
            return null;
        }

        return new ParallelCellResponse(
            words ?? [],
            alignment,
            references.GetValueOrDefault(number),
            own.GetValueOrDefault(number) ?? [],
            stated.GetValueOrDefault(number) ?? [],
            sourceNotes ?? [],
            strength.GetValueOrDefault(number));
    }

    /// <summary>
    /// How strongly each of this text's verses answers the reference pane's verse at the same
    /// address, which is the one thing about a verse pair that only the corpus knows.
    ///
    /// <para>
    /// The verification pass computes exactly this number and uses it to decide a verse was laid
    /// against the wrong verse. That is one of two things a faint pair can mean, and the other is
    /// the more interesting: the two traditions say different things here. The corpus cannot tell
    /// them apart, and a reader looking at both panes can — but only if the number reaches them,
    /// and until now it reached nobody outside a verification run.
    /// </para>
    ///
    /// <para>
    /// Against the reference pane alone rather than every pair of panes. Six corpora make fifteen
    /// pairs and fourteen of them are not what the reader is comparing; the response already names
    /// the reference corpus, and this is read against it.
    /// </para>
    ///
    /// <para>
    /// Links are counted once however many words of this text they name, and read in either
    /// direction, because which text a link is stored as being from is a fact about the loader
    /// rather than about the two verses.
    /// </para>
    /// </summary>
    public static async Task<Dictionary<int, LinkStrengthResponse>> Strengths(
        AppDbContext db,
        int textId,
        int againstId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.LinkWords
            .Where(side => side.Word!.Verse!.References.Any(r => r.IsPrimary
                                                                 && r.CanonicalBook == canonicalBook
                                                                 && r.CanonicalChapter == canonicalChapter)
                           && ((side.Side == LinkSide.From
                                && side.Link!.FromTextId == textId
                                && side.Link.ToTextId == againstId)
                               || (side.Side == LinkSide.To
                                   && side.Link!.ToTextId == textId
                                   && side.Link.FromTextId == againstId)))
            .Select(side => new
            {
                Canonical = side.Word!.Verse!.References.First(r => r.IsPrimary).CanonicalVerse,
                side.LinkId,
                side.Link!.Confidence,
            })
            .Distinct()
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Canonical)
            .ToDictionary(
                group => group.Key,
                group => Strength([.. group.Select(row => row.Confidence)]));
    }

    /// <summary>
    /// Whether this text's verses in the chapter are joined to the reference's verses only by verse
    /// links a reading drew, and by none the frame or a source states. Then the rows lay the two side
    /// by side by the numbers each prints, and the reading — which may put a verse a row or two away —
    /// is the only statement of which verse answers which.
    /// </summary>
    internal static async Task<bool> JoinedOnlyByReading(
        AppDbContext db,
        int textId,
        int againstId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var methods = await db.VerseLinks
            .Where(link => ((link.FromTextId == textId && link.ToTextId == againstId)
                            || (link.FromTextId == againstId && link.ToTextId == textId))
                           && link.Verses.Any(side => side.Verse!.TextId == textId
                                                      && side.Verse.References.Any(r => r.IsPrimary
                                                          && r.CanonicalBook == canonicalBook
                                                          && r.CanonicalChapter == canonicalChapter)))
            .Select(link => link.Method)
            .Distinct()
            .ToListAsync(cancellationToken);
        return methods.Count > 0 && methods.All(method => method == LinkMethod.ModelReading);
    }

    /// <summary>
    /// A stated link carries no confidence and is not averaged as though it did — <c>Average</c>
    /// over nullable doubles skips the nulls and answers null when they are all there is, which is
    /// the reading wanted here: nothing inferred this pair, so there is no inference to report.
    /// </summary>
    private static LinkStrengthResponse Strength(IReadOnlyList<double?> confidences) =>
        new(confidences.Count, confidences.Count(c => c is null), confidences.Average());

    /// <summary>
    /// Which of a text's own verses sit at each canonical address in this chapter, as the text
    /// numbers them.
    ///
    /// <see cref="OwnReferences"/> answers the same question and keeps only one per address, because
    /// a reference is a single address and cannot be two. That is right for what it is for and it is
    /// why the label went missing: Brenton's <c>31:50a</c> and <c>31:50</c> both belong at canonical
    /// 31:50, so one of the two was reported and the row silently held both.
    /// </summary>
    private static async Task<Dictionary<int, List<string>>> OwnVerses(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.Verse!.TextId == textId
                        && r.CanonicalBook == canonicalBook
                        && r.CanonicalChapter == canonicalChapter)
            .Select(r => new { r.CanonicalVerse, r.Verse!.ChapterNumber, r.Verse.Number, r.Verse.Label })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.CanonicalVerse)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(r => r.Number).ThenBy(r => r.Label, StringComparer.Ordinal)
                    .Select(r => $"{r.ChapterNumber}:{r.Number}{r.Label}")
                    .ToList());
    }

    /// <summary>
    /// What the edition prints as its own address for each canonical address in this chapter.
    ///
    /// <para>
    /// It sits beside <see cref="OwnVerses"/> and answers a different question. That one asks which
    /// verses this text holds here, and every answer is a row that can be fetched; this one asks
    /// what the edition's own pages call them, and the answer is a claim about a printed book that
    /// no row of this corpus answers to. Reading them as one field is how the Synodal would come to
    /// offer 118:1 as a verse it has.
    /// </para>
    ///
    /// <para>
    /// The addresses of a verse are ordered as the edition printed them, and where two of its
    /// verses became one of ours both are here — the second is not a duplicate of the first and
    /// dropping it would say the edition divides the passage as we do.
    /// </para>
    /// </summary>
    public static async Task<Dictionary<int, List<string>>> StatedVerses(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.StatedVerseNumbers
            .Where(n => n.Verse!.TextId == textId
                        && n.Verse.References.Any(r => r.IsPrimary
                                                       && r.CanonicalBook == canonicalBook
                                                       && r.CanonicalChapter == canonicalChapter))
            .Select(n => new
            {
                Canonical = n.Verse!.References.First(r => r.IsPrimary).CanonicalVerse,
                Holder = n.Verse.Number,
                n.Position,
                Chapter = n.ChapterNumber,
                n.ChapterLabel,
                Verse = n.Number,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Canonical)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(row => row.Holder).ThenBy(row => row.Position)
                    .Select(row => $"{row.Chapter}{row.ChapterLabel}:{row.Verse}")
            .ToList());
    }

    /// <summary>
    /// The edition's notes, grouped by the canonical row rather than by their printed number. A
    /// row can hold more than one of a translation's own verses, so the order keeps the verse they
    /// belong to before their position inside it.
    /// </summary>
    private static async Task<Dictionary<int, List<SourceNoteResponse>>> SourceNotes(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseNotes
            .Where(note => note.Verse!.TextId == textId
                           && note.Verse.References.Any(reference => reference.IsPrimary
                                                                    && reference.CanonicalBook == canonicalBook
                                                                    && reference.CanonicalChapter == canonicalChapter))
            .Select(note => new
            {
                Canonical = note.Verse!.References.First(reference => reference.IsPrimary).CanonicalVerse,
                Holder = note.Verse.Number,
                note.Verse.Label,
                note.Position,
                note.Kind,
                note.Content,
                note.AnchorWordId,
                Anchor = note.AnchorWord == null ? null : note.AnchorWord.Surface,
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.Canonical)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(row => row.Holder).ThenBy(row => row.Label, StringComparer.Ordinal)
                    .ThenBy(row => row.Position)
                    .Select(row => new SourceNoteResponse(
                        EnumSpelling.Of(row.Kind), row.Content, row.AnchorWordId, row.Anchor))
                    .ToList());
    }

    private static async Task<List<TextEntry>> Requested(
        ICanonIndex canon,
        string? corpora,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(corpora))
        {
            return (await canon.Texts(cancellationToken)).Take(2).ToList();
        }

        var found = new List<TextEntry>();
        foreach (var slug in corpora.Split(CorpusSeparator, StringSplitOptions.RemoveEmptyEntries |
                                                            StringSplitOptions.TrimEntries))
        {
            if (await canon.Text(slug, cancellationToken) is { } entry && found.All(f => f.Id != entry.Id))
            {
                found.Add(entry);
            }
        }

        return found;
    }

    /// <summary>What each text calls the verses it puts at these canonical addresses.</summary>
    private static async Task<Dictionary<int, VerseRefResponse>> OwnReferences(
        AppDbContext db,
        int textId,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary
                        && r.Verse!.TextId == textId
                        && r.CanonicalBook == canonicalBook
                        && r.CanonicalChapter == canonicalChapter)
            .Select(r => new { r.CanonicalVerse, r.Verse!.ChapterNumber, r.Verse.Number })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => r.CanonicalVerse)
            .ToDictionary(
                group => group.Key,
                group => new VerseRefResponse(
                    canonicalBook,
                    BookReferences.Name(canonicalBook),
                    BookReferences.Slug(canonicalBook),
                    group.Min(r => r.ChapterNumber),
                    group.Min(r => r.Number)));
    }

    private static async Task<IList<CorpusResponse>> CorpusRows(
        AppDbContext db,
        ICanonIndex canon,
        List<TextEntry> requested,
        CancellationToken cancellationToken)
    {
        var ids = requested.Select(e => e.Id).ToList();
        var texts = await db.Texts.Where(t => ids.Contains(t.Id)).ToListAsync(cancellationToken);

        return requested
            .Select(entry => Texts.Corpus(
                texts.Single(t => t.Id == entry.Id),
                new CoverageResponse(entry.FirstBook, entry.LastBook, entry.Books),
                entry.HasWordMapping))
            .ToList();
    }
}
