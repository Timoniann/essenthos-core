using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Swete;
using Essenthos.Core.Usfm;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

internal sealed record BoundaryPair(string From, string To);
internal sealed record EditionBoundaryOutcome(int RemovedWords, int MovedWords, IReadOnlyList<string> Texts,
    IReadOnlyList<BoundaryPair> Pairs, int RewrittenWords = 0)
{
    public override string ToString() => RemovedWords + MovedWords + RewrittenWords == 0
        ? "Edition boundaries: nothing to do, every text already reads as its edition prints it"
        : $"Edition boundaries: {RemovedWords} editorial words removed, {MovedWords} words moved with their rows, "
          + $"{RewrittenWords} words rewritten in place; texts [{string.Join(", ", Texts)}]; "
          + $"affected pairs [{string.Join(", ", Pairs.Select(p => p.From + "→" + p.To))}]";
}

/// <summary>
/// Brings a warm corpus to what the readers now read where an edition's own boundary was misread: the
/// King James Apocrypha's editorial headings, Swete's 2 Samuel 19:43, which the transcription ran into
/// 19:42, and the chapter numbers Swete's transcription let into the text. A cold load reads all three
/// right and this finds nothing to do, and so does a second run.
///
/// <para>
/// Every verse is compared with the reading first, and one that already reads as the edition prints it
/// is left alone, whatever else is true of it: the pass is replayed by every load, and a pass that
/// refused a corpus already right would stop every load after it. A verse reading as neither the
/// reading nor a state this pass knows stops it, and the transaction leaves the corpus as it was.
/// The words that stay keep their rows, and with them every link and annotation; a word that goes may
/// take with it only what <see cref="RemovedWordEvidence"/> allows.
/// </para>
/// </summary>
internal sealed class EditionBoundaryRepairLoader(AppDbContext db)
{
    private const string SecondSamuel = "12.Regnorum_II";

    public async Task<EditionBoundaryOutcome> Load(string resources, CancellationToken cancellationToken = default)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var beforePairs = await Pairs(new HashSet<string> { Sources.KingJamesSlug, SweteTextSource.Slug }, cancellationToken);
        var changed = new HashSet<string>();
        var removed = 0;
        var moved = 0;
        var rewritten = 0;

        var kjv = await db.Texts.SingleOrDefaultAsync(t => t.Slug == Sources.KingJamesSlug, cancellationToken);
        if (kjv is not null)
        {
            var headings = await RemoveHeadings(kjv, resources, cancellationToken);
            removed += headings;
            if (headings > 0)
            {
                changed.Add(kjv.Slug);
            }
        }

        var swete = await db.Texts.SingleOrDefaultAsync(t => t.Slug == SweteTextSource.Slug, cancellationToken);
        if (swete is not null)
        {
            var folder = Path.Combine(resources, "Swete");
            var reading = SweteTextSource.Read(folder);
            var marked = SweteTextSource.Read(folder, chapterMarkers: false);

            var (split, marker) = await OpenSecondSamuelNineteenFortyThree(swete, folder, reading, marked, cancellationToken);
            var opened = await OpenChaptersRunIntoTheVerseBefore(swete, reading, cancellationToken);
            var (taken, kept) = await TakeChapterMarkers(swete, reading, marked, cancellationToken);
            moved += split + opened;
            removed += marker + taken;
            rewritten += kept;
            if (split + opened + marker + taken + kept > 0)
            {
                changed.Add(swete.Slug);
            }

            if (opened > 0 && swete.RightsNote?.Contains(SweteSettled.Note, StringComparison.Ordinal) != true)
            {
                swete.RightsNote = $"{swete.RightsNote} {SweteSettled.Note}".Trim();
            }

            if (marker + taken + kept > 0)
            {
                var note = swete.RightsNote?.Replace(SweteRestorations.SecondSamuelMarkerNote, SweteRestorations.ChapterMarkersNote);
                swete.RightsNote = note?.Contains(SweteRestorations.ChapterMarkersNote, StringComparison.Ordinal) == true
                    ? note
                    : $"{note} {SweteRestorations.ChapterMarkersNote}".Trim();
            }
        }

