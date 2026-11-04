using Essenthos.Core.Loading;
using Essenthos.Core.Products;
using Essenthos.Core.XmlBible;

namespace Essenthos.Core.Conversion;

/// <summary>
/// One repair a text's corrections file declares. Every repair names what the source reads where it
/// applies (<see cref="Before"/>), so a source that changed upstream stops the conversion instead of
/// taking a repair somewhere it was never meant.
/// </summary>
/// <param name="Own">
/// A word key (<c>1.1:1.3</c>) for <c>letters</c>, <c>drop</c> and <c>strong</c>; a verse key (<c>1.1:1</c>) for the others.
/// A <c>strong</c> row's key is the word's as the words product writes it, after every other row and stage.
/// </param>
/// <param name="Before">For <c>block</c>, the further verses of the chapter the source stores the block under, space-separated.</param>
/// <param name="Kind">
/// <c>verse</c>: the source's verse, markup and all, is read as <see cref="After"/> before it is divided into words;
/// <c>letters</c>: the word's letters <see cref="Before"/> become <see cref="After"/>;
/// <c>prepend</c> / <c>append</c>: the words of <see cref="After"/> open / close the verse;
/// <c>drop</c>: the word, which reads <see cref="Before"/>, is not the edition's and goes;
/// <c>block</c>: the source prints the verses <see cref="Own"/> and <see cref="Before"/> as one block and stores it under each
/// of their numbers; it is one verse, at <see cref="Own"/>, stating all of them, and the copies, which must read the same, go;
/// <c>strong</c>: the word's Strong number becomes <see cref="After"/>, or none where it is empty. <see cref="Before"/> is the word
/// as it reads and, after a space, the number it carries until the row applies (none written where it carries none), so a row
/// stops the conversion once the word or its number is no longer what the row was written against. It applies after the
/// converter's stages, so a person's number stands over every number a stage lays, moves or clears.
/// </param>
internal sealed record TextCorrection(string Own, string Kind, string? Before, string? After, string Why)
{
    public const string Verse = "verse";
    public const string Letters = "letters";
    public const string Prepend = "prepend";
    public const string Append = "append";
    public const string Drop = "drop";
    public const string Block = "block";
    public const string Strong = "strong";

    public static readonly ProductSchema<TextCorrection> Schema = new ProductSchema<TextCorrection>()
        .Text("own", c => c.Own)
        .Text("kind", c => c.Kind)
        .OptionalText("before", c => c.Before)
        .OptionalText("after", c => c.After)
        .Text("why", c => c.Why)
        .Reads(row => new TextCorrection(row.Text(0), row.Text(1), row.TextOrNull(2), row.TextOrNull(3), row.Text(4)));
}

/// <summary>
/// The repairs of one text, read from <c>texts/&lt;SLUG&gt;/corrections.tsv</c> and applied by the converter:
/// <c>verse</c> rows inside the reader (<see cref="SourceRepairs"/>), the word rows to what it read
/// (<see cref="Apply"/>). Every row must find what it says the source reads, once.
/// </summary>
internal sealed class TextCorrections
{
    private readonly string _slug;
    private readonly List<TextCorrection> _rows;

    private TextCorrections(string slug, List<TextCorrection> rows)
    {
        _slug = slug;
        _rows = rows;
    }

    public static string ProductOf(string text) => $"texts/{text}/corrections";

    public IReadOnlyList<TextCorrection> Rows => _rows;

    /// <summary>The words <c>prepend</c> and <c>append</c> rows added, which no source printed where they now stand.</summary>
    public HashSet<WordDraft> Added { get; } = new(ReferenceEqualityComparer.Instance);

    public static TextCorrections None(string slug) => new(slug, []);

    public static TextCorrections Read(ProductStore store, string slug)
    {
        if (!store.Exists(ProductOf(slug)))
        {
            return None(slug);
        }

        using var reader = store.Open(ProductOf(slug), TextCorrection.Schema);
        var rows = reader.ReadAll();
        foreach (var row in rows)
        {
            if (row.Kind is not (TextCorrection.Verse or TextCorrection.Letters or TextCorrection.Prepend or TextCorrection.Append
                or TextCorrection.Drop or TextCorrection.Block or TextCorrection.Strong))
            {
                throw new FormatException($"{slug} corrections: \"{row.Kind}\" at {row.Own} is not a kind of correction.");
            }
        }

        return new TextCorrections(slug, rows);
    }

