namespace Essenthos.Core.Loading.Frame;

/// <param name="Book">The canonical ordinal of the book, which is how the edition is addressed here
/// even though its own numbering is what the rest of this record describes.</param>
/// <param name="Piece">
/// Which of the pieces printed at this address it is, counting from one. An edition that prints
/// <c>2:35</c> and then <c>2:35a</c> to <c>2:35o</c> has sixteen pieces at 2:35, and the
/// versification data names them <c>2:35.1</c> to <c>2:35.16</c> — so the base verse is the first
/// piece and the letter is not part of the count. An address printed whole has one piece and is
/// not divided, which is a different thing from being divided into one.
/// </param>
internal readonly record struct PrintedVerse(int Book, int Chapter, int Verse, int Piece, int Length);

/// <summary>
/// What an edition prints, in its own numbering: which verses it has, which one ends a chapter,
/// how many pieces stand at an address and how long each of them is.
///
/// The versification data describes more than one edition per tradition — the Greek of Rahlfs, the
/// Greek of NETS, the Greek that runs the additions into one verse — and states, beside every rule,
/// the test that tells them apart: <em>this numbering is the one where Exodus 21 ends at verse 36</em>.
/// A test can only be answered by the edition being placed, which is why a frame is built for a
/// text and not once for a tradition.
/// </summary>
internal sealed class EditionShape
{
    private readonly Dictionary<(int Book, int Chapter, int Verse), List<int>> lengths;

    private readonly Dictionary<(int Book, int Chapter), int> lastVerse;

    private readonly HashSet<int> books;

    public EditionShape(IEnumerable<PrintedVerse> verses)
    {
        lengths = [];
        lastVerse = [];
        books = [];

        foreach (var verse in verses.OrderBy(v => v.Book).ThenBy(v => v.Chapter).ThenBy(v => v.Verse)
                     .ThenBy(v => v.Piece))
        {
            var address = (verse.Book, verse.Chapter, verse.Verse);
            if (!lengths.TryGetValue(address, out var pieces))
            {
                pieces = [];
                lengths[address] = pieces;
            }

            pieces.Add(verse.Length);

            books.Add(verse.Book);

            var chapter = (verse.Book, verse.Chapter);
            if (!lastVerse.TryGetValue(chapter, out var last) || verse.Verse > last)
            {
                lastVerse[chapter] = verse.Verse;
            }
        }
    }

    /// <summary>
    /// The edition as the versification data's tests ask about it. A test names the pieces at an
    /// address by number — <c>Gen.6:1.2</c> is the second thing printed at 6:1 — so the label
    /// becomes a position, and the base verse is the first piece whether or not the edition also
    /// prints an <c>a</c>.
    /// </summary>
    public static EditionShape Of(
        IEnumerable<(int Book, int Chapter, int Number, string Label, int Length)> verses) =>
        new(verses
            .GroupBy(verse => (verse.Book, verse.Chapter, verse.Number))
            .SelectMany(address => address
                .OrderBy(verse => verse.Label, StringComparer.Ordinal)
                .Select((verse, piece) =>
                    new PrintedVerse(verse.Book, verse.Chapter, verse.Number, piece + 1, verse.Length))));

    /// <summary>
    /// Whether the edition carries this book at all. A test about a book it does not carry is not
    /// failed by it — an Old Testament edition is not the wrong numbering scheme for having no
    /// gospel of Mark — so the condition goes unanswered instead.
    /// </summary>
    public bool Carries(int book) => books.Contains(book);

    /// <param name="piece">
    /// Which piece is meant, counting from one, or zero for the whole verse. Naming a piece asks
    /// whether the edition divides the address at all: an edition that prints 37:2 whole has no
    /// <c>37:2.1</c>, which is what the condition <c>Exo.37:2.1=NotExist</c> is there to find out.
    /// </param>
    public bool Prints(CanonicalReference reference, int piece) =>
        lengths.TryGetValue((reference.Book, reference.Chapter, reference.Verse), out var pieces) &&
        (piece == 0 || (pieces.Count > 1 && piece <= pieces.Count));

    public bool EndsChapter(CanonicalReference reference) =>
        lastVerse.TryGetValue((reference.Book, reference.Chapter), out var last) && last == reference.Verse;

    /// <summary>
    /// How much text stands there, in characters. Zero for an address the edition does not print,
    /// which is why a comparison against it cannot be answered rather than being answered wrongly.
    /// </summary>
    public int Length(CanonicalReference reference, int piece)
    {
        if (!lengths.TryGetValue((reference.Book, reference.Chapter, reference.Verse), out var pieces))
        {
            return 0;
        }

        return piece == 0 ? pieces.Sum()
            : pieces.Count > 1 && piece <= pieces.Count ? pieces[piece - 1]
            : 0;
    }
}
