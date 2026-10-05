using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Swete;
using Essenthos.Core.Usfm;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

internal sealed record BoundaryPair(string From, string To);
internal sealed record EditionBoundaryOutcome(int RemovedWords, int MovedWords, IReadOnlyList<string> Texts,
    IReadOnlyList<BoundaryPair> Pairs)
{
    public override string ToString() => $"Edition boundaries: {RemovedWords} editorial words removed, "
        + $"{MovedWords} words moved with their rows; texts [{string.Join(", ", Texts)}]; "
        + $"affected pairs [{string.Join(", ", Pairs.Select(p => p.From + "→" + p.To))}]";
}

internal sealed class EditionBoundaryRepairLoader(AppDbContext db)
{
    public async Task<EditionBoundaryOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var beforePairs = await Pairs(new HashSet<string> { Sources.KingJamesSlug, SweteTextSource.Slug }, cancellationToken);
        var changed = new HashSet<string>();
        var removed = 0;
        var moved = 0;
        var kjv = await db.Texts.SingleOrDefaultAsync(t => t.Slug == Sources.KingJamesSlug, cancellationToken);
        if (kjv is not null)
        {
            var folder = Path.Combine(resources, DeuterocanonTextSource.KingJamesFolder);
            foreach (var (code, ordinal) in new[] { ("BAR", 67), ("SIR", 72), ("BEL", 78) })
            {
                var file = Directory.GetFiles(folder, "*.usfm").Single(path =>
                    File.ReadAllText(path).StartsWith($"\\id {code}", StringComparison.Ordinal));
                var content = File.ReadAllText(file);
                var before = UsfmReader.Read(content);
                var after = UsfmReader.Read(content, editorialHeadings: true);
                foreach (var chapter in before.Chapters)
                foreach (var verse in chapter.Verses)
                {
                    var wanted = after.Chapters.Single(c => c.Number == chapter.Number).Verses
                        .Single(v => v.Number == verse.Number && v.Label == verse.Label);
                    var oldTokens = verse.Words.Select(w => w.Surface).ToArray();
                    var newTokens = wanted.Words.Select(w => w.Surface).ToArray();
                    var count = oldTokens.Length - newTokens.Length;
                    if (count == 0) continue;
                    var keep = new HashSet<int>();
                    var cursor = oldTokens.Length - 1;
                    for (var index = newTokens.Length - 1; index >= 0; index--)
                    {
                        while (cursor >= 0 && oldTokens[cursor] != newTokens[index]) cursor--;
                        if (cursor < 0)
                            throw new InvalidOperationException($"{code} {chapter.Number}:{verse.Number}: the heading-free text is not contained in the known headed text; nothing changed.");
                        keep.Add(cursor--);
                    }
                    var storedVerse = await db.Verses.SingleOrDefaultAsync(v => v.TextId == kjv.Id
                        && v.Book!.CanonicalOrdinal == ordinal && v.ChapterNumber == chapter.Number
                        && v.Number == verse.Number && v.Label == verse.Label, cancellationToken);
                    if (storedVerse is null) continue;
                    var words = await Stored(storedVerse.Id, cancellationToken);
                    if (Matches(words, wanted.Words.Select(w => (w.Surface, w.Trailer)))) continue;
                    if (!Matches(words, verse.Words.Select(w => (w.Surface, w.Trailer))))
                        throw new InvalidOperationException($"KJV {ordinal}:{chapter.Number}:{verse.Number} is neither the known headed nor corrected verse; nothing changed.");
                    var ids = words.Where((_, index) => !keep.Contains(index)).Select(w => w.Id).ToArray();
                    await GuardRemoved(ids, cancellationToken);
                    await db.Links.Where(l => l.Words.Any(w => ids.Contains(w.WordId)))
                        .ExecuteDeleteAsync(cancellationToken);
                    await db.Words.Where(w => ids.Contains(w.Id)).ExecuteDeleteAsync(cancellationToken);
                    await db.Words.Where(w => w.VerseId == storedVerse.Id)
                        .ExecuteUpdateAsync(setters => setters.SetProperty(w => w.Position, w => -w.Position), cancellationToken);
                    await db.Database.ExecuteSqlRawAsync(
                        "UPDATE word w SET position = p.position FROM "
                        + "(SELECT id, row_number() OVER (ORDER BY position DESC)::integer AS position FROM word WHERE verse_id = {0}) p "
                        + "WHERE w.id = p.id", [storedVerse.Id], cancellationToken);
                    if (!Matches(await Stored(storedVerse.Id, cancellationToken), wanted.Words.Select(w => (w.Surface, w.Trailer))))
                        throw new InvalidOperationException("The heading removal did not reconstruct the printed verse; nothing changed.");
                    removed += count;
                    changed.Add(kjv.Slug);
                }
            }
        }

