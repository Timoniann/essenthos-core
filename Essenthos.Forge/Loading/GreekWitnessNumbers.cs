using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading;

/// <param name="Book">The canonical book, with the chapter and verse of the shared frame.</param>
/// <param name="Folded">The word, folded as its searchable form is, so every edition's spelling of it matches.</param>
/// <param name="Tagged">The number the editions write on it.</param>
/// <param name="Read">The Strong number the word is.</param>
/// <param name="Why">What says so.</param>
internal sealed record WitnessNumberReading(
    int Book,
    int Chapter,
    int Verse,
    string Folded,
    string Tagged,
    string Read,
    string Why);

/// <summary>
/// Strong numbers the Greek witnesses write on a word that is not the word, read instead as the
/// number the word is.
///
/// <para>
/// The critical editions arrive with a Strong number on every word, supplied by their digitisers
/// rather than printed by their editors, and where one of those numbers names a different thing
/// the corpus names it too: the number is what the encyclopedia is reached by. So a number is read
/// here only where the word itself says otherwise and Strong's concordance files that word, at that
/// verse, under another number. These are this project's readings, not the editions'.
/// </para>
/// </summary>
internal static class GreekWitnessNumbers
{
    private const int Luke = 42;

    private const string Greek = "grc";

    public static readonly IReadOnlyList<WitnessNumberReading> All =
    [
        new(Luke, 3, 26, "ιωδα", "G2448", "G2455",
            "Joda, a man of Luke's genealogy. Nestle, Tischendorf and Westcott-Hort print Ἰωδά and tag it "
            + "G2448, Ἰουδά, the region of Judah that Luke 1:39 names. Strong's files the man of Luke 3:26 "
            + "under G2455, Ἰούδας, the number the Textus Receptus and the Byzantine Textform write on the "
            + "Ἰούδα they print at the same place."),
    ];

    /// <summary>The text with every such number read, and every other word as it was.</summary>
    public static TextSource Apply(TextSource source)
    {
        var readings = All.ToLookup(r => (r.Book, r.Chapter, r.Verse));
        if (!source.Books.Any(book => readings.Any(r => r.Key.Book == book.CanonicalOrdinal)))
        {
            return source;
        }

        return source with
        {
            Books =
            [
                .. source.Books.Select(book => book with
                {
                    Chapters =
                    [
                        .. book.Chapters.Select(chapter => chapter with
                        {
                            Verses =
                            [
                                .. chapter.Verses.Select(verse =>
                                    readings[(book.CanonicalOrdinal, chapter.Number, verse.Number)] is var here
                                    && here.Any()
                                        ? verse with { Words = [.. verse.Words.Select(word => Read(word, here))] }
                                        : verse),
                            ],
                        }),
                    ],
                }),
            ],
        };
    }

    private static WordDraft Read(WordDraft word, IEnumerable<WitnessNumberReading> readings)
    {
        var folded = WordFolding.Fold(word.Surface, Greek);
        var reading = readings.FirstOrDefault(r => r.Folded == folded && r.Tagged == word.StrongNumber);
        return reading is null ? word : word with { StrongNumber = reading.Read };
    }
}