    /// <summary>The <c>verse</c> rows, as the repairs a reader applies to its source's verses before it divides them.</summary>
    public TextRepairs SourceRepairs()
    {
        var verses = _rows
            .Where(row => row.Kind == TextCorrection.Verse)
            .Select(row =>
            {
                var key = VerseKey.Parse(row.Own);
                return new VerseRepair(key.Book, key.Chapter, key.Verse, row.Before!, row.After!);
            })
            .ToList();
        return new TextRepairs(_slug, verses, string.Empty);
    }

    /// <summary>
    /// Every <c>verse</c> row against the verse its source prints, before the reader applies them: a reader keeps
    /// the source's verse where a repair does not meet it, so a row that no longer meets its verse stops here.
    /// </summary>
    public void CheckVerses(Func<int, int, int, string?> sourceVerse)
    {
        foreach (var row in _rows.Where(row => row.Kind == TextCorrection.Verse))
        {
            var key = VerseKey.Parse(row.Own);
            if (sourceVerse(key.Book, key.Chapter, key.Verse) != row.Before)
            {
                throw Unmatched(row, "the source does not print the verse as the row's before says");
            }
        }
    }

    /// <summary>The word rows applied to what the reader read; a note's anchor follows its word.</summary>
    public TextSource Apply(TextSource source)
    {
        if (_rows.Count == 0)
        {
            return source;
        }

        var byVerse = _rows
            .Where(row => row.Kind is not (TextCorrection.Verse or TextCorrection.Block or TextCorrection.Strong))
            .GroupBy(row => row.Kind is TextCorrection.Letters or TextCorrection.Drop
                ? WordKey.Parse(row.Own, _slug).Verse
                : VerseKey.Parse(row.Own))
            .ToDictionary(group => group.Key, group => group.ToList());
        var blocks = _rows
            .Where(row => row.Kind == TextCorrection.Block)
            .ToDictionary(row => VerseKey.Parse(row.Own), row => (Row: row, Copies: Copies(row)));
        if (byVerse.Count == 0 && blocks.Count == 0)
        {
            return source;
        }

        var met = new HashSet<VerseKey>();
        var books = source.Books.Select(book => book with
        {
            Chapters =
            [
                .. book.Chapters.Select(chapter => chapter with
                {
                    Verses =
                    [
                        .. Blocks(book.CanonicalOrdinal, chapter, blocks, met).Select(verse =>
                        {
                            var key = new VerseKey(book.CanonicalOrdinal, chapter.Number, verse.Number, verse.Label);
                            if (!byVerse.TryGetValue(key, out var rows))
                            {
                                return verse;
                            }

                            met.Add(key);
                            var (words, read) = Corrected(verse.Words, rows);
                            return verse with
                            {
                                Words = words,
                                Notes =
                                [
                                    .. verse.Notes.Select(note => note with
                                    {
                                        AnchorWordPosition = note.AnchorWordPosition is { } anchor && read.IndexOf(anchor) is var at and >= 0 ? at + 1 : null,
                                    }),
                                ],
                            };
                        }),
                    ],
                }),
            ],
        }).ToList();

        foreach (var (key, rows) in byVerse)
        {
            if (!met.Contains(key))
            {
                throw Unmatched(rows[0], "the text has no such verse");
            }
        }

        foreach (var (key, (row, copies)) in blocks)
        {
            if (!met.Contains(key) || !copies.All(met.Contains))
            {
                throw Unmatched(row, "the text has no such verse");
            }
        }

        return source with { Books = books };
    }