        var pairs = (await Pairs(changed, cancellationToken))
            .Concat(beforePairs.Where(p => changed.Contains(p.From) || changed.Contains(p.To))).Distinct().ToList();
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new EditionBoundaryOutcome(removed, moved, [.. changed.Order()], pairs, rewritten);
    }

    /// <summary>
    /// The headings the King James Apocrypha sets above a verse, which its file marks as <c>\s</c> and
    /// <c>\ms</c> and a loader before the reader knew them took as the verse's first words.
    /// </summary>
    private async Task<int> RemoveHeadings(Text kjv, string resources, CancellationToken cancellationToken)
    {
        var removed = 0;
        var folder = Path.Combine(resources, DeuterocanonTextSource.KingJamesFolder);
        foreach (var (code, ordinal) in new[] { ("BAR", 67), ("SIR", 72), ("BEL", 78) })
        {
            var file = Directory.GetFiles(folder, "*.usfm").Single(path =>
                File.ReadAllText(path).StartsWith($"\\id {code}", StringComparison.Ordinal));
            var content = File.ReadAllText(file);
            var headed = UsfmReader.Read(content);
            var printed = UsfmReader.Read(content, editorialHeadings: true);
            foreach (var chapter in headed.Chapters)
            foreach (var verse in chapter.Verses.Where(v => v.Words.Any(w => w.Heading)))
            {
                var wanted = printed.Chapters.Single(c => c.Number == chapter.Number).Verses
                    .Single(v => v.Number == verse.Number && v.Label == verse.Label).Words
                    .Select(w => (w.Surface, w.Trailer)).ToList();
                if (!verse.Words.Where(w => !w.Heading).Select(w => (w.Surface, w.Trailer)).SequenceEqual(wanted))
                {
                    throw new InvalidOperationException(
                        $"{code} {chapter.Number}:{verse.Number} without its headings is not the verse the edition prints; nothing changed.");
                }

                var storedVerse = await db.Verses.SingleOrDefaultAsync(v => v.TextId == kjv.Id
                    && v.Book!.CanonicalOrdinal == ordinal && v.ChapterNumber == chapter.Number
                    && v.Number == verse.Number && v.Label == verse.Label, cancellationToken);
                if (storedVerse is null)
                {
                    continue;
                }

                var words = await Stored(storedVerse.Id, cancellationToken);
                if (Matches(words, wanted))
                {
                    continue;
                }

                if (!Matches(words, verse.Words.Select(w => (w.Surface, w.Trailer))))
                {
                    throw new InvalidOperationException(
                        $"KJV {ordinal}:{chapter.Number}:{verse.Number} is neither the known headed nor the printed verse; nothing changed.");
                }

                var ids = words.Where((_, index) => verse.Words[index].Heading).Select(w => w.Id).ToArray();
                await RemovedWordEvidence.Remove(db, ids, cancellationToken);
                await Renumber(storedVerse.Id, cancellationToken);
                if (!Matches(await Stored(storedVerse.Id, cancellationToken), wanted))
                {
                    throw new InvalidOperationException("The heading removal did not reconstruct the printed verse; nothing changed.");
                }

                removed += ids.Length;
            }
        }

        return removed;
    }

    /// <summary>
    /// 2 Samuel 19:43, which the transcription ran into 19:42 and a corpus loaded before the division
    /// holds as one verse: the words after 19:42's are moved, rows and all, into a verse of their own,
    /// and the chapter number the transcription closes it with is taken off. A corpus that holds 19:43
    /// already has nothing for this to do; its chapter numbers are <see cref="TakeChapterMarkers"/>'s.
    /// </summary>
    private async Task<(int Moved, int Removed)> OpenSecondSamuelNineteenFortyThree(
        Text swete, string folder, TextSource reading, TextSource marked, CancellationToken cancellationToken)
    {
        var previous = await db.Verses.SingleOrDefaultAsync(v => v.TextId == swete.Id
            && v.Book!.CanonicalOrdinal == 10 && v.ChapterNumber == 19 && v.Number == 42 && v.Label == "", cancellationToken);
        if (previous is null
            || await db.Verses.AnyAsync(v => v.TextId == swete.Id && v.BookId == previous.BookId
                && v.ChapterNumber == 19 && v.Number == 43 && v.Label == "", cancellationToken))
        {
            return (0, 0);
        }

        var chapter = marked.Books.Single(b => b.CanonicalOrdinal == 10).Chapters.Single(c => c.Number == 19);
        var before = chapter.Verses.Single(v => v.Number == 42).Words.Select(w => (w.Surface, w.Trailer)).ToList();
        var after = chapter.Verses.Single(v => v.Number == 43).Words.Select(w => (w.Surface, w.Trailer)).ToList();
        var lines = File.ReadLines(Path.Combine(folder, SweteTextSource.FileName(SecondSamuel))).ToList();
        var raw = SweteReader.Read(lines, keepChapterMarkers: true)
            .Chapters.Single(c => c.Number == 19).Verses.Single(v => v.Number == 42).Words
            .Select(w => (w.Surface, w.Trailer)).ToList();
        var historical = SweteReader.Read(SweteRestorations.Apply(SecondSamuel, lines,
                [.. SweteCorrections.All.Where(c => c.Book == SecondSamuel && c.Chapter == 19 && c.Verse == 42)]),
                keepChapterMarkers: true)
            .Chapters.Single(c => c.Number == 19).Verses.Single(v => v.Number == 42).Words
            .Select(w => (w.Surface, w.Trailer)).ToList();
        var read = reading.Books.Single(b => b.CanonicalOrdinal == 10).Chapters.Single(c => c.Number == 19);
        var known = new[] { [.. before, .. after], raw, historical,
                [.. read.Verses.Single(v => v.Number == 42).Words.Select(w => (w.Surface, w.Trailer)),
                    .. read.Verses.Single(v => v.Number == 43).Words.Select(w => (w.Surface, w.Trailer))] }
            .SelectMany(words => new[] { words, words.Count > 0 && Roman(words[^1].Surface) ? words.SkipLast(1).ToList() : words })
            .ToList();

        var words = await Stored(previous.Id, cancellationToken);
        if (!known.Any(k => Matches(words, k)))
        {
            throw new InvalidOperationException("SWETE 2 Samuel 19:42 is not the known combined verse; nothing changed.");
        }

        var next = new Verse
        {
            TextId = swete.Id, BookId = previous.BookId, ChapterId = previous.ChapterId,
            ChapterNumber = 19, Number = 43, Sequence = previous.Sequence + 1,
        };
        db.Verses.Add(next);
        await db.SaveChangesAsync(cancellationToken);
        var tail = words.Skip(before.Count).Select(w => w.Id).ToArray();
        await db.Words.Where(w => tail.Contains(w.Id)).ExecuteUpdateAsync(setters => setters
            .SetProperty(w => w.VerseId, next.Id)
            .SetProperty(w => w.Position, w => w.Position - before.Count), cancellationToken);
        var oldNote = SweteDivisions.Note.Replace("thirty verse divisions", "twenty-nine verse divisions")
            .Replace("twenty-six where", "twenty-five where");
        var note = swete.RightsNote?.Replace(oldNote, SweteDivisions.Note);
        swete.RightsNote = note?.Contains(SweteDivisions.Note, StringComparison.Ordinal) == true
            ? note
            : $"{note} {SweteDivisions.Note}".Trim();

        var last = await Stored(next.Id, cancellationToken);
        if (last.Count == 0 || !Roman(last[^1].Surface))
        {
            return (tail.Length, 0);
        }

        await RemovedWordEvidence.Remove(db, [last[^1].Id], cancellationToken);
        return (tail.Length, 1);
    }

    /// <summary>
    /// A chapter's first verse that <see cref="SweteDivisions"/> opens out of the verse before it, where
    /// the transcription ran its words into the last verse of the chapter before and left the verse
    /// itself nothing but the chapter's number: 3 Kingdoms 16:1, in 15:34. A corpus loaded before the
    /// division holds the words at the end of 15:34, with the number after them or, once this pass took
    /// the numbers out, without, and 16:1 empty or holding the number alone. The words are moved, rows
    /// and all, to the head of 16:1, the numbers with them, so that the verse then reads as the edition
    /// with its numbers or without and <see cref="TakeChapterMarkers"/> finishes it. A corpus whose 16:1
    /// holds words already has nothing for this to do.
    /// </summary>
    private async Task<int> OpenChaptersRunIntoTheVerseBefore(Text swete, TextSource reading, CancellationToken cancellationToken)
    {
        var moved = 0;
        foreach (var (canonical, (fromChapter, fromVerse), (toChapter, toVerse)) in new[] { (11, (15, 34), (16, 1)) })
        {
            var from = await db.Verses.SingleOrDefaultAsync(v => v.TextId == swete.Id && v.Book!.CanonicalOrdinal == canonical
                && v.ChapterNumber == fromChapter && v.Number == fromVerse && v.Label == "", cancellationToken);
            var to = await db.Verses.SingleOrDefaultAsync(v => v.TextId == swete.Id && v.Book!.CanonicalOrdinal == canonical
                && v.ChapterNumber == toChapter && v.Number == toVerse && v.Label == "", cancellationToken);
            if (from is null || to is null)
            {
                continue;
            }

            var opening = await Stored(to.Id, cancellationToken);
            if (opening.Any(w => !Roman(w.Surface)))
            {
                continue;
            }

            var book = reading.Books.Single(b => b.CanonicalOrdinal == canonical);
            var head = Read(book, fromChapter, fromVerse);
            var words = Read(book, toChapter, toVerse);
            var closing = await Stored(from.Id, cancellationToken);
            var tail = closing.Skip(head.Count).ToList();
            var numerals = tail.Skip(words.Count).ToList();
            if (!Matches(closing.Take(head.Count), head) || !Matches(tail.Take(words.Count), words)
                || numerals.Any(w => !Roman(w.Surface)))
            {
                throw new InvalidOperationException(
                    $"SWETE {BookReferences.Name(canonical)} {fromChapter}:{fromVerse} is neither the verse the edition prints "
                    + $"nor that verse with the words of {toChapter}:{toVerse} run into it; nothing changed.");
            }

            await db.Words.Where(w => w.VerseId == to.Id).ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Position, w => -w.Position - tail.Count), cancellationToken);
            var ids = tail.Select(w => w.Id).ToArray();
            await db.Words.Where(w => ids.Contains(w.Id)).ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.VerseId, to.Id)
                .SetProperty(w => w.Position, w => w.Position - head.Count), cancellationToken);
            await db.Words.Where(w => w.VerseId == to.Id && w.Position < 0).ExecuteUpdateAsync(setters => setters
                .SetProperty(w => w.Position, w => -w.Position), cancellationToken);
            moved += words.Count;
        }

        return moved;
    }

    private static List<(string Surface, string Trailer)> Read(BookDraft book, int chapter, int verse) =>
        [.. book.Chapters.Single(c => c.Number == chapter).Verses.Single(v => v.Number == verse && v.Label == "").Words
            .Select(w => (w.Surface, w.Trailer))];

    /// <summary>
    /// The chapter numbers <see cref="SweteReader"/> takes out of the text, taken out of a corpus that
    /// loaded Swete with them: a numeral standing alone goes, with what <see cref="RemovedWordEvidence"/>
    /// allows, and one run into a word is taken off it, the row and its links staying. Only a verse that
    /// reads as the restored edition with its numbers is changed. One that reads as the edition without
    /// them is already right, and one holding none of the numbers has nothing of them to take; one
    /// holding a number and reading otherwise is a Swete nobody here has read, and stops the pass.
    /// </summary>
    private async Task<(int Removed, int Rewritten)> TakeChapterMarkers(
        Text swete, TextSource reading, TextSource marked, CancellationToken cancellationToken)
    {
        var removed = 0;
        var rewritten = 0;
        foreach (var (book, chapter, verse, before, after) in Differing(marked, reading))
        {
            var stored = await db.Verses.AsNoTracking()
                .Where(v => v.TextId == swete.Id && v.Book!.CanonicalOrdinal == book && v.ChapterNumber == chapter
                            && v.Number == verse.Number && v.Label == verse.Label)
                .Select(v => (int?)v.Id)
                .SingleOrDefaultAsync(cancellationToken);
            if (stored is not { } verseId)
            {
                continue;
            }

            var words = await Stored(verseId, cancellationToken);
            var printed = after.Select(w => (w.Surface, w.Trailer)).ToList();
            if (Matches(words, printed))
            {
                continue;
            }

            var edits = SweteChapterMarkerEdits.Between(before, after);
            if (!Matches(words, before.Select(w => (w.Surface, w.Trailer))))
            {
                var numerals = edits.Select(e => before[e.Index].Surface).ToHashSet();
                if (words.Any(w => numerals.Contains(w.Surface)))
                {
                    throw new InvalidOperationException(
                        $"SWETE {BookReferences.Name(book)} {chapter}:{verse.Number}{verse.Label} holds a chapter number "
                        + $"but reads neither as the edition with it nor without it: \"{string.Concat(words.Select(w => w.Surface + w.Trailer))}\". "
                        + "Nothing changed.");
                }

                continue;
            }

            var gone = edits.Where(e => e.Kept is null).Select(e => words[e.Index].Id).ToArray();
            await RemovedWordEvidence.Remove(db, gone, cancellationToken);
            foreach (var edit in edits.Where(e => e.Kept is not null))
            {
                var id = words[edit.Index].Id;
                var surface = edit.Kept!;
                var folded = WordFolding.Fold(surface, swete.Language);
                await db.Words.Where(w => w.Id == id).ExecuteUpdateAsync(setters => setters
                    .SetProperty(w => w.Surface, surface)
                    .SetProperty(w => w.NormalisedText, folded), cancellationToken);
            }

            await Renumber(verseId, cancellationToken);
            if (!Matches(await Stored(verseId, cancellationToken), printed))
            {
                throw new InvalidOperationException(
                    $"SWETE {BookReferences.Name(book)} {chapter}:{verse.Number}{verse.Label} does not read as the edition "
                    + "once its chapter number is taken out; nothing changed.");
            }

            removed += gone.Length;
            rewritten += edits.Count - gone.Length;
        }

        return (removed, rewritten);
    }

    /// <summary>The verses the two readings of the edition hold differently, with both readings' words.</summary>
    private static IEnumerable<(int Book, int Chapter, VerseDraft Verse, IReadOnlyList<WordDraft> Before,
        IReadOnlyList<WordDraft> After)> Differing(TextSource marked, TextSource reading)
    {
        foreach (var book in marked.Books)
        {
            var other = reading.Books.Single(b => b.CanonicalOrdinal == book.CanonicalOrdinal);
            foreach (var chapter in book.Chapters)
            {
                var otherChapter = other.Chapters.Single(c => c.Number == chapter.Number);
                foreach (var verse in chapter.Verses)
                {
                    var after = otherChapter.Verses.Single(v => v.Number == verse.Number && v.Label == verse.Label).Words;
                    if (!verse.Words.SequenceEqual(after))
                    {
                        yield return (book.CanonicalOrdinal, chapter.Number, verse, verse.Words, after);
                    }
                }
            }
        }
    }

    private async Task Renumber(int verse, CancellationToken cancellationToken)
    {
        await db.Words.Where(w => w.VerseId == verse)
            .ExecuteUpdateAsync(setters => setters.SetProperty(w => w.Position, w => -w.Position), cancellationToken);
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE word w SET position = p.position FROM "
            + "(SELECT id, row_number() OVER (ORDER BY position DESC)::integer AS position FROM word WHERE verse_id = {0}) p "
            + "WHERE w.id = p.id", [verse], cancellationToken);
    }

    private static bool Roman(string surface) => SweteChapterMarkerEdits.IsNumeral(surface);

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
}
