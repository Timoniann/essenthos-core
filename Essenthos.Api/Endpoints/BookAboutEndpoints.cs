using System.Text;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a whole book holds, for a card a reader can ask for above a chapter's context: how many
/// chapters and verses each text gives it, the parts the text itself marks, who and where it names
/// most, and the years its events span in each reckoning. Everything is counted from the corpus; a
/// book the corpus says nothing about in one of these respects simply has nothing there.
/// </summary>
internal static class BookAboutEndpoints
{
    private const int MostPeople = 8;

    /// <summary>More places than people, because the map draws every one of them.</summary>
    private const int MostPlaces = 20;

    public static void MapBookAbout(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/context/{book}/about", async (
            string book,
            [FromQuery] string? language,
            AppDbContext db,
            CancellationToken cancellationToken) =>
        {
            var ordinal = BookReferences.ResolveOrdinal(book);
            return ordinal is null
                ? ApiResults.NotFound(BookReferences.FormatHint(book))
                : Results.Ok(await About(db, ordinal.Value, language, cancellationToken));
        });
    }

    internal static async Task<BookAboutResponse> About(
        AppDbContext db,
        int book,
        string? language,
        CancellationToken cancellationToken)
    {
        var counts = await db.Verses
            .Where(v => v.Book!.CanonicalOrdinal == book)
            .GroupBy(v => v.Text!.Slug)
            .Select(text => new BookTextCountResponse(
                text.Key,
                text.Select(v => v.ChapterNumber).Distinct().Count(),
                text.Select(v => v.ChapterNumber * 1000 + v.Number).Distinct().Count()))
            .ToListAsync(cancellationToken);

        var (people, places) = await Named(db, book, language, cancellationToken);

        var span = await Span(db, book, cancellationToken);

        return new BookAboutResponse(
            new BookRefResponse(book, BookReferences.Name(book), BookReferences.Slug(book)),
            [.. counts.OrderBy(c => c.Text, StringComparer.Ordinal)],
            people,
            places,
            span,
            await BookStructure.Of(db, book, cancellationToken));
    }

    /// <summary>
    /// In each reckoning, the year of the first event the book tells and of the last, in the order of
    /// its verses — not the least and greatest year of every event set in it, because a promise is set
    /// at the verse that makes it and dated by its fulfilment: Genesis 15 tells of four hundred years of
    /// oppression, and the book does not run to their end.
    /// </summary>
    private static async Task<Dictionary<string, int[]>> Span(AppDbContext db, int book, CancellationToken cancellationToken)
    {
        var inBook = db.Events.Where(e => e.CanonicalBook == book && e.CanonicalChapter != null);
        var at = await inBook
            .Select(e => new { e.Id, Chapter = e.CanonicalChapter!.Value, Verse = e.CanonicalVerse ?? 0 })
            .ToDictionaryAsync(e => e.Id, e => (e.Chapter, e.Verse), cancellationToken);
        var years = await EncyclopediaEndpoints.EventYears(
            db, db.EventDates.Where(d => inBook.Any(e => e.Id == d.EventId)), cancellationToken);

        return years
            .SelectMany(dated => dated.Value.Select(year => (Reckoning: year.Key, Verse: at[dated.Key], Year: year.Value)))
            .GroupBy(dated => dated.Reckoning)
            .ToDictionary(
                reckoning => reckoning.Key,
                reckoning =>
                {
                    var first = reckoning.Min(dated => dated.Verse);
                    var last = reckoning.Max(dated => dated.Verse);
                    return new[]
                    {
                        reckoning.Where(dated => dated.Verse == first).Min(dated => dated.Year),
                        reckoning.Where(dated => dated.Verse == last).Max(dated => dated.Year),
                    };
                });
    }

    /// <summary>
    /// The people and places the book names in the most verses: a verse counts where a word of any
    /// text names the record, as the chapter's panel counts it, or where the record's own source lists
    /// the verse and does not dispute it. The places are those the gazetteer puts on the map, because
    /// the sources also file the earth and the heavens as places, and Genesis names them in more
    /// verses than Canaan.
    /// </summary>
    private static async Task<(IList<BookEntityResponse> People, IList<BookEntityResponse> Places)> Named(
        AppDbContext db,
        int book,
        string? language,
        CancellationToken cancellationToken)
    {
        var (verses, withheld) = await Annotations.InBookApart(db, book, cancellationToken);
        var listed = await db.EntityVerses.Shown()
            .Where(v => v.CanonicalBook == book && !v.Disputed)
            .Select(v => new { v.Entity!.Slug, v.CanonicalChapter, v.CanonicalVerse, v.Source })
            .Distinct()
            .ToListAsync(cancellationToken);
        foreach (var row in listed)
        {
            // The verse list is read off the same words, so it holds the verse the ranking leaves to the
            // people; a record some other word in the verse names keeps it.
            if (ChapterSalience.FromTheWords(row.Source)
                && withheld.GetValueOrDefault(row.Slug)?.Contains((row.CanonicalChapter, row.CanonicalVerse)) == true
                && verses.GetValueOrDefault(row.Slug)?.Contains((row.CanonicalChapter, row.CanonicalVerse)) != true)
            {
                continue;
            }

            if (!verses.TryGetValue(row.Slug, out var at))
            {
                verses[row.Slug] = at = [];
            }

            at.Add((row.CanonicalChapter, row.CanonicalVerse));
        }

        var slugs = verses.Keys.ToList();
        var records = await db.Entities
            .Where(e => slugs.Contains(e.Slug) && (e.Kind == EntityKind.Person || e.Kind == EntityKind.Place))
            .Select(e => new
            {
                e.Id,
                e.Slug,
                e.Kind,
                e.Name,
                Location = e.Location == null
                    ? null
                    : new EntityLocationResponse(
                        e.Location.Longitude,
                        e.Location.Latitude,
                        e.Location.Kind,
                        e.Location.Score / EncyclopediaEndpoints.ScoreScale),
            })
            .ToListAsync(cancellationToken);

        var ranked = records
            .OrderByDescending(r => verses[r.Slug].Count)
            .ThenBy(r => r.Slug, StringComparer.Ordinal)
            .ToList();
        var people = ranked.Where(r => r.Kind == EntityKind.Person).Take(MostPeople).ToList();
        var places = ranked.Where(r => r.Kind == EntityKind.Place && r.Location != null).Take(MostPlaces).ToList();
        var local = await EntityNames.Of(
            db, [.. people.Concat(places).Select(r => r.Id)], language, cancellationToken);

        return (
            [.. people.Select(r => new BookEntityResponse(r.Slug, EnumSpelling.Of(r.Kind), r.Name, verses[r.Slug].Count)
            {
                LocalName = local.GetValueOrDefault(r.Id),
            })],
            [.. places.Select(r => new BookEntityResponse(r.Slug, EnumSpelling.Of(r.Kind), r.Name, verses[r.Slug].Count)
            {
                LocalName = local.GetValueOrDefault(r.Id),
                Location = r.Location,
            })]);
    }
}