    /// <summary>
    /// The <c>strong</c> rows applied to the converted text, after its stages: each sets or clears one word's number, and
    /// stops the conversion where the word does not read, or does not carry, what the row's <c>before</c> says.
    /// </summary>
    /// <returns>How many words the rows numbered and how many they left without a number.</returns>
    public (int Set, int Cleared) ApplyNumbers(ConvertedText text)
    {
        var rows = _rows.Where(row => row.Kind == TextCorrection.Strong).ToList();
        if (rows.Count == 0)
        {
            return (0, 0);
        }

        var verses = text.Verses.ToDictionary(verse => verse.Key);
        int set = 0, cleared = 0;
        foreach (var row in rows)
        {
            var key = WordKey.Parse(row.Own, _slug);
            if (!verses.TryGetValue(key.Verse, out var verse) || key.Position < 1 || key.Position > verse.Words.Count)
            {
                throw Unmatched(row, "the text has no such word");
            }

            var word = verse.Words[key.Position - 1];
            var (surface, number) = NumberedWord(row);
            if (word.Surface != surface)
            {
                throw Unmatched(row, $"the word reads \"{word.Surface}\"");
            }

            if (word.Strong != number)
            {
                throw Unmatched(row, word.Strong is null ? "the word carries no number" : $"the word carries {word.Strong}");
            }

            if (word.Strong == row.After)
            {
                throw Unmatched(row, "the word already carries the number the row gives it");
            }

            word.Strong = row.After;
            if (row.After is null)
            {
                cleared++;
            }
            else
            {
                set++;
            }
        }

        return (set, cleared);
    }

    /// <summary>A <c>strong</c> row's <c>before</c>: the word as it reads, and the number it carries, if any, after a space.</summary>
    private (string Surface, string? Number) NumberedWord(TextCorrection row)
    {
        var before = row.Before ?? throw Unmatched(row, "a strong row names in before the word as it reads and the number it carries");
        var space = before.LastIndexOf(' ');
        return space < 0 ? (before, null) : (before[..space], before[(space + 1)..]);
    }