        var swete = await db.Texts.SingleOrDefaultAsync(t => t.Slug == SweteTextSource.Slug, cancellationToken);
        if (swete is not null)
        {
            var previous = await db.Verses.SingleOrDefaultAsync(v => v.TextId == swete.Id
                && v.Book!.CanonicalOrdinal == 10 && v.ChapterNumber == 19 && v.Number == 42 && v.Label == "", cancellationToken);
            if (previous is not null)
            {
                var source = SweteTextSource.Read(Path.Combine(resources, "Swete"), chapterMarkers: false);
                var chapter = source.Books.Single(b => b.CanonicalOrdinal == 10).Chapters.Single(c => c.Number == 19);
                var before = chapter.Verses.Single(v => v.Number == 42);
                var after = chapter.Verses.Single(v => v.Number == 43);
                var raw = SweteReader.Read(File.ReadLines(Path.Combine(resources, "Swete", "12.Regnorum_II.txt")))
                    .Chapters.Single(c => c.Number == 19).Verses.Single(v => v.Number == 42).Words;
                var historical = SweteReader.Read(SweteRestorations.Apply("12.Regnorum_II",
                        File.ReadLines(Path.Combine(resources, "Swete", "12.Regnorum_II.txt")),
                        [.. SweteCorrections.All.Where(c => c.Book == "12.Regnorum_II" && c.Chapter == 19 && c.Verse == 42)]))
                    .Chapters.Single(c => c.Number == 19).Verses.Single(v => v.Number == 42).Words;
                if (raw.Count != before.Words.Count + after.Words.Count)
                    throw new InvalidOperationException("The known raw and corrected Swete boundary differ in word count; nothing changed.");
                if (after.Words[^1].Surface != "XX" || raw[^1].Surface != "XX" || historical[^1].Surface != "XX")
                    throw new InvalidOperationException("The known Swete chapter marker is absent from its source boundary; nothing changed.");
                bool Known(IEnumerable<Word> actual) => Matches(actual, before.Words.Concat(after.Words)
                    .Select(w => (w.Surface, w.Trailer))) || Matches(actual, raw.Select(w => (w.Surface, w.Trailer)))
                    || Matches(actual, historical.Select(w => (w.Surface, w.Trailer)))
                    || Matches(actual, before.Words.Concat(after.Words.SkipLast(1)).Select(w => (w.Surface, w.Trailer)))
                    || Matches(actual, raw.SkipLast(1).Select(w => (w.Surface, w.Trailer)))
                    || Matches(actual, historical.SkipLast(1).Select(w => (w.Surface, w.Trailer)));
                var words = await Stored(previous.Id, cancellationToken);
                var next = await db.Verses.SingleOrDefaultAsync(v => v.TextId == swete.Id
                    && v.BookId == previous.BookId && v.ChapterNumber == 19 && v.Number == 43 && v.Label == "", cancellationToken);
                if (next is not null)
                {
                    if (words.Count != before.Words.Count
                        || !Known(words.Concat(await Stored(next.Id, cancellationToken))))
                        throw new InvalidOperationException("SWETE 2 Samuel 19:42–43 does not match the known split text; nothing changed.");
                }
                else
                {
                    if (!Known(words))
                        throw new InvalidOperationException("SWETE 2 Samuel 19:42 is not the known combined verse; nothing changed.");
                    next = new Verse { TextId = swete.Id, BookId = previous.BookId, ChapterId = previous.ChapterId,
                        ChapterNumber = 19, Number = 43, Sequence = previous.Sequence + 1 };
                    db.Verses.Add(next);
                    await db.SaveChangesAsync(cancellationToken);
                    var tail = words.Skip(before.Words.Count).Select(w => w.Id).ToArray();
                    await db.Words.Where(w => tail.Contains(w.Id)).ExecuteUpdateAsync(setters => setters
                        .SetProperty(w => w.VerseId, next.Id)
                        .SetProperty(w => w.Position, w => w.Position - before.Words.Count), cancellationToken);
                    var oldNote = SweteDivisions.Note.Replace("thirty verse divisions", "twenty-nine verse divisions")
                        .Replace("twenty-six where", "twenty-five where");
                    var note = swete.RightsNote?.Replace(oldNote, SweteDivisions.Note);
                    swete.RightsNote = note?.Contains(SweteDivisions.Note, StringComparison.Ordinal) == true
                        ? note : $"{note} {SweteDivisions.Note}".Trim();
                    moved += tail.Length;
                    changed.Add(swete.Slug);
                }

                var lastWords = await Stored(next.Id, cancellationToken);
                if (lastWords[^1].Surface == "XX")
                {
                    var ids = new[] { lastWords[^1].Id };
                    await SweteChapterMarkerEvidence.GuardRemoved(db, ids, cancellationToken);
                    await db.Links.Where(l => l.Words.Any(w => ids.Contains(w.WordId))).ExecuteDeleteAsync(cancellationToken);
                    await db.Words.Where(w => ids.Contains(w.Id)).ExecuteDeleteAsync(cancellationToken);
                    if (swete.RightsNote?.Contains(SweteRestorations.ChapterMarkersNote, StringComparison.Ordinal) != true)
                        swete.RightsNote = $"{swete.RightsNote} {SweteRestorations.ChapterMarkersNote}".Trim();
                    removed++;
                    changed.Add(swete.Slug);
                }
            }
        }