/// <summary>
/// The parts a book's own text marks, found by the word that marks them in the Hebrew.
///
/// <para>
/// Genesis opens its parts with <em>these are the generations of</em>, אֵלֶּה תּוֹלְדֹת, and once with
/// <em>this is the book of the generations of</em>: a verse where תּוֹלְדֹת follows אֵלֶּה or סֵפֶר opens a
/// part. The word is used in two more verses that open nothing — <em>after their generations</em>, in
/// 10:32 and 25:13 — and they are counted and said to open nothing.
/// </para>
///
/// <para>
/// The Psalms close each of their first four books with a blessing ending in <em>Amen</em>, the
/// only four verses of the book that say it: a book ends with the psalm the word is in.
/// </para>
/// </summary>
internal static class BookStructure
{
    private const int GenesisOrdinal = 1;
    private const int PsalmsOrdinal = 19;

    private const string Generations = "H8435";
    private const string These = "H428";
    private const string Book = "H5612";
    private const string This = "H2088";
    private const string Conjunction = "H9000";
    private const string Amen = "H543";

    private const string HebrewLanguage = "hbo";
    private const string ConstructState = "c";

    public const string GenerationsKind = "generations";
    public const string PsalterKind = "books";

    private sealed record HebrewWord(int Position, string Surface, string Trailer, string? Strong, string? State);

