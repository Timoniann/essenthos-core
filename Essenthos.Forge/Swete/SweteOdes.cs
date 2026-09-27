using Essenthos.Core.Loading;

namespace Essenthos.Core.Swete;

/// <summary>
/// The Odes: the songs the Greek churches sang at morning prayer, copied out of the books they
/// stand in — the Song of Moses, Hannah's prayer, the Magnificat — and bound after the Psalms in
/// Codex Alexandrinus, from which Swete prints them, in its order and under its numbers.
///
/// <para>
/// They are one book here, numbered as Rahlfs numbers them, one to fourteen: his is the critical
/// edition and the numbering scholarship cites, and his chapters are integers where Swete's are
/// not. Swete prints the Song of the Vineyard and the Prayer of Isaiah as <c>iva</c> and
/// <c>ivb</c>, Habakkuk sixth, and the Magnificat and the Benedictus as two Odes with Simeon's
/// between them, which Rahlfs makes one, his ninth. Where the two disagree, Swete's number stands
/// beside each verse of the Ode as the address his page prints.
/// </para>
///
/// <para>
/// The verses keep the numbers Swete prints, which are those of the passage each Ode is taken from
/// — the Magnificat runs from 46 and the Benedictus from 68 — so Rahlfs's ninth Ode holds both
/// without two verses at one address. Where the transcription runs two verses into one (Hezekiah's
/// 14 and 15, Zechariah's 73 and 74) they stay run together, and the Morning Hymn, which Swete
/// prints without a verse number, is one verse.
/// </para>
/// </summary>
internal static class SweteOdes
{
    public const string File = "28.Odae";

    /// <summary>What the text's row says about the Odes.</summary>
    public const string Note =
        "The Odes are numbered as Rahlfs numbers them, one to fourteen, with Swete's own number for an Ode "
        + "kept beside its verses where it differs; the numeral the transcription let into the heading of his "
        + "Ode ivb is taken out, as it is printed over every other Ode's heading and not in the text.";

    /// <summary>
    /// Every Ode in the order Swete prints them: the chapter the file names it by, the number he
    /// prints, the chapter Rahlfs gives it, and how its heading stands in the file.
    /// </summary>
    private static readonly Ode[] Odes =
    [
        new("1", (1, ""), 1, Heading.Alone),
        new("2", (2, ""), 2, Heading.Alone),
        new("3", (3, ""), 3, Heading.Alone),
        new("iva", (4, "a"), 10, Heading.Alone),
        new("ivb", (4, "b"), 5, Heading.InTheFirstVerse, ["Δ΄", "(β)"]),
        new("5", (5, ""), 6, Heading.Alone),
        new("6", (6, ""), 4, Heading.Alone),
        new("7", (7, ""), 11, Heading.Alone),
        new("8", (8, ""), 12, Heading.Alone),
        new("9", (9, ""), 7, Heading.Alone),
        new("10", (10, ""), 8, Heading.Alone),
        new("11", (11, ""), 9, Heading.Alone),
        new("12", (12, ""), 13, Heading.Alone),
        new("13", (13, ""), 9, Heading.Alone),
        new("14", (14, ""), 14, Heading.Unnumbered),
    ];

    /// <summary>The verse a heading is given so that the reader opens the Ode's first verse with it.</summary>
    private const string Head = "0";

    /// <summary>The verse the Morning Hymn is, having no number of its own.</summary>
    private const int OnlyVerse = 1;

    private enum Heading
    {
        /// <summary>
        /// The heading is the Ode's first block, under the number of the verse before it in the file,
        /// or — Habakkuk's and the Hymn of the Fathers' — under a number of its own the encoding gave
        /// it where Swete's margin gives the Ode as 2—19 and 52—88.
        /// </summary>
        Alone,

        /// <summary>The heading and the first verse are one block, because the carried number is the first verse's.</summary>
        InTheFirstVerse,

        /// <summary>The Ode is one block, heading and all, and Swete numbers none of it.</summary>
        Unnumbered,
    }

