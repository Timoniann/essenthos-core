using Essenthos.Core.Berean;
using Essenthos.Core.Loading;

namespace Essenthos.Core.Conversion;

/// <summary>
/// What the Greek editions print as brackets, taken out of the words and kept as marks: Nestle's <c>[[…]]</c> and
/// <c>[…]</c> as doubtful spans with their number of brackets, the Stephanus <c>[προς …]</c> as subscriptions, and
/// Brenton's two only taken out. A span runs on through the verses of its chapter. Done to the words before they are
/// written.
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

/// <summary>
/// Charles's signs in his Enoch and Jubilees, taken out of the trailers they are printed in and kept as marks: ( ) round
/// words supplied for the sense as supplied, 〈 〉 and &lt; &gt; round a restoration as restored, [ ] round an interpolation
/// as doubtful, † † round a corruption as corrupt, ┌ ┐ round words the Greek has and the Ethiopic lacks as not in the
/// Ethiopic, and ╓ ╖ round words the Ethiopic has and the Greek lacks as not in the Greek. A span runs on through the
/// verses of its book (Enoch 3:1-5:1 is one ╓ ╖). An opening sign is printed in the trailer of the word before its span,
/// or of an empty word heading a verse, which goes when the sign was all it held.
///
/// <para>
/// A sign is taken out only where its span is certain: the sign that closes it matches the last one opened in the same
/// book, and the span holds a word. Daggers open and close alike, so they pair within a verse, and only in a verse that
/// prints an even number of them. What fails stays printed and marks nothing: the scan's odd daggers (Enoch 39:1, 75:5,
/// 83:11), its two closing brackets for a misread letter (Jubilees 23:11 "Land", 40:2 "to]d") and the (?) Charles prints
/// after a word he queries (Enoch 30:2), which encloses no word.
/// </para>
/// </summary>
internal sealed class CharlesSigns : ITextStage
{
    private const char Dagger = '†';

    private static readonly Dictionary<char, char> Closing = new()
    {
        ['('] = ')',
        ['〈'] = '〉',
        ['<'] = '>',
        ['['] = ']',
        ['┌'] = '┐',
        ['╓'] = '╖',
    };

    private static readonly HashSet<char> Closers = [.. Closing.Values];

    public bool AppliesTo(string slug) => slug == CharlesTextSource.Slug;

    public IReadOnlyList<string> Sources(string slug) => [];

    /// <summary>Whether the character is one of Charles's signs.</summary>
    public static bool IsSign(char c) => c == Dagger || Closing.ContainsKey(c) || Closers.Contains(c);

    /// <summary>A sign as printed: the index of the word whose trailer holds it, and where in the trailer.</summary>
    private readonly record struct Sign(int Word, int At);

    private sealed class Span(char opener, Sign open)
    {
        public char Opener { get; } = opener;

        public Sign Open { get; } = open;

        public Sign? Close { get; set; }

        public List<int> Words { get; } = [];
    }

