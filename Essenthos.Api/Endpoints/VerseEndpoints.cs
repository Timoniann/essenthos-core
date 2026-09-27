using System.Globalization;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
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

    /// <summary>Where the frame places a psalm's title: before the psalm's first verse.</summary>
    private const int TitleVerse = 0;

    private const int FirstVerse = 1;

    /// <summary>How often a text must spell an entity one way for that spelling to mark a verse the annotations missed.</summary>
    private const int LeastSpelled = 2;

    /// <summary>The one book whose chapters have titles the frame numbers apart.</summary>
    private const int Psalms = 19;

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
        var titles = keys.Where(key => key % Address.ChapterStride == TitleVerse).ToHashSet();
        var sought = keys.Concat(titles.Select(title => title - TitleVerse + FirstVerse)).Distinct().ToList();
        var found = await db.VerseReferences
            .Where(r => r.Verse!.TextId == textId
                        && books.Contains(r.CanonicalBook)
                        && sought.Contains((r.CanonicalBook * Address.BookStride)
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

        // A text that gives a psalm's title no verse of its own prints it in the psalm's first verse,
        // as the Berean does, so that verse answers for the title where nothing else is placed there.
        var held = found.Select(place => place.Key).ToHashSet();
        var placed = found
            .Where(place => keys.Contains(place.Key))
            .Concat(found
                .Where(place => place.CanonicalVerse == FirstVerse)
                .Select(place => place with { Key = place.Key - FirstVerse + TitleVerse, CanonicalVerse = TitleVerse })
                .Where(place => titles.Contains(place.Key) && !held.Contains(place.Key)))
            .ToList();

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

        var stated = await StatedNumbers(db, verseIds, cancellationToken);

        var naming = named is null
            ? []
            : await Naming(db, words.Values.SelectMany(list => list.Select(word => word.Id)), named, cancellationToken);
        var spellings = named is null ? [] : await Spellings(db, textId, named, cancellationToken);

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
                    .SelectMany(place => OwnNumbers(
                        stated.GetValueOrDefault(place.VerseId),
                        place.OwnBook, place.OwnChapter, place.OwnVerse, place.Label, group.Key.CanonicalBook))
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
                        : Marks(Spelled(
                            [.. joined.Select(word => new MarkedWord(word.Surface, word.Trailer, naming.Contains(word.Id)))],
                            spellings)),
                    Printed = printed.Count > 0 ? printed : null,
                };
            })
            .OfType<VerseTextResponse>()
            .ToList();
    }

    /// <summary>
    /// The numbers an edition prints inside each of these verses, in the order it prints them, as
    /// <c>chapter:verse</c>. Most verses have none, and are numbered by their own row.
    /// </summary>
    internal static async Task<Dictionary<int, List<string>>> StatedNumbers(
        AppDbContext db,
        IReadOnlyCollection<int> verseIds,
        CancellationToken cancellationToken) =>
        (await db.StatedVerseNumbers
            .Where(n => verseIds.Contains(n.VerseId))
            .Select(n => new { n.VerseId, n.Position, n.ChapterNumber, n.ChapterLabel, n.Number })
            .ToListAsync(cancellationToken))
        .GroupBy(n => n.VerseId)
        .ToDictionary(
            group => group.Key,
            group => group.OrderBy(n => n.Position)
                .Select(n => $"{n.ChapterNumber}{n.ChapterLabel}{WithinAddress}{n.Number}")
                .ToList());

    /// <summary>
    /// How a text numbers one of its verses: what the edition prints in it where it prints anything,
    /// and otherwise the verse's own chapter and number. Nothing where the text holds the verse in
    /// another book than the one it is placed in, since a bare <c>chapter:verse</c> would then name
    /// a place in the wrong book.
    /// </summary>
    internal static IEnumerable<string> OwnNumbers(
        IEnumerable<string>? stated,
        int ownBook,
        int ownChapter,
        int ownVerse,
        string label,
        int placedBook) =>
        stated ?? (ownBook == placedBook ? [$"{ownChapter}{WithinAddress}{ownVerse}{label}"] : []);

    /// <summary>
    /// How a text numbers one verse placed at a frame address, where that differs from the address.
    /// </summary>
    internal static IList<string>? Printed(
        IEnumerable<string>? stated,
        int ownBook,
        int ownChapter,
        int ownVerse,
        string label,
        int placedBook,
        int placedChapter,
        int placedVerse)
    {
        var asked = $"{placedChapter}{WithinAddress}{placedVerse}";
        var printed = OwnNumbers(stated, ownBook, ownChapter, ownVerse, label, placedBook)
            .Where(address => address != asked)
            .Distinct()
            .ToList();
        return printed.Count > 0 ? printed : null;
    }

    /// <summary>
    /// The words that name the entity, by the same pick the word card makes, so that a word marked
    /// in a list and the same word opened in the chapter never name two different people. A text the
    /// annotations never reached has none, and gets none: nothing here guesses a name from the links.
    ///
    /// <para>
    /// A word for God is the exception, because it is a word and not a person: <em>God</em> in
    /// Genesis 1:1 is annotated to YHVH, and it is also the word Elohim. Its verses are the ones
    /// whose original carries its number, so the words marked are those that carry the number
    /// themselves or render one that does.
    /// </para>
    /// </summary>
    internal static async Task<HashSet<long>> Naming(
        AppDbContext db,
        IEnumerable<long> wordIds,
        string slug,
        CancellationToken cancellationToken)
    {
        var ids = wordIds.ToList();
        var numbers = (await db.EntityNames
                .Where(n => n.Entity!.Slug == slug && n.Entity.Kind == EntityKind.Term)
                .Select(n => new { n.HebrewStrongNumber, n.GreekStrongNumber })
                .ToListAsync(cancellationToken))
            .SelectMany(n => (string?[])[n.HebrewStrongNumber, n.GreekStrongNumber])
            .OfType<string>()
            .Distinct()
            .ToList();
        if (numbers.Count > 0)
        {
            return await Numbered(db, ids, numbers, cancellationToken);
        }

        return (await Annotations.Of(db, ids, cancellationToken))
            .Where(annotation => annotation.Value.Slug == slug)
            .Select(annotation => annotation.Key)
            .ToHashSet();
    }

    /// <summary>
    /// How this text spells the entity where its words are known to name it, folded: the forms the
    /// corpus counted from the annotated words, each seen more than once.
    /// </summary>
    private static async Task<HashSet<string>> Spellings(
        AppDbContext db,
        int textId,
        string slug,
        CancellationToken cancellationToken) =>
        [
            .. await db.EntityRenderings
                .Where(r => r.TextId == textId && r.Entity!.Slug == slug && r.Occurrences >= LeastSpelled)
                .Select(r => r.Folded)
                .ToListAsync(cancellationToken),
        ];

    /// <summary>
    /// A listed verse whose words the annotations never reached — a translation that names Joshua's
    /// Jerusalem with no link to the Hebrew word, a place the translator names where the Hebrew
    /// says <em>there</em> — is marked where a word is spelled as this text spells the entity
    /// everywhere else, alone or with the word after it as one name. A verse with any annotated
    /// word keeps exactly those, so a spelling never adds to or overrules what the corpus says.
    /// </summary>
    internal static IReadOnlyList<MarkedWord> Spelled(IReadOnlyList<MarkedWord> words, IReadOnlySet<string> spellings)
    {
        if (spellings.Count == 0 || words.Any(word => word.Named))
        {
            return words;
        }

        var spelled = words.ToArray();
        for (var at = 0; at < spelled.Length; at++)
        {
            var one = NameFolding.Fold(spelled[at].Surface);
            if (one.Length == 0)
            {
                continue;
            }

            if (spellings.Contains(one))
            {
                spelled[at] = spelled[at] with { Named = true };
            }
            else if (at + 1 < spelled.Length && spellings.Contains(one + NameFolding.Fold(spelled[at + 1].Surface)))
            {
                spelled[at] = spelled[at] with { Named = true };
                spelled[at + 1] = spelled[at + 1] with { Named = true };
                at++;
            }
        }

        return spelled;
    }

    /// <summary>The words among these that carry one of the numbers, or render or equal a word that does.</summary>
    private static async Task<HashSet<long>> Numbered(
        AppDbContext db,
        IReadOnlyCollection<long> wordIds,
        IReadOnlyCollection<string> numbers,
        CancellationToken cancellationToken)
    {
        var own = await db.Words
            .Where(w => wordIds.Contains(w.Id) && w.StrongNumber != null && numbers.Contains(w.StrongNumber))
            .Select(w => w.Id)
            .ToListAsync(cancellationToken);
        var rendering = await db.LinkWords
            .Where(side => wordIds.Contains(side.WordId)
                           && (side.Link!.Relation == LinkRelation.Renders || side.Link.Relation == LinkRelation.Equals)
                           && db.LinkWords.Any(other => other.LinkId == side.LinkId
                                                        && other.Side != side.Side
                                                        && other.Word!.StrongNumber != null
                                                        && numbers.Contains(other.Word.StrongNumber)))
            .Select(side => side.WordId)
            .ToListAsync(cancellationToken);
        return [.. own, .. rendering];
    }

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

        /// <summary>
        /// Verse 0 is an address in the Psalms and nowhere else: it is where the frame places a
        /// psalm's title, which BHSA numbers as the psalm's first verse.
        /// </summary>
        public static Address? Parse(string asked)
        {
            var parts = asked.Split(WithinAddress);
            if (parts.Length != 3
                || BookReferences.ResolveOrdinal(parts[0]) is not { } ordinal
                || !int.TryParse(parts[1], out var chapter)
                || !int.TryParse(parts[2], out var verse)
                || chapter < 1
                || verse < (ordinal == Psalms ? TitleVerse : FirstVerse))
            {
                return null;
            }

            return new Address(asked, (ordinal * BookStride) + (chapter * ChapterStride) + verse);
        }
    }
}
