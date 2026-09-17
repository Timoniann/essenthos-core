using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

internal sealed record StatedNumberOutcome(string Slug, bool AlreadyLoaded, int Verses, int Numbers, TimeSpan Elapsed)
{
    public override string ToString() => (AlreadyLoaded, Numbers) switch
    {
        (true, _) => $"{Slug} already states its own verse numbers",
        (_, 0) => $"{Slug} numbers its verses the way it is stored and states nothing further",
        _ => $"{Slug}: {Numbers} stated verse numbers over {Verses} verses in {Elapsed}",
    };
}

/// <summary>
/// Writes the addresses an edition prints for its own verses, for a text already loaded.
///
/// <para>
/// It is a pass of its own rather than part of <see cref="CorpusLoader"/> because the corpus loader
/// returns early for a text whose slug is already in the text table, and both texts this concerns
/// are loaded everywhere the corpus is. A filler guarded on its own rows reaches those databases on
/// the next start; a column filled by the corpus loader would only ever reach a corpus rebuilt from
/// nothing, and this is one table of a few thousand rows against a gigabyte of text.
/// </para>
///
/// <para>
/// Idempotent by those rows: a text that already has one is skipped, so this costs one indexed
/// existence check per text per boot. It takes the same <see cref="TextSource"/> the corpus loader
/// took, so the addresses are read by the same reader that read the words and the two cannot come
/// to disagree about which verse a marker belonged to.
/// </para>
/// </summary>
internal sealed class StatedNumberLoader(AppDbContext db, ILogger<StatedNumberLoader> logger)
{
    public async Task<StatedNumberOutcome> Load(TextSource source, CancellationToken cancellationToken = default)
    {
        var slug = source.Definition.Slug;
        var stated = source.Books
            .SelectMany(book => book.Chapters
                .SelectMany(chapter => chapter.Verses
                    .Where(verse => verse.Stated.Count > 0)
                    .Select(verse => (book.CanonicalOrdinal, chapter.Number, Verse: verse))))
            .ToList();

        if (stated.Count == 0)
        {
            return new StatedNumberOutcome(slug, AlreadyLoaded: false, 0, 0, TimeSpan.Zero);
        }

        var text = await db.Texts.FirstOrDefaultAsync(t => t.Slug == slug, cancellationToken);
        if (text is null)
        {
            throw new InvalidOperationException(
                $"The text \"{slug}\" states verse numbers of its own but is not loaded. Run this after the " +
                $"corpus loader, which is what writes the verses these numbers hang on.");
        }

        if (await db.StatedVerseNumbers.AnyAsync(n => n.Verse!.TextId == text.Id, cancellationToken))
        {
            logger.LogInformation("Text {Slug} already states its own verse numbers; nothing to do", slug);
            return new StatedNumberOutcome(slug, AlreadyLoaded: true, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var verses = await VerseIds(text.Id, cancellationToken);
        var written = 0;

        foreach (var (ordinal, chapter, draft) in stated)
        {
            if (!verses.TryGetValue((ordinal, chapter, draft.Number, draft.Label), out var verseId))
            {
                throw new InvalidOperationException(
                    $"{slug} states a verse number at book {ordinal} {chapter}:{draft.Number}{draft.Label}, and " +
                    "no verse of the loaded text sits there. The text and this pass were read from different " +
                    "files, or the text was loaded by a reader that numbers its verses otherwise.");
            }

            for (var position = 0; position < draft.Stated.Count; position++)
            {
                db.StatedVerseNumbers.Add(new StatedVerseNumber
                {
                    VerseId = verseId,
                    Position = position + 1,
                    ChapterNumber = draft.Stated[position].Chapter,
                    Number = draft.Stated[position].Number,
                });
                written++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var outcome = new StatedNumberOutcome(slug, AlreadyLoaded: false, stated.Count, written, started.Elapsed);
        logger.LogInformation("Loaded {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Every verse of the text by the address its own numbering gives it, which is the address the
    /// draft knows it by. The book is its canonical ordinal rather than its key, because the draft
    /// carries the ordinal and the two orders are not the same in every text.
    /// </summary>
    private async Task<Dictionary<(int Book, int Chapter, int Number, string Label), int>> VerseIds(
        int textId,
        CancellationToken cancellationToken)
    {
        var rows = await db.Verses
            .Where(v => v.TextId == textId)
            .Select(v => new { v.Id, Ordinal = v.Book!.CanonicalOrdinal, v.ChapterNumber, v.Number, v.Label })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => (r.Ordinal, r.ChapterNumber, r.Number, r.Label), r => r.Id);
    }
}
