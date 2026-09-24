using System.Globalization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <param name="Text">The verse as it reads, its words joined by their own trailers.</param>
internal record VerseTextResponse(BookRefResponse Book, int Chapter, int Verse, string Text)
{
    /// <summary>The verse's own label where the text gives one, as the chapter reading does.</summary>
    public string? Label { get; init; }

    /// <summary>
    /// Where the verse names the entity the request asked about, in order and never overlapping.
    /// Null when no entity was asked about; empty where this text names it nowhere in the verse, or
    /// carries no word-level annotation to say so.
    /// </summary>
    public IList<VerseMarkResponse>? Marks { get; init; }

    /// <summary>
    /// The address the text itself gives this verse, in its own numbering, where that is not the one
    /// asked for: the Synodal's <c>118:1</c> for Psalm 119:1, the Reina-Valera's own number where it
    /// divides a chapter differently. More than one where the text divides what the shared frame
    /// holds as one verse. Null where the two numberings agree.
    /// </summary>
    public IList<string>? Printed { get; init; }
}

/// <summary>
/// A stretch of <see cref="VerseTextResponse.Text"/>, as UTF-16 offsets — what a JavaScript string
/// indexes by — from <paramref name="Start"/> up to but not including <paramref name="End"/>.
/// </summary>
internal record VerseMarkResponse(int Start, int End);

/// <param name="Missing">
/// The addresses this text has nothing at, in the spelling they were asked for. A reference that
/// resolves to no verse is an answer — the Septuagint numbers Jeremiah differently and the King
/// James has no Sirach — and dropping it silently would leave a caller unable to tell an empty
/// verse from one it never asked about.
/// </param>
internal record VerseTextListResponse(string Corpus, IList<VerseTextResponse> Items, IList<string> Missing);

/// <summary>
/// The words of a handful of verses, addressed one by one, with nothing else attached to them.
///
/// The reading endpoints answer a chapter at a time and answer it in full — every word with its
/// links, its morphology and its annotations, which is 200KB for Genesis 11 and is the right answer
/// for a reader. It is the wrong answer for a page that shows forty references and wants to say
/// what each of them says: forty chapters at that size is megabytes, fetched to render a tooltip.
///
/// So this endpoint takes the addresses and returns the words joined and nothing more. One request
/// for a page rather than one per reference, because a page of a hundred references is a hundred
/// round trips otherwise and the client cannot batch what the API will not take in a batch.
/// </summary>
internal static class VerseEndpoints
{
    /// <summary>
    /// More addresses than a page shows. The entity page pages its references at a hundred, and a
    /// request past this is a download of the text under another name.
    /// </summary>
    private const int MostAddresses = 120;

    /// <summary>What separates one address from the next, and the parts of one: `genesis:11:27`.</summary>
    private const char BetweenAddresses = ',';

    private const char WithinAddress = ':';

