namespace Essenthos.Core.Loading;

/// <summary>
/// Young's square brackets, taken out of the words and kept as supplied spans (PRB-1993).
///
/// <para>
/// eBible's engylt prints the words Young supplied for the English the way his printed edition sets them, in square
/// brackets: "By His light I walk [through] darkness" (Job 29:3), "testifieth [to] me" (29:11). The American Standard's
/// file marks its italics with <c>\add</c> and the reader keeps them as <see cref="WordDraft.SuppliedSpan"/>; Young's
/// file has no markup at all, so the brackets reached the corpus as letters of the words (10,391 words read
/// <c>[through</c> and the like, none of them marked supplied) and the aligner linked them as if they rendered the
/// original. This gives them the same field the <c>\add</c> spans fill, with the brackets taken out of the letters.
/// </para>
///
/// <para>
/// Measured over the file (2026-11-23): 10,391 brackets open and as many close; none nests and none stands inside a
/// word but three. A span holds one word 10,040 times and up to 29 (Acts 8:37); 53 open a verse. One span runs across a
/// verse boundary (1 John 5:7-8), so the state is carried from verse to verse and the span continues under a new number
/// in the next verse, numbers counting from one in each verse as the field's do.
/// </para>
///
/// <list type="bullet">
/// <item>A word is in a span where its first letter is: <c>'[It is]</c> is supplied, the opening quotation mark being the
/// edition's; <c>sea -[monster]</c> (Young's hyphen before the supplied half of a compound) gives "-monster", supplied.</item>
/// <item>A bracket inside a word marks letters, not the word: <c>god[dess]</c> (1 Kings 11:5, 11:33), whose word renders
/// אֱלֹהֵי, reads "goddess" and is not supplied, as the Synodal's tokeniser decides a word by where it starts.</item>
/// <item><c>[evening -]present</c> (1 Kings 18:36): "evening" is supplied and "-present", whose letters stand after the
/// bracket closes, is not. A hyphen opening a word stays in its letters, as the reader leaves every opening mark.</item>
/// </list>
///
/// <para>
/// Not every bracket is a supplied word. Young also brackets text the Greek editions differ on, which he translated:
/// Acts 8:37, the heavenly witnesses of 1 John 5:7-8, Mark 15:3's "but he answered nothing", 1 John 2:23's second
/// clause. Those are the edition's own text, and <c>texts/YLT/corrections.tsv</c> takes the mark off them with
/// <c>unsupplied</c> rows, as Brenton's 1 Samuel 17 passage is; their brackets go all the same, the reader having no
/// field for a doubtful reading of a translation.
/// </para>
/// </summary>
internal static class SquareBrackets
{
    private const char Open = '[';

    private const char Close = ']';

    /// <summary>The chapters of one book with Young's brackets taken out and the words inside them supplied.</summary>
    public static List<ChapterDraft> Supplied(IEnumerable<ChapterDraft> chapters)
    {
        var open = false;
        var result = new List<ChapterDraft>();
        foreach (var chapter in chapters)
        {
            var verses = new List<VerseDraft>(chapter.Verses.Count);
            foreach (var verse in chapter.Verses)
            {
                // A span is numbered when its first word is met, so a bracket round letters only (god[dess]) takes no number.
                var spans = 0;
                var current = 0;
                var numbered = false;
                int Span()
                {
                    if (!numbered)
                    {
                        current = ++spans;
                        numbered = true;
                    }

                    return current;
                }

                var words = new List<WordDraft>(verse.Words.Count);
                foreach (var word in verse.Words)
                {
                    if (word.Surface.AsSpan().IndexOfAny(Open, Close) < 0 && word.Trailer.AsSpan().IndexOfAny(Open, Close) < 0)
                    {
                        words.Add(open ? word with { SuppliedSpan = Span() } : word);
                        continue;
                    }

                    int? supplied = null;
                    var lettered = false;
                    foreach (var character in word.Surface)
                    {
                        if (character == Open)
                        {
                            open = true;
                            numbered = false;
                        }
                        else if (character == Close)
                        {
                            open = false;
                        }
                        else if (!lettered && char.IsLetterOrDigit(character))
                        {
                            lettered = true;
                            supplied = open ? Span() : null;
                        }
                    }

                    foreach (var character in word.Trailer)
                    {
                        if (character == Open)
                        {
                            open = true;
                            numbered = false;
                        }
                        else if (character == Close)
                        {
                            open = false;
                        }
                    }

                    words.Add(word with
                    {
                        Surface = Without(word.Surface),
                        Trailer = Without(word.Trailer),
                        SuppliedSpan = supplied,
                    });
                }

                verses.Add(verse with { Words = words });
            }

            result.Add(chapter with { Verses = verses });
        }

        return result;
    }

    private static string Without(string value) =>
        value.AsSpan().IndexOfAny(Open, Close) < 0 ? value : value.Replace(Open.ToString(), string.Empty).Replace(Close.ToString(), string.Empty);
}