        var pairs = (await Pairs(changed, cancellationToken))
            .Concat(beforePairs.Where(p => changed.Contains(p.From) || changed.Contains(p.To))).Distinct().ToList();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new EditionBoundaryOutcome(removed, moved, [.. changed.Order()], pairs);
    }

    private async Task<List<BoundaryPair>> Pairs(HashSet<string> texts, CancellationToken token)
    {
        var wordPairs = await db.Links.Where(l => texts.Contains(l.FromText!.Slug) || texts.Contains(l.ToText!.Slug))
            .Select(l => new BoundaryPair(l.FromText!.Slug, l.ToText!.Slug)).Distinct().ToListAsync(token);
        var versePairs = await db.VerseLinks.Where(l => texts.Contains(l.FromText!.Slug) || texts.Contains(l.ToText!.Slug))
            .Select(l => new BoundaryPair(l.FromText!.Slug, l.ToText!.Slug)).Distinct().ToListAsync(token);
        return [.. wordPairs.Concat(versePairs).Distinct()];
    }

    private Task<List<Word>> Stored(int verse, CancellationToken token) =>
        db.Words.AsNoTracking().Where(w => w.VerseId == verse).OrderBy(w => w.Position).ToListAsync(token);

    private static bool Matches(IEnumerable<Word> words, IEnumerable<(string Surface, string Trailer)> expected) =>
        words.Select(w => (w.Surface, w.Trailer)).SequenceEqual(expected);

    private async Task GuardRemoved(long[] words, CancellationToken token)
    {
        if (await db.WordEntities.AnyAsync(a => words.Contains(a.WordId), token)
            || await db.Links.AnyAsync(l => l.Words.Any(w => words.Contains(w.WordId))
                && (l.Method != Database.Entities.Enums.LinkMethod.Aligner
                    || l.Claims.Any(c => c.Method != Database.Entities.Enums.LinkMethod.Aligner)), token))
            throw new InvalidOperationException("An editorial heading has annotated or protected source/manual/accepted evidence; nothing changed. Review that evidence before removing its words.");
    }
}
