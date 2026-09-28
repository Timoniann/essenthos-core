using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading;

/// <summary>
/// The verses eBible's Brenton Greek begins at another word than Brenton's own English does, begun
/// again where the English begins them, one by one as <c>BrentonDivisions.json</c> lists them.
///
/// <para>
/// The Greek is the page Brenton translated and the English stands beside it with the same verse
/// numbers, so where the two begin a verse at different words one of them is wrong. In 235 places the
/// file's marker stands a clause off — in Nehemiah 12:18 it leaves the verse two words, <c>Τῷ
/// Ἰωαρὶβ,</c>, and hands verse 17 the next two names — and each of them is moved only where a Greek
/// edition begins the verse where the English does: the Greek Wikisource text GLAUx annotates,
/// Swete's own division, or the English Bible's number Swete prints in brackets inside his text. Where
/// the English divides otherwise and no Greek edition with it, the difference is the translation's
/// and the file's marker stays; so do the Septuagint's own verses, missing, lettered or in its own
/// order.
/// </para>
///
/// <para>
/// Each division names the words that change sides as the file prints them, so nothing moves where
/// the file does not read so: a verse that reads otherwise stops the read, because then the file is
/// not the one the list was drawn up against. The words themselves are the file's; only the verse
/// they stand in changes.
/// </para>
/// </summary>
internal static class BrentonDivisions
{
    private const string Resource = "Essenthos.Core.Loading.BrentonDivisions.json";

    /// <summary>What the text's row says about the divisions, on a cold load and on a warm one.</summary>
    public const string Note =
        "Modified: 235 verses eBible's file begins at another word than Brenton's English are begun by "
        + "Essenthos where the English begins them, each where a Greek edition does too — the Greek Wikisource "
        + "text GLAUx annotates, Swete's division, or the English numbering Swete prints in brackets; the words "
        + "are the file's.";

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    public static readonly IReadOnlyList<BrentonDivision> All = ReadList();

    /// <summary>A book's chapters with its divisions made, in the order the list gives them.</summary>
    public static IReadOnlyList<ChapterDraft> Apply(
        int canonical,
        IReadOnlyList<ChapterDraft> chapters,
        IReadOnlyList<BrentonDivision>? list = null)
    {
        var divisions = (list ?? All).Where(d => d.Book == canonical).ToList();
        if (divisions.Count == 0)
        {
            return chapters;
        }

        var order = chapters
            .SelectMany(chapter => chapter.Verses.Select(verse => (Chapter: chapter.Number, verse.Number, verse.Label)))
            .ToList();
        var words = chapters
            .SelectMany(chapter => chapter.Verses.Select(verse => verse.Words.ToList()))
            .ToList();

        foreach (var division in divisions)
        {
            var at = order.IndexOf(division.Address);
            if (at < 1 || order[at - 1] != division.PreviousAddress)
            {
                throw Unread(division, "does not follow the verse it names as the one before it, or is not there at all");
            }

            Move(division, words[at - 1], words[at]);
        }

        var next = 0;
        return
        [
            .. chapters.Select(chapter => chapter with
            {
                Verses = [.. chapter.Verses.Select(verse => verse with { Words = words[next++] })],
            }),
        ];
    }

    /// <summary>
    /// The division made on the words of the verse before and of the verse itself. A paragraph the
    /// edition opens before the verse stays at its head, whichever words now begin it.
    /// </summary>
    private static void Move(BrentonDivision division, List<WordDraft> previous, List<WordDraft> verse)
    {
        var count = division.Count;
        var head = verse[0];
        if (division.Begins is { } begins)
        {
            if (count >= previous.Count || Printed(previous.TakeLast(count)) != begins)
            {
                throw Unread(division, $"does not end the verse before it with \"{begins}\"");
            }

            var moved = previous.GetRange(previous.Count - count, count);
            previous.RemoveRange(previous.Count - count, count);
            verse.InsertRange(0, moved);
        }
        else
        {
            var ends = division.Ends!;
            if (count >= verse.Count || Printed(verse.Take(count)) != ends)
            {
                throw Unread(division, $"does not begin with \"{ends}\"");
            }

            previous.AddRange(verse.GetRange(0, count));
            verse.RemoveRange(0, count);
        }

        if (head.Break is { } opening)
        {
            var within = verse.Exists(word => ReferenceEquals(word, head)) ? verse : previous;
            within[within.FindIndex(word => ReferenceEquals(word, head))] = head with { Break = null };
            verse[0] = verse[0] with { Break = opening };
        }
    }

    /// <summary>Words as the file prints them, which is how a division names what it moves.</summary>
    public static string Printed(IEnumerable<WordDraft> words) =>
        string.Concat(words.Select(word => word.Surface + word.Trailer)).TrimEnd();

    private static InvalidOperationException Unread(BrentonDivision division, string what) =>
        new($"Brenton's Greek {BookReferences.Name(division.Book)} {division.Chapter}:{division.Verse} {what}. The "
            + "file is not the one BrentonDivisions.json was drawn up against: read the verses against Brenton's "
            + "English again and correct the list before loading.");

    private static IReadOnlyList<BrentonDivision> ReadList()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<BrentonDivisionList>(stream, Shape)?.Divisions ?? [];
    }

    private sealed record BrentonDivisionList(IReadOnlyList<BrentonDivision> Divisions);
}

/// <summary>
/// One verse of Brenton's Greek begun at another word: the words the file prints at the end of the
/// verse before that begin it, or the words it prints at its own head that end the verse before.
/// </summary>
/// <param name="Book">The book, by its canonical ordinal.</param>
/// <param name="Verse">The verse as the file numbers it, with its letter where it has one.</param>
/// <param name="Previous">The verse before it, <c>chapter:verse</c>, which is another chapter's last verse at a chapter's head.</param>
/// <param name="Witnesses">What else begins the verse at exactly that word.</param>
internal sealed record BrentonDivision(
    int Book,
    int Chapter,
    string Verse,
    string Previous,
    string? Begins,
    string? Ends,
    IReadOnlyList<string> Witnesses,
    string? Note)
{
    public (int Chapter, int Number, string Label) Address => At(Chapter, Verse);

    public (int Chapter, int Number, string Label) PreviousAddress =>
        Previous.Split(':') is [var chapter, var verse]
            ? At(int.Parse(chapter, System.Globalization.CultureInfo.InvariantCulture), verse)
            : throw new InvalidOperationException(
                $"\"{Previous}\" is not a chapter and a verse. Write the verse before {Chapter}:{Verse} as chapter:verse.");

    /// <summary>How many of the file's words change sides: a mark standing alone is part of the word before it.</summary>
    public int Count => (Begins ?? Ends ?? string.Empty)
        .Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .Count(token => token.Any(char.IsLetterOrDigit));

    private static (int Chapter, int Number, string Label) At(int chapter, string verse)
    {
        var digits = verse.TakeWhile(char.IsAsciiDigit).Count();
        return (chapter, int.Parse(verse[..digits], System.Globalization.CultureInfo.InvariantCulture), verse[digits..]);
    }
}