    public static void MapVerses(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/verses", async (
            [FromQuery] string? corpus,
            [FromQuery] string? refs,
            [FromQuery] string? entity,
            AppDbContext db,
            ICanonIndex canon,
            CancellationToken cancellationToken) =>
        {
            var text = corpus is { Length: > 0 }
                ? await canon.Text(corpus, cancellationToken)
                : (await canon.Texts(cancellationToken)).FirstOrDefault();

            if (text is null)
            {
                return ApiResults.NotFound(
                    $"There is no text \"{corpus}\". Ask /v1/corpora for the ones this corpus holds.");
            }

            var asked = (refs ?? string.Empty)
                .Split(BetweenAddresses, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToList();

            if (asked.Count == 0)
            {
                return Results.BadRequest(new ProblemResponse(
                    "Ask for verses with refs=book:chapter:verse, separated by commas — "
                    + "refs=genesis:11:27,exodus:4:14. The book is its name, slug or abbreviation."));
            }

            if (asked.Count > MostAddresses)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"{asked.Count} addresses is more than this endpoint answers at once. Ask for at most "
                    + $"{MostAddresses}, or read the chapter with /v1/text/{{corpus}}/{{book}}/{{chapter}}."));
            }

            var wanted = new List<Address>(asked.Count);
            var unreadable = new List<string>();
            foreach (var one in asked)
            {
                if (Address.Parse(one) is { } address)
                {
                    wanted.Add(address);
                }
                else
                {
                    unreadable.Add(one);
                }
            }

            if (unreadable.Count > 0)
            {
                return Results.BadRequest(new ProblemResponse(
                    $"\"{unreadable[0]}\" is not an address. Write it as book:chapter:verse, as in "
                    + "genesis:11:27."));
            }

            string? named = null;
            if (entity is { Length: > 0 })
            {
                var current = await MergedAddresses.Current(db, entity, cancellationToken);
                named = await db.Entities.Where(e => e.Slug == current).Select(e => e.Slug)
                    .FirstOrDefaultAsync(cancellationToken);
                if (named is null)
                {
                    return ApiResults.NotFound(
                        $"There is no person, place or people \"{entity}\". Ask /v1/entities for their slugs.");
                }
            }

            var keys = wanted.Select(address => address.Key).Distinct().ToList();
            var found = await Read(db, text.Id, keys, named, cancellationToken);

            var answered = found
                .Select(verse => (verse.Book.Ordinal * Address.BookStride)
                                 + (verse.Chapter * Address.ChapterStride) + verse.Verse)
                .ToHashSet();

            return Results.Ok(new VerseTextListResponse(
                text.Slug,
                found,
                [.. wanted.Where(address => !answered.Contains(address.Key)).Select(address => address.Asked)]));
        });
    }

    /// <summary>
    /// The verses at a set of addresses in the shared frame, each as one text reads it.
    ///
    /// <para>
    /// An address names a place in the frame, not a row of the text: the Reina-Valera numbers 187
    /// of its verses differently from the frame and the Synodal 30, so matching a text's own numbers
    /// quoted the verse beside the one asked for. The placement a verse carries is what is matched,
    /// and the verse's own number is sent back as <see cref="VerseTextResponse.Printed"/> wherever
    /// it differs, together with any the edition prints in the verse itself.
    /// </para>
    ///
    /// <para>
    /// Where a verse is placed at an address as its primary placement, only such verses answer for
    /// it; a verse that merely runs on into the address answers only where nothing is placed there.
    /// Otherwise a text that joins two verses into one would print the joined verse twice over.
    /// </para>
    /// </summary>
    internal static async Task<List<VerseTextResponse>> Read(
        AppDbContext db,
        int textId,
        IReadOnlyCollection<int> keys,
        string? named,
        CancellationToken cancellationToken)
    {
        // The book narrows the match to what the placement index can find; the packed number then
        // picks the addresses out of those books, on the same arithmetic the encyclopedia orders by.
        var books = keys.Select(key => key / Address.BookStride).Distinct().ToList();
        var placed = await db.VerseReferences
            .Where(r => r.Verse!.TextId == textId
                        && books.Contains(r.CanonicalBook)
                        && keys.Contains((r.CanonicalBook * Address.BookStride)
                                         + (r.CanonicalChapter * Address.ChapterStride)
                                         + r.CanonicalVerse))
            .Select(r => new
            {
                Key = (r.CanonicalBook * Address.BookStride) + (r.CanonicalChapter * Address.ChapterStride)
                      + r.CanonicalVerse,
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                r.IsPrimary,
                r.VerseId,
                OwnBook = r.Verse!.Book!.CanonicalOrdinal,
                OwnChapter = r.Verse.ChapterNumber,
                OwnVerse = r.Verse.Number,
                r.Verse.Label,
            })
            .ToListAsync(cancellationToken);

        var chosen = placed
            .GroupBy(place => place.Key)
            .SelectMany(group => group.Any(place => place.IsPrimary) ? group.Where(place => place.IsPrimary) : group)
            .ToList();
        var verseIds = chosen.Select(place => place.VerseId).Distinct().ToList();

        var words = (await db.Words
                .Where(w => verseIds.Contains(w.VerseId))
                .Select(w => new { w.VerseId, w.Id, w.Surface, w.Trailer, w.Position })
                .ToListAsync(cancellationToken))
            .GroupBy(word => word.VerseId)
            .ToDictionary(group => group.Key, group => group.OrderBy(word => word.Position).ToList());

        var stated = (await db.StatedVerseNumbers
                .Where(n => verseIds.Contains(n.VerseId))
                .Select(n => new { n.VerseId, n.Position, n.ChapterNumber, n.Number })
                .ToListAsync(cancellationToken))
            .GroupBy(n => n.VerseId)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(n => n.Position).Select(n => $"{n.ChapterNumber}{WithinAddress}{n.Number}").ToList());

        var naming = named is null
            ? []
            : await Naming(db, words.Values.SelectMany(list => list.Select(word => word.Id)), named, cancellationToken);

        return chosen
            .GroupBy(place => new { place.Key, place.CanonicalBook, place.CanonicalChapter, place.CanonicalVerse })
            .OrderBy(group => group.Key.Key)
            .Select(VerseTextResponse? (group) =>
            {
                var verses = group
                    .OrderBy(place => place.OwnBook).ThenBy(place => place.OwnChapter)
                    .ThenBy(place => place.OwnVerse).ThenBy(place => place.Label, StringComparer.Ordinal)
                    .ToList();
                var joined = verses.SelectMany(place => words.GetValueOrDefault(place.VerseId) ?? []).ToList();
                if (joined.Count == 0)
                {
                    return null;
                }

                var asked = $"{group.Key.CanonicalChapter}{WithinAddress}{group.Key.CanonicalVerse}";
                var printed = verses
                    .SelectMany(place => stated.GetValueOrDefault(place.VerseId)
                                         ?? (place.OwnBook == group.Key.CanonicalBook
                                             ? [$"{place.OwnChapter}{WithinAddress}{place.OwnVerse}{place.Label}"]
                                             : []))
                    .Where(address => address != asked)
                    .Distinct()
                    .ToList();
                var label = verses.Select(place => place.Label).FirstOrDefault(one => !string.IsNullOrWhiteSpace(one));

                return new VerseTextResponse(
                    new BookRefResponse(
                        group.Key.CanonicalBook,
                        BookReferences.Name(group.Key.CanonicalBook),
                        BookReferences.Slug(group.Key.CanonicalBook)),
                    group.Key.CanonicalChapter,
                    group.Key.CanonicalVerse,
                    string.Concat(joined.Select(word => word.Surface + word.Trailer)).Trim())
                {
                    Label = label,
                    Marks = named is null
                        ? null
                        : Marks([.. joined.Select(word => new MarkedWord(word.Surface, word.Trailer, naming.Contains(word.Id)))]),
                    Printed = printed.Count > 0 ? printed : null,
                };
            })
            .OfType<VerseTextResponse>()
            .ToList();
    }

    /// <summary>
    /// The words that name the entity, by the same pick the word card makes, so that a word marked
    /// in a list and the same word opened in the chapter never name two different people. A text the
    /// annotations never reached has none, and gets none: nothing here guesses a name from the links.
    /// </summary>
    internal static async Task<HashSet<long>> Naming(
        AppDbContext db,
        IEnumerable<long> wordIds,
        string slug,
        CancellationToken cancellationToken) =>
        (await Annotations.Of(db, wordIds, cancellationToken))
            .Where(annotation => annotation.Value.Slug == slug)
            .Select(annotation => annotation.Key)
            .ToHashSet();

    /// <summary>
    /// Where the marked words sit in the verse as <see cref="VerseTextResponse.Text"/> spells it,
    /// which is the words and their trailers joined and trimmed — so the offsets are counted over the
    /// same join and shifted by what the trim took off the front. A word printed with no letters has
    /// nothing to mark. Two marked words with only a space or a dash between them are one name, as
    /// in <em>Beth-el</em> or <em>Kirjath-arba</em>, and are marked as one.
    /// </summary>
    internal static List<VerseMarkResponse> Marks(IReadOnlyList<MarkedWord> words)
    {
        var joined = string.Concat(words.Select(word => word.Surface + word.Trailer));
        var text = joined.Trim();
        var trimmed = joined.Length - joined.TrimStart().Length;

        var marks = new List<VerseMarkResponse>();
        var at = -trimmed;
        foreach (var word in words)
        {
            var start = Math.Max(at, 0);
            var end = Math.Min(at + word.Surface.Length, text.Length);
            at += word.Surface.Length + word.Trailer.Length;

            if (!word.Named || end <= start)
            {
                continue;
            }

            if (marks.Count > 0 && Joins(text.AsSpan(marks[^1].End, start - marks[^1].End)))
            {
                marks[^1] = marks[^1] with { End = end };
            }
            else
            {
                marks.Add(new VerseMarkResponse(start, end));
            }
        }

        return marks;
    }

    private static bool Joins(ReadOnlySpan<char> between)
    {
        foreach (var character in between)
        {
            if (!char.IsWhiteSpace(character)
                && CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.DashPunctuation)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>One word of a verse, and whether it names the entity asked about.</summary>
    internal readonly record struct MarkedWord(string Surface, string Trailer, bool Named);

    /// <summary>One address as it was asked for, and as one number the database can match on.</summary>
    internal readonly record struct Address(string Asked, int Key)
    {
        internal const int ChapterStride = 1_000;

        internal const int BookStride = 1_000_000;

        public static Address? Parse(string asked)
        {
            var parts = asked.Split(WithinAddress);
            if (parts.Length != 3
                || BookReferences.ResolveOrdinal(parts[0]) is not { } ordinal
                || !int.TryParse(parts[1], out var chapter)
                || !int.TryParse(parts[2], out var verse)
                || chapter < 1
                || verse < 1)
            {
                return null;
            }

            return new Address(asked, (ordinal * BookStride) + (chapter * ChapterStride) + verse);
        }
    }
}