    /// <param name="Keyed">The chapter the file names the Ode by.</param>
    /// <param name="Printed">The number Swete prints over it, with the letter of <c>iva</c> and <c>ivb</c>.</param>
    /// <param name="Rahlfs">The chapter it is held at.</param>
    /// <param name="Numeral">
    /// Tokens of the Ode's number printed over it that the transcription let into the text, which
    /// must be there to be taken out.
    /// </param>
    private sealed record Ode(string Keyed, (int Chapter, string Label) Printed, int Rahlfs, Heading Heading, string[]? Numeral = null)
    {
        public bool Renumbered => Printed != (Rahlfs, "");
    }

    /// <summary>
    /// The file's lines keyed for <see cref="SweteReader"/>: each Ode's chapter as its place in the
    /// file, since two of them are not numbers, and a heading standing alone under the verse it opens,
    /// since some are keyed under numbers the reader would take for verses.
    /// </summary>
    public static IEnumerable<string> Lines(IEnumerable<string> lines)
    {
        Ode? ode = null;
        var place = 0;
        string? opening = null;
        var numerals = new List<string>();

        foreach (var line in lines)
        {
            var space = line.IndexOf(' ');
            var parts = space < 0 ? [] : line[..space].Split('.');
            if (parts.Length != 3)
            {
                // Not a line of the edition; the reader says why.
                yield return line;
                continue;
            }

            if (ode?.Keyed != parts[1])
            {
                Verify(ode, numerals);
                place = Array.FindIndex(Odes, o => o.Keyed == parts[1]) + 1;
                ode = place > 0
                    ? Odes[place - 1]
                    : throw new InvalidOperationException(
                        $"{File} names an Ode \"{parts[1]}\" that is not one of the {Odes.Length} Swete prints. Read the "
                        + $"file again and give the Ode its place in {nameof(SweteOdes)}.{nameof(Odes)}.");
                opening = parts[2];
                numerals.Clear();
            }
            else if (opening != parts[2])
            {
                opening = null;
            }

            var token = line[(space + 1)..];
            if (opening is not null && ode.Numeral?.Contains(token) == true)
            {
                numerals.Add(token);
                continue;
            }

            var verse = opening is not null && ode.Heading == Heading.Alone ? Head : parts[2];
            yield return $"{parts[0]}.{place}.{verse} {token}";
        }

        Verify(ode, numerals);
    }

    /// <summary>
    /// The Odes as the corpus holds them, from the book <see cref="SweteReader"/> read out of
    /// <see cref="Lines"/>: Rahlfs's chapters in his order, the verses of each in Swete's.
    /// </summary>
    public static IReadOnlyList<ChapterDraft> Chapters(SweteBook book) =>
    [
        .. Odes
            .Select((ode, index) => (Ode: ode, Chapter: book.Chapters.SingleOrDefault(c => c.Number == index + 1)
                ?? throw new InvalidOperationException(
                    $"{File} holds no Ode {ode.Keyed}. The file is not the one this reader was written against; "
                    + "fetch it again with scripts/fetch-swete.ps1 rather than loading the Odes without one.")))
            .GroupBy(held => held.Ode.Rahlfs)
            .OrderBy(group => group.Key)
            .Select(group => new ChapterDraft(group.Key, [.. group.SelectMany(held => Verses(held.Ode, held.Chapter))])),
    ];

    private static IEnumerable<VerseDraft> Verses(Ode ode, SweteChapter chapter)
    {
        if (ode.Heading == Heading.Unnumbered && chapter.Verses.Count != 1)
        {
            throw new InvalidOperationException(
                $"{File} numbers verses in Ode {ode.Keyed}, which Swete prints without any. Read them as the "
                + "file now divides them rather than as one verse.");
        }

        return chapter.Verses.Select(verse =>
        {
            var number = ode.Heading == Heading.Unnumbered ? OnlyVerse : verse.Number;
            return new VerseDraft(number, [.. verse.Words.Select(word => new WordDraft(word.Surface, word.Trailer))], verse.Label)
            {
                Stated = ode.Renumbered ? [new StatedNumberDraft(ode.Printed.Chapter, number, ode.Printed.Label)] : [],
            };
        });
    }

    private static void Verify(Ode? ode, List<string> numerals)
    {
        if (ode?.Numeral is { } expected && !numerals.SequenceEqual(expected))
        {
            throw new InvalidOperationException(
                $"The heading of Ode {ode.Keyed} no longer carries \"{string.Join(' ', expected)}\" where the file "
                + $"had it. Read the heading again and correct {nameof(SweteOdes)}.{nameof(Odes)}.");
        }
    }
}
