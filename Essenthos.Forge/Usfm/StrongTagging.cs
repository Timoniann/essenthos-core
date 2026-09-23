namespace Essenthos.Core.Usfm;

/// <param name="Verses">Verses carrying at least one tag.</param>
/// <param name="Tags">Words tagged with a number, over those verses.</param>
/// <param name="Numbers">Distinct numbers per verse, summed over those verses.</param>
/// <param name="Places">
/// Places per verse a number stands at, summed: a run of neighbouring words under one number is one
/// place, however many words it holds.
/// </param>
internal readonly record struct StrongTaggingMeasure(int Verses, int Tags, int Numbers, int Places)
{
    /// <summary>
    /// How many separate places in its verse each number stands at. One when each number names the
    /// word, or the phrase, that renders it.
    /// </summary>
    public double PlacesPerNumber => Numbers == 0 ? 0 : (double)Places / Numbers;

    public override string ToString() =>
        $"{Tags} tags over {Verses} verses, each number at {PlacesPerNumber:F2} places in its verse";
}

/// <summary>
/// Whether an edition's Strong tagging says which word renders which original word, or only which
/// numbers belong to the verse.
///
/// <para>
/// eBible ships the second kind under the same marker as the first. The layer under the American
/// Standard, the World English Bible, the Douay-Rheims and both French texts puts twenty-three tags
/// on a verse from a pool of eight numbers, most of them on function words: Genesis 1:1 tags "In",
/// "God", "and" and "earth" with H8064, the heavens, and uses H430 nowhere. Its proper nouns land
/// right, which is why a spot check passes and a count does not.
/// </para>
///
/// <para>
/// So the count is the test, and it is language-independent: at how many separate places in its
/// verse each number stands. Counting words instead cannot tell the spray from a tagging made by
/// hand, because a hand tagging marks phrases — the Reina-Valera puts one number over "en el
/// principio" — and the reader gives every word of the phrase the number. Measured over the whole of
/// each file with this reader:
/// </para>
///
/// <code>
///                            words per number   places per number
/// Luther 1912 (hand-made)          1.13               1.13
/// Reina-Valera 1909                2.01               1.15
/// American Standard 1901           2.50               2.12
/// World English Bible              2.46               2.09
/// </code>
///
/// <para>
/// A word that recurs in its verse puts its number at two places honestly, so a word-level tagging
/// sits a little above one. The ceiling is halfway between the two groups.
/// </para>
/// </summary>
internal static class StrongTagging
{
    public const double WordLevelCeiling = 1.6;

    public static StrongTaggingMeasure Measure(IEnumerable<UsfmBook> books)
    {
        int verses = 0, tags = 0, numbers = 0, places = 0;

        foreach (var verse in books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses))
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string? previous = null;

            foreach (var word in verse.Words)
            {
                if (word.StrongNumber is { } number)
                {
                    tags++;
                    seen.Add(number);
                    if (!string.Equals(number, previous, StringComparison.Ordinal))
                    {
                        places++;
                    }
                }

                previous = word.StrongNumber;
            }

            if (seen.Count > 0)
            {
                verses++;
                numbers += seen.Count;
            }
        }

        return new StrongTaggingMeasure(verses, tags, numbers, places);
    }

    /// <summary>
    /// Whether the numbers an edition carries may be stored on its words. An edition that tags
    /// nothing has nothing to refuse.
    /// </summary>
    public static bool IsWordLevel(IEnumerable<UsfmBook> books) =>
        Measure(books).PlacesPerNumber <= WordLevelCeiling;
}
