using Essenthos.Core.Berean;
using Essenthos.Core.Loading;

namespace Essenthos.Core.Conversion;

/// <summary>
/// What the Greek editions print as brackets, taken out of the words and kept as marks: Nestle's <c>[[…]]</c> and
/// <c>[…]</c> as doubtful spans with their number of brackets, the Stephanus <c>[προς …]</c> as subscriptions, and
/// Brenton's two only taken out. A span runs on through the verses of its chapter. The load's <c>edition-marks</c>
/// step (<see cref="EditionMarkLoader"/>), done to the words before they are written.
/// </summary>
internal sealed class EditionBrackets : ITextStage
{
    private const string Stephanus = "TR1550";
    private const string BrentonGreek = "GRCBRENT";

    public bool AppliesTo(string slug) => slug is NestleTextSource.Slug or Stephanus or BrentonGreek;

    public IReadOnlyList<string> Sources(string slug) => [];

    public void Apply(ConvertedText text, string resources)
    {
        static bool Bracketed(string value) => value.AsSpan().IndexOfAny('[', ']') >= 0;

        var chapters = text.Words()
            .Where(w => Bracketed(w.Word.Surface) || Bracketed(w.Word.Trailer))
            .Select(w => (w.Verse.Key.Book, w.Verse.Key.Chapter))
            .ToHashSet();
        if (chapters.Count == 0)
        {
            return;
        }

        var spans = new List<(List<ConvertedWord> Words, int Brackets)>();
        (List<ConvertedWord> Words, int Brackets)? open = null;
        foreach (var (verse, _, word) in text.Words().Where(w => chapters.Contains((w.Verse.Key.Book, w.Verse.Key.Chapter))))
        {
            if (word.Surface.StartsWith('['))
            {
                open = ([], word.Surface.TakeWhile(c => c == '[').Count());
                spans.Add(open.Value);
            }

            open?.Words.Add(word);
            if (open is not null && (word.Surface.Contains(']') || word.Trailer.Contains(']')))
            {
                open = null;
            }
        }

        if (open is { } unclosed)
        {
            spans.Remove(unclosed);
        }

        foreach (var (_, _, word) in text.Words())
        {
            if (Bracketed(word.Surface) || Bracketed(word.Trailer))
            {
                word.Surface = Strip(word.Surface);
                word.Trailer = Strip(word.Trailer);
                word.Normalised = word.Normalised is null ? null : Strip(word.Normalised);
            }
        }

        if (text.Slug == BrentonGreek)
        {
            return;
        }

        foreach (var (at, span) in spans.Index())
        {
            foreach (var word in span.Words)
            {
                if (text.Slug == Stephanus)
                {
                    word.Subscription = at + 1;
                }
                else
                {
                    word.Doubtful = at + 1;
                    word.Brackets = span.Brackets;
                }
            }
        }
    }

    private static string Strip(string value) => value.Replace("[", string.Empty).Replace("]", string.Empty);
}

/// <summary>
/// The Berean's supplied words, read from its tables, where its English prints them in brackets or braces: matched
/// word by word to the verse's words, a verse whose words do not match being left unmarked. The load's
/// <c>edition-marks</c> step for the Berean.
/// </summary>
internal sealed class BereanSupplied : ITextStage
{
    private const string Tables = "Berean/bsb_tables.tsv";

    public bool AppliesTo(string slug) => slug == BereanTextSource.Slug;

    public IReadOnlyList<string> Sources(string slug) => [Tables];

    public void Apply(ConvertedText text, string resources)
    {
        var ours = text.Verses.Where(v => v.Key.VerseLabel.Length == 0).ToDictionary(v => (v.Key.Book, v.Key.Chapter, v.Key.Verse));
        foreach (var verse in text.Verses)
        {
            foreach (var word in verse.Words)
            {
                word.Supplied = null;
                word.SuppliedMark = null;
            }
        }

        foreach (var (reference, rows) in BereanTable.Verses(Path.Combine(resources, Tables)))
        {
            if (!BereanTextSource.Address(reference, out var book, out var chapter, out var number)
                || !ours.TryGetValue((book, chapter, number), out var verse))
            {
                continue;
            }

            var words = verse.Words;
            var marked = new List<(List<int> Words, string? Mark)>();
            var at = 0;
            var matched = true;
            foreach (var row in rows.OrderBy(row => row.EnglishOrder))
            {
                (List<int> Words, string? Mark)? open = null;
                var openAt = -1;
                foreach (var (word, span, mark) in BereanWords.Marked(row.English))
                {
                    if (at >= words.Count || !BereanWords.Same(words[at].Surface, word))
                    {
                        matched = false;
                        break;
                    }

                    if (span is { } index)
                    {
                        if (open is null || openAt != index)
                        {
                            open = ([], mark);
                            openAt = index;
                            marked.Add(open.Value);
                        }

                        open.Value.Words.Add(at);
                    }
                    else
                    {
                        open = null;
                    }

                    at++;
                }

                if (!matched)
                {
                    break;
                }
            }

            if (!matched || at != words.Count)
            {
                continue;
            }

            // Numbered as the load groups them: by the first word of each group, in the verse.
            var run = 0;
            foreach (var (members, mark) in marked.Where(m => m.Words.Count > 0).OrderBy(m => m.Words.Min()))
            {
                run++;
                foreach (var member in members.Distinct())
                {
                    words[member].Supplied ??= run;
                    words[member].SuppliedMark ??= mark;
                }
            }
        }

        text.Provenance.Add(new("supplied", $"{Tables}: the Berean's brackets and braces"));
    }
}
