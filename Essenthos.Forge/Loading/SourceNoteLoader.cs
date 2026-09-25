using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

internal sealed record SourceNoteOutcome(string Slug, bool AlreadyLoaded, int Verses, int Notes, TimeSpan Elapsed)
{
    public override string ToString() => AlreadyLoaded
        ? $"{Slug} already has its source notes"
        : $"{Slug}: {Notes} source notes over {Verses} verses in {Elapsed}";
}

/// <summary>
/// Adds the footnotes and cross-references an edition printed to a corpus already holding its
/// scripture. It is separate from <see cref="CorpusLoader"/> because an established corpus skips
/// whole texts on startup; an import hidden in that first load would never reach the data users
/// already have.
/// </summary>
internal sealed class SourceNoteLoader(AppDbContext db, ILogger<SourceNoteLoader> logger)
{
    public async Task<SourceNoteOutcome> Load(TextSource source, CancellationToken cancellationToken = default)
    {
        var notes = source.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses
                .Where(verse => verse.Notes.Count > 0)
                .Select(verse => (book.CanonicalOrdinal, chapter.Number, Verse: verse))))
            .ToList();

        if (notes.Count == 0)
        {
            return new SourceNoteOutcome(source.Definition.Slug, AlreadyLoaded: false, 0, 0, TimeSpan.Zero);
        }

        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == source.Definition.Slug, cancellationToken);
        if (text is null)
        {
            throw new InvalidOperationException(
                $"The text \"{source.Definition.Slug}\" carries source notes but is not loaded. Run this after " +
                "the corpus loader, which writes the verses notes hang on.");
        }

        // Guarded book by book, so the books a loaded text gains get their notes too.
        var annotated = (await db.VerseNotes
                .Where(note => note.Verse!.TextId == text.Id)
                .Select(note => note.Verse!.Book!.CanonicalOrdinal)
                .Distinct()
                .ToListAsync(cancellationToken))
            .ToHashSet();
        if (annotated.Count > 0)
        {
            var backfilled = await BackfillAnchors(text, notes, cancellationToken);
            notes = [.. notes.Where(note => !annotated.Contains(note.CanonicalOrdinal))];
            if (notes.Count == 0)
            {
                logger.LogInformation(
                    "Text {Slug} already has its source notes; {Anchors} source-marker anchors backfilled",
                    text.Slug,
                    backfilled);
                return new SourceNoteOutcome(text.Slug, AlreadyLoaded: true, 0, 0, TimeSpan.Zero);
            }
        }

        var started = Stopwatch.StartNew();
        var verses = await VerseIds(text.Id, cancellationToken);
        await AddNoteOnlyVerses(text, notes, verses, cancellationToken);
        var anchorWords = await AnchorWordIds(text.Id, cancellationToken);
        var written = 0;

        foreach (var (book, chapter, draft) in notes)
        {
            if (!verses.TryGetValue((book, chapter, draft.Number, draft.Label), out var verseId))
            {
                throw new InvalidOperationException(
                    $"{text.Slug} has a source note at book {book} {chapter}:{draft.Number}{draft.Label}, but no " +
                    "loaded verse sits there. The notes and scripture were read from different editions.");
            }

            for (var position = 0; position < draft.Notes.Count; position++)
            {
                var note = draft.Notes[position];
                db.VerseNotes.Add(new VerseNote
                {
                    VerseId = verseId,
                    Position = position + 1,
                    Kind = note.Kind,
                    Content = note.Content,
                    AnchorWordId = AnchorWordId(anchorWords, verseId, note.AnchorWordPosition),
                });
                written++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);
        var outcome = new SourceNoteOutcome(text.Slug, AlreadyLoaded: false, notes.Count, written, started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Source notes existed before their marker position was retained. The source pass runs on
    /// every startup, so it is also the safe, idempotent migration path for those already-loaded
    /// rows rather than a one-off corpus rewrite.
    /// </summary>
    private async Task<int> BackfillAnchors(
        Text text,
        List<(int CanonicalOrdinal, int Chapter, VerseDraft Verse)> notes,
        CancellationToken cancellationToken)
    {
        var positions = notes
            .SelectMany(entry => entry.Verse.Notes.Select((note, index) => new
            {
                entry.CanonicalOrdinal,
                entry.Chapter,
                entry.Verse.Number,
                entry.Verse.Label,
                Position = index + 1,
                note.AnchorWordPosition,
            }))
            .ToDictionary(
                entry => (entry.CanonicalOrdinal, entry.Chapter, entry.Number, entry.Label, entry.Position),
                entry => entry.AnchorWordPosition);
        var existing = await db.VerseNotes
            .Include(note => note.Verse)
            .ThenInclude(verse => verse!.Book)
            .Where(note => note.Verse!.TextId == text.Id && note.AnchorWordId == null)
            .ToListAsync(cancellationToken);
        if (existing.Count == 0)
        {
            return 0;
        }

        // Every word of the text, so it is loaded only once something needs an anchor.
        var words = await AnchorWordIds(text.Id, cancellationToken);
        var changed = 0;

        foreach (var note in existing)
        {
            var verse = note.Verse!;
            if (positions.TryGetValue(
                    (verse.Book!.CanonicalOrdinal, verse.ChapterNumber, verse.Number, verse.Label, note.Position),
                    out var position)
                && AnchorWordId(words, verse.Id, position) is { } wordId)
            {
                note.AnchorWordId = wordId;
                changed++;
            }
        }

        if (changed > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return changed;
    }

    private async Task<Dictionary<(int VerseId, int Position), long>> AnchorWordIds(
        int textId,
        CancellationToken cancellationToken) =>
        (await db.Words
            .Where(word => word.TextId == textId)
            .Select(word => new { word.VerseId, word.Position, word.Id })
            .ToListAsync(cancellationToken))
        .ToDictionary(word => (word.VerseId, word.Position), word => word.Id);

    private static long? AnchorWordId(
        IReadOnlyDictionary<(int VerseId, int Position), long> words,
        int verseId,
        int? position) => position is { } value && words.TryGetValue((verseId, value), out var wordId)
        ? wordId
        : null;

    private async Task<Dictionary<(int Book, int Chapter, int Number, string Label), int>> VerseIds(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Verses
            .Where(verse => verse.TextId == textId)
            .Select(verse => new
            {
                verse.Id,
                Ordinal = verse.Book!.CanonicalOrdinal,
                verse.ChapterNumber,
                verse.Number,
                verse.Label,
            })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(row => (row.Ordinal, row.ChapterNumber, row.Number, row.Label), row => row.Id);
    }

    /// <summary>
    /// Older databases quite properly omitted a <c>\v</c> marker carrying no scripture. Some
    /// editions nevertheless put their explanation of that absence there — ASV's Matthew 17:21
    /// is exactly that. The note needs an anchor, so this upgrade creates the otherwise empty
    /// verse slot from the same source draft, without reloading or rewriting the text's words.
    /// </summary>
    private async Task AddNoteOnlyVerses(
        Text text,
        List<(int CanonicalOrdinal, int Chapter, VerseDraft Verse)> notes,
        Dictionary<(int Book, int Chapter, int Number, string Label), int> verses,
        CancellationToken cancellationToken)
    {
        var missing = notes
            .Where(note => !verses.ContainsKey((
                note.CanonicalOrdinal, note.Chapter, note.Verse.Number, note.Verse.Label)))
            .ToList();
        if (missing.Count == 0)
        {
            return;
        }

        var chapters = await db.Chapters
            .Where(chapter => chapter.TextId == text.Id)
            .Select(chapter => new
            {
                chapter.Id,
                chapter.BookId,
                CanonicalOrdinal = chapter.Book!.CanonicalOrdinal,
                chapter.Number,
            })
            .ToListAsync(cancellationToken);
        var locations = chapters.ToDictionary(
            chapter => (chapter.CanonicalOrdinal, chapter.Number),
            chapter => (chapter.BookId, chapter.Id));

        var added = new List<(Verse Verse, (int Book, int Chapter, int Number, string Label) Address)>();
        foreach (var missingVerse in missing)
        {
            if (!locations.TryGetValue((missingVerse.CanonicalOrdinal, missingVerse.Chapter), out var location))
            {
                throw new InvalidOperationException(
                    $"{text.Slug} has a source note in book {missingVerse.CanonicalOrdinal} chapter " +
                    $"{missingVerse.Chapter}, but the loaded text has no such chapter.");
            }

            var verse = new Verse
            {
                TextId = text.Id,
                BookId = location.BookId,
                ChapterId = location.Id,
                ChapterNumber = missingVerse.Chapter,
                Number = missingVerse.Verse.Number,
                Label = missingVerse.Verse.Label,
            };
            db.Verses.Add(verse);
            added.Add((
                verse,
                (missingVerse.CanonicalOrdinal, missingVerse.Chapter, verse.Number, verse.Label)));
        }

        await db.SaveChangesAsync(cancellationToken);
        foreach (var (verse, address) in added)
        {
            verses[address] = verse.Id;
            await db.Database.ExecuteSqlInterpolatedAsync(SlotByNumber(verse.Id), cancellationToken);
        }
        logger.LogInformation(
            "{Slug}: added {Verses} note-only verse slots missing from the earlier corpus load",
            text.Slug, missing.Count);
    }

    /// <summary>
    /// A slot added after the load has no place in the edition's order of its own, so it takes the
    /// one its number gives it: straight after the verse numbered before it, with every verse the
    /// edition writes later moved one along.
    /// </summary>
    private static FormattableString SlotByNumber(int verseId) => $"""
        WITH slot AS (
            SELECT v.id, v.chapter_id,
                   coalesce((SELECT max(o.sequence) FROM verse o
                             WHERE o.chapter_id = v.chapter_id AND o.id <> v.id
                               AND (o.number, o.label) < (v.number, v.label)), 0) + 1 AS sequence
            FROM verse v WHERE v.id = {verseId}),
        moved AS (
            UPDATE verse o SET sequence = o.sequence + 1
            FROM slot
            WHERE o.chapter_id = slot.chapter_id AND o.id <> slot.id AND o.sequence >= slot.sequence)
        UPDATE verse v SET sequence = slot.sequence FROM slot WHERE v.id = slot.id
        """;
}