    public static async Task<BookStructureResponse?> Of(AppDbContext db, int book, CancellationToken cancellationToken)
    {
        var marker = book switch
        {
            GenesisOrdinal => Generations,
            PsalmsOrdinal => Amen,
            _ => null,
        };
        if (marker is null)
        {
            return null;
        }

        var text = await db.Texts
            .Where(t => t.Language == HebrewLanguage)
            .OrderBy(t => t.Kind != TextKind.CriticalEdition)
            .ThenBy(t => t.Id)
            .Select(t => (int?)t.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (text is null)
        {
            return null;
        }

        var inBook = db.VerseReferences.Where(r =>
            r.IsPrimary && r.CanonicalBook == book && r.Verse!.TextId == text);
        var marked = await inBook
            .Where(r => r.Verse!.Words.Any(w => w.StrongNumber == marker))
            .Select(r => new { r.VerseId, r.CanonicalChapter, r.CanonicalVerse })
            .ToListAsync(cancellationToken);
        if (marked.Count == 0)
        {
            return null;
        }

        var ids = marked.Select(m => m.VerseId).ToList();
        var words = (await db.Words
                .Where(w => ids.Contains(w.VerseId))
                .Select(w => new { w.VerseId, w.Position, w.Surface, w.Trailer, w.StrongNumber, w.Morphology })
                .ToListAsync(cancellationToken))
            .GroupBy(w => w.VerseId)
            .ToDictionary(
                verse => verse.Key,
                verse => verse.OrderBy(w => w.Position)
                    .Select(w => new HebrewWord(w.Position, w.Surface, w.Trailer, w.StrongNumber, StateOf(w.Morphology)))
                    .ToList());

        var verses = (await inBook
                .Select(r => new { r.CanonicalChapter, r.CanonicalVerse })
                .ToListAsync(cancellationToken))
            .Select(r => (r.CanonicalChapter, r.CanonicalVerse))
            .Distinct()
            .Order()
            .ToList();

        var markers = marked
            .OrderBy(m => m.CanonicalChapter).ThenBy(m => m.CanonicalVerse)
            .Select(m =>
            {
                var verse = words[m.VerseId];
                return marker == Generations
                    ? Heading(m.CanonicalChapter, m.CanonicalVerse, verse)
                    : new BookMarkerResponse(m.CanonicalChapter, m.CanonicalVerse, true, ToTheEnd(verse, marker));
            })
            .ToList();

        return marker == Generations
            ? new BookStructureResponse(GenerationsKind, markers, Opening(markers, verses))
            : new BookStructureResponse(PsalterKind, markers, Closing(markers, verses));
    }

    /// <summary>A verse using תּוֹלְדֹת, and whether it opens a part: the words from <em>these</em> through whose generations they are.</summary>
    private static BookMarkerResponse Heading(int chapter, int verse, List<HebrewWord> words)
    {
        var at = words.FindIndex(w => w.Strong == Generations);
        var before = at - 1;
        while (before >= 0 && words[before].Strong == Conjunction)
        {
            before--;
        }

        var opens = before >= 0 && words[before].Strong is These or Book;
        if (!opens)
        {
            return new BookMarkerResponse(chapter, verse, false, Joined(words, at, at));
        }

        var from = words[before].Strong == Book && before > 0 && words[before - 1].Strong == This ? before - 1 : before;
        var to = at + 1;
        while (to < words.Count - 1 && (words[to].Trailer.Length == 0 || words[to].State == ConstructState))
        {
            to++;
        }

        return new BookMarkerResponse(chapter, verse, true, Joined(words, from, Math.Min(to, words.Count - 1)));
    }

    private static string ToTheEnd(List<HebrewWord> words, string marker) =>
        Joined(words, words.FindIndex(w => w.Strong == marker), words.Count - 1);

    private static string Joined(List<HebrewWord> words, int from, int to)
    {
        var joined = new StringBuilder();
        for (var i = from; i <= to; i++)
        {
            joined.Append(words[i].Surface);
            if (i < to)
            {
                joined.Append(words[i].Trailer);
            }
        }

        return joined.ToString().Trim().TrimEnd('׃', '׀').Trim();
    }

    /// <summary>A part from the book's first verse to the one before the first heading, then one from each heading to the verse before the next.</summary>
    private static List<BookPartResponse> Opening(
        IReadOnlyList<BookMarkerResponse> markers,
        List<(int Chapter, int Verse)> verses)
    {
        var starts = markers.Where(m => m.Divides).Select(m => (m.Chapter, m.Verse)).ToList();
        if (starts.Count == 0 || starts[0] != verses[0])
        {
            starts.Insert(0, verses[0]);
        }

        var parts = new List<BookPartResponse>();
        for (var i = 0; i < starts.Count; i++)
        {
            var end = i + 1 < starts.Count ? verses[verses.IndexOf(starts[i + 1]) - 1] : verses[^1];
            parts.Add(new BookPartResponse(starts[i].Chapter, starts[i].Verse, end.Chapter, end.Verse));
        }

        return parts;
    }

    /// <summary>A part ending with each chapter holding a closing verse, and the last one running to the book's end.</summary>
    private static List<BookPartResponse> Closing(
        IReadOnlyList<BookMarkerResponse> markers,
        List<(int Chapter, int Verse)> verses)
    {
        var parts = new List<BookPartResponse>();
        var start = verses[0];
        foreach (var chapter in markers.Select(m => m.Chapter).Distinct())
        {
            var end = verses.Last(v => v.Chapter == chapter);
            parts.Add(new BookPartResponse(start.Chapter, start.Verse, end.Chapter, end.Verse));
            var next = verses.IndexOf(end) + 1;
            if (next >= verses.Count)
            {
                return parts;
            }

            start = verses[next];
        }

        parts.Add(new BookPartResponse(start.Chapter, start.Verse, verses[^1].Chapter, verses[^1].Verse));
        return parts;
    }

    private static string? StateOf(JsonDocument? morphology) =>
        morphology?.RootElement.TryGetProperty("state", out var state) == true
        && state.ValueKind == JsonValueKind.String
            ? state.GetString()
            : null;
}

/// <param name="Counts">How many chapters and verses each text gives the book, in its own numbering.</param>
/// <param name="People">The people the book names in the most verses, the most first.</param>
/// <param name="Places">The located places the book names in the most verses, the most first.</param>
/// <param name="Years">
/// The years from creation of the first and the last event the book tells, in the order of its
/// verses, in each reckoning that dates any of them, keyed by the reckoning's slug.
/// </param>
/// <param name="Structure">The parts the book's own text marks, where it marks them; null elsewhere.</param>
internal record BookAboutResponse(
    BookRefResponse Book,
    IList<BookTextCountResponse> Counts,
    IList<BookEntityResponse> People,
    IList<BookEntityResponse> Places,
    Dictionary<string, int[]> Years,
    BookStructureResponse? Structure);

internal record BookTextCountResponse(string Text, int Chapters, int Verses);

/// <param name="Verses">How many verses of the book name it.</param>
internal record BookEntityResponse(string Slug, string Kind, string Name, int Verses)
{
    public string? LocalName { get; init; }

    public EntityLocationResponse? Location { get; init; }
}

/// <param name="Kind">
/// <c>generations</c> for the headings of Genesis, <c>books</c> for the five books of the Psalms.
/// </param>
/// <param name="Markers">Every verse of the book using the marking word, in order.</param>
/// <param name="Parts">The parts the markers divide the book into, in the shared numbering.</param>
internal record BookStructureResponse(string Kind, IList<BookMarkerResponse> Markers, IList<BookPartResponse> Parts);

/// <param name="Divides">Whether the verse opens or closes a part; a use of the word in passing does neither.</param>
/// <param name="Words">The marking words as the Hebrew prints them.</param>
internal record BookMarkerResponse(int Chapter, int Verse, bool Divides, string Words);

internal record BookPartResponse(int FromChapter, int FromVerse, int ToChapter, int ToVerse);