    public void Apply(ConvertedText text, string resources)
    {
        var words = text.Words().Select(w => (w.Verse, w.Word)).ToList();
        var spans = new List<Span>();
        var open = new List<Span>();
        int? book = null;
        ConvertedVerse? current = null;
        var pairsDaggers = false;
        for (var i = 0; i < words.Count; i++)
        {
            var (verse, word) = words[i];
            if (verse.Key.Book != book)
            {
                open.Clear();
                book = verse.Key.Book;
            }

            if (verse != current)
            {
                open.RemoveAll(span => span.Opener == Dagger);
                current = verse;
                pairsDaggers = verse.Words.Sum(w => w.Trailer.Count(c => c == Dagger)) % 2 == 0;
            }

            if (word.Surface.Length > 0)
            {
                foreach (var span in open)
                {
                    span.Words.Add(i);
                }
            }

            for (var at = 0; at < word.Trailer.Length; at++)
            {
                var c = word.Trailer[at];
                var top = open.Count > 0 ? open[^1].Opener : (char?)null;
                var closes = c == Dagger ? pairsDaggers && top == Dagger : top is { } opener && Closing.GetValueOrDefault(opener) == c;
                if (closes)
                {
                    open[^1].Close = new Sign(i, at);
                    open.RemoveAt(open.Count - 1);
                }
                else if (Closing.ContainsKey(c) || (c == Dagger && pairsDaggers))
                {
                    var span = new Span(c, new Sign(i, at));
                    open.Add(span);
                    spans.Add(span);
                }
            }
        }

        var marked = spans.Where(span => span.Close is not null && span.Words.Count > 0).OrderBy(span => span.Words[0]).ToList();
        foreach (var signs in marked.SelectMany(span => new[] { span.Open, span.Close!.Value }).GroupBy(sign => sign.Word))
        {
            var word = words[signs.Key].Word;
            var at = signs.Select(sign => sign.At).ToHashSet();
            var trailer = new string([.. word.Trailer.Where((_, index) => !at.Contains(index))]);
            while (trailer.Contains("  ", StringComparison.Ordinal))
            {
                trailer = trailer.Replace("  ", " ", StringComparison.Ordinal);
            }

            word.Trailer = trailer;
        }

        Number(marked, words, ['('], perVerse: true, word => word.Supplied, (word, n) => word.Supplied = n);
        Number(marked, words, ['〈', '<'], perVerse: true, word => word.Restored, (word, n) => word.Restored = n);
        Number(marked, words, ['['], perVerse: false, word => word.Doubtful, (word, n) => word.Doubtful = n);
        Number(marked, words, [Dagger], perVerse: false, word => word.Corrupt, (word, n) => word.Corrupt = n);
        Number(marked, words, ['┌'], perVerse: false, word => word.NotInEthiopic, (word, n) => word.NotInEthiopic = n);
        Number(marked, words, ['╓'], perVerse: false, word => word.NotInGreek, (word, n) => word.NotInGreek = n);

        foreach (var verse in text.Verses)
        {
            verse.Words.RemoveAll(word => word.Surface.Length == 0 && word.Trailer.Length == 0);
        }

        var left = text.Verses.SelectMany(verse => verse.Words).Sum(word => word.Trailer.Count(IsSign));
        var counts = string.Join(", ", marked.GroupBy(span => Kind(span.Opener)).OrderBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => $"{g.Key} {g.Count()}"));
        text.Provenance.Add(new("charles-signs", $"Charles's signs taken out as marks, spans: {counts}; {left} sign(s) left printed"));
    }

    private static string Kind(char opener) => opener switch
    {
        '(' => "supplied",
        '〈' or '<' => "restored",
        '[' => "doubtful",
        Dagger => "corrupt",
        '┌' => "not-in-ethiopic",
        _ => "not-in-greek",
    };

    /// <summary>
    /// Numbers the spans of one kind as the words product counts them: within the verse for supplied and restored words,
    /// by the first word there, and through the text for the rest. A span wholly inside another of its kind adds no number.
    /// </summary>
    private static void Number(List<Span> marked, List<(ConvertedVerse Verse, ConvertedWord Word)> words, char[] openers, bool perVerse,
        Func<ConvertedWord, int?> get, Action<ConvertedWord, int> set)
    {
        var run = 0;
        var runs = new Dictionary<ConvertedVerse, int>();
        foreach (var span in marked.Where(span => openers.Contains(span.Opener)))
        {
            IEnumerable<(ConvertedVerse Verse, List<int> Words)> parts = perVerse
                ? span.Words.GroupBy(i => words[i].Verse).Select(g => (g.Key, g.ToList()))
                : [(words[span.Words[0]].Verse, span.Words)];
            foreach (var (verse, members) in parts)
            {
                var unmarked = members.Where(i => get(words[i].Word) is null).ToList();
                if (unmarked.Count == 0)
                {
                    continue;
                }

                var n = perVerse ? runs[verse] = runs.GetValueOrDefault(verse) + 1 : ++run;
                foreach (var i in unmarked)
                {
                    set(words[i].Word, n);
                }
            }
        }
    }
}
