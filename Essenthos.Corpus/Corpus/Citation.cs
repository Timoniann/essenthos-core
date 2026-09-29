namespace Essenthos.Core.Corpus;

/// <summary>
/// The verses a claim rests on, as a clause writes them: one verse (<c>NUM 10:29</c>), a passage of
/// adjacent verses in one chapter that tell one event or one list under one heading
/// (<c>HOS 1:2-4</c>), or two parts that each state part of it, each a verse or such a passage
/// (<c>2KI 8:18; 2KI 8:26</c>, <c>GEN 20:12-14; GEN 11:27</c>).
///
/// <para>
/// A passage is a statement read over its whole length — Hosea is named in 1:2 and Jezreel in 1:4,
/// and the verses between say whose son he is — so both people have to be named somewhere in it.
/// Two parts composed are two statements joined by a reader, and a claim so made is never stored as
/// though one verse stated it: it carries <see cref="ComposedConfidence"/> at most.
/// </para>
/// </summary>
/// <param name="Verses">The verses in the order cited; a passage's in the order they stand.</param>
/// <param name="Composed">Two parts each stating a part, rather than one verse or one passage.</param>
internal sealed record Citation(IReadOnlyList<(int Book, int Chapter, int Verse)> Verses, bool Composed)
{
    /// <summary>The most verses a passage may run to and still be read as one statement.</summary>
    public const int LongestPassage = 3;

    /// <summary>How many parts, each a verse or a passage, a composed claim joins.</summary>
    public const int ComposedParts = 2;

    /// <summary>
    /// How sure a claim composed of two verses is at most. Each verse states its part, and what a
    /// reader adds is only that the two speak of the same person — so it stands above the owner's
    /// <em>very likely</em> (0.9) and below a statement, which carries no number at all.
    /// </summary>
    public const double ComposedConfidence = 0.95;

    private const char PartSeparator = ';';

    private const char RangeSeparator = '-';

    public (int Book, int Chapter, int Verse) First => Verses[0];

    /// <summary>A single verse, which is what nearly every claim cites and what needs no more than its address.</summary>
    public bool Single => Verses.Count == 1;

    /// <summary>
    /// The citation a claim writes, or null where it is not one: an unknown book, a passage running
    /// across a chapter or past <see cref="LongestPassage"/> verses, anything but two parts joined by
    /// a semicolon, or two parts that share a verse.
    /// </summary>
    public static Citation? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var parts = text.Split(PartSeparator, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1)
        {
            return Passage(parts[0]) is { } verses ? new Citation(verses, Composed: false) : null;
        }

        if (parts.Length != ComposedParts)
        {
            return null;
        }

        var joined = parts.Select(Passage).ToList();
        if (joined.Any(verses => verses is null) || joined[0]!.Intersect(joined[1]!).Any())
        {
            return null;
        }

        return new Citation([.. joined.SelectMany(verses => verses!)], Composed: true);
    }

    /// <summary><c>HOS 1:2-4</c> or <c>HOS 1:2</c> as its verses, in order.</summary>
    private static List<(int Book, int Chapter, int Verse)>? Passage(string text)
    {
        var split = text.LastIndexOf(' ');
        if (split <= 0 || BookReferences.ResolveOrdinal(text[..split]) is not { } book)
        {
            return null;
        }

        var address = text[(split + 1)..].Split(':');
        if (address.Length != 2 || !int.TryParse(address[0], out var chapter))
        {
            return null;
        }

        var range = address[1].Split(RangeSeparator);
        if (range.Length is < 1 or > 2
            || !int.TryParse(range[0], out var from)
            || !int.TryParse(range[^1], out var through)
            || through < from
            || through - from + 1 > LongestPassage)
        {
            return null;
        }

        return [.. Enumerable.Range(from, through - from + 1).Select(verse => (book, chapter, verse))];
    }
}