    /// <summary>The verses a <c>block</c> row says the source stores the block under again: in its chapter, after it.</summary>
    private List<VerseKey> Copies(TextCorrection row)
    {
        var own = VerseKey.Parse(row.Own);
        var copies = (row.Before ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(key => VerseKey.Parse(key)).ToList();
        if (copies.Count == 0 || row.After is not null
            || copies.Any(copy => copy.Book != own.Book || copy.Chapter != own.Chapter || copy.ChapterLabel != own.ChapterLabel
                                  || copy.CompareTo(own) <= 0))
        {
            throw Unmatched(row, "a block names in before the later verses of its chapter it is stored under, and nothing in after");
        }

        return copies;
    }

    /// <summary>
    /// The chapter's verses with each block once: the copies, which must read word for word what the block reads,
    /// go, and the block states every number it was stored under, in order.
    /// </summary>
    private IEnumerable<VerseDraft> Blocks(
        int book,
        ChapterDraft chapter,
        Dictionary<VerseKey, (TextCorrection Row, List<VerseKey> Copies)> blocks,
        HashSet<VerseKey> met)
    {
        if (blocks.Count == 0)
        {
            return chapter.Verses;
        }

        VerseKey Key(VerseDraft verse) => new(book, chapter.Number, verse.Number, verse.Label);
        var verses = chapter.Verses.ToDictionary(Key);
        var copyOf = new Dictionary<VerseKey, VerseKey>();
        foreach (var (own, (row, copies)) in blocks.Where(block => block.Key.Book == book && block.Key.Chapter == chapter.Number))
        {
            if (!verses.TryGetValue(own, out var block))
            {
                continue;
            }

            foreach (var copy in copies)
            {
                if (verses.TryGetValue(copy, out var copied) && !Printed(copied).SequenceEqual(Printed(block)))
                {
                    throw Unmatched(row, $"{copy} does not read what {own} reads");
                }

                copyOf[copy] = own;
            }
        }

        return chapter.Verses
            .Where(verse =>
            {
                var key = Key(verse);
                if (!copyOf.ContainsKey(key))
                {
                    return true;
                }

                met.Add(key);
                return false;
            })
            .Select(verse =>
            {
                var key = Key(verse);
                if (!blocks.TryGetValue(key, out var block))
                {
                    return verse;
                }

                if (verse.Stated.Count > 0)
                {
                    throw Unmatched(block.Row, "the source already states the verse's numbers");
                }

                met.Add(key);
                return verse with
                {
                    Stated = [.. new[] { key }.Concat(block.Copies).Select(at => new StatedNumberDraft(at.Chapter, at.Verse))],
                };
            })
            .ToList();

        static IEnumerable<(string, string)> Printed(VerseDraft verse) => verse.Words.Select(word => (word.Surface, word.Trailer));
    }

    /// <summary>
    /// The verse's words with its rows applied, and for each the position it had as read (0 for a word a row added),
    /// so a note's anchor can follow its word.
    /// </summary>
    private (List<WordDraft> Words, List<int> Read) Corrected(IReadOnlyList<WordDraft> read, List<TextCorrection> rows)
    {
        var words = read.ToList();
        var origin = Enumerable.Range(1, read.Count).ToList();

        // Word rows first, by the positions the reader gave, from the last so a drop moves nothing still to come.
        foreach (var row in rows
                     .Where(row => row.Kind is TextCorrection.Letters or TextCorrection.Drop)
                     .OrderByDescending(row => WordKey.Parse(row.Own, _slug).Position))
        {
            var at = WordKey.Parse(row.Own, _slug).Position - 1;
            if (at < 0 || at >= words.Count || words[at].Surface != row.Before)
            {
                throw Unmatched(row, at >= 0 && at < words.Count ? $"the word reads \"{words[at].Surface}\"" : "the verse has no such word");
            }

            if (row.Kind == TextCorrection.Drop)
            {
                words.RemoveAt(at);
                origin.RemoveAt(at);
            }
            else
            {
                words[at] = words[at] with { Surface = row.After! };
            }
        }

        foreach (var row in rows.Where(row => row.Kind == TextCorrection.Prepend))
        {
            var head = Words(row);
            Added.UnionWith(head);
            if (words.Take(head.Count).Select(w => w.Surface).SequenceEqual(head.Select(w => w.Surface)))
            {
                throw Unmatched(row, "the verse already opens with these words");
            }

            words.InsertRange(0, head);
            origin.InsertRange(0, Enumerable.Repeat(0, head.Count));
        }

        foreach (var row in rows.Where(row => row.Kind == TextCorrection.Append))
        {
            var tail = Words(row);
            Added.UnionWith(tail);
            if (words.Count > 0)
            {
                var last = words[^1];
                if (last.Trailer.Length == 0 || !char.IsWhiteSpace(last.Trailer[^1]))
                {
                    words[^1] = last with { Trailer = last.Trailer + " " };
                }
            }

            words.AddRange(tail);
            origin.AddRange(Enumerable.Repeat(0, tail.Count));
        }

        return (words, origin);
    }

    /// <summary>
    /// The words a <c>prepend</c> or <c>append</c> row adds, divided as the bible4u files' verses are. Space
    /// the row ends with stays on its last word: it is what parts a prepended title from the verse after it.
    /// </summary>
    internal static List<WordDraft> Words(TextCorrection row)
    {
        var after = row.After ?? string.Empty;
        var words = VerseWords.Parse(after)
            .Select(token => new WordDraft(token.Word, token.Trailer, Elided: token.Word.Length == 0, SuppliedSpan: token.SuppliedSpan))
            .ToList();
        var read = string.Concat(words.Select(w => w.Surface + w.Trailer));
        if (words.Count > 0 && after.Length > read.Length && after.StartsWith(read, StringComparison.Ordinal) && string.IsNullOrWhiteSpace(after[read.Length..]))
        {
            words[^1] = words[^1] with { Trailer = words[^1].Trailer + after[read.Length..] };
        }

        return words;
    }

    private InvalidOperationException Unmatched(TextCorrection row, string why) =>
        new($"{_slug} corrections: the {row.Kind} row at {row.Own} ({row.Before} -> {row.After}) does not apply: {why}. "
            + "The source is not the one the row was written against; read both before changing either.");
}
