namespace Essenthos.Core.BetaMasaheft;

/// <param name="Number">The verse as it is read, which is the file's number wherever that is legible.</param>
/// <param name="Text">Its words, with <see cref="GeezReader.LineBreak"/> where the edition starts a line.</param>
internal sealed record GeezVerse(int Number, string Text);

/// <param name="Joined">Lines read as the continuation of the verse before them.</param>
/// <param name="Renumbered">Lines read as the verse the numbering skipped, rather than as the number they carry.</param>
internal sealed record GeezSequence(IReadOnlyList<GeezVerse> Verses, int Joined, int Renumbered);

/// <summary>
/// Turns the numbered lines of one chapter into verses, reading the file's numbering as a sequence.
///
/// <para>
/// The church's printed Bible was typed by hand and its line numbers carry the slips typing makes.
/// A verse broken over two lines carries its own number twice, or a stray low number — <em>4</em>,
/// <em>2</em>, <em>30</em> — on its second half: in 4 Ezra 5 the words <em>ሰዓት።</em>, one word,
/// stand on a line of their own numbered 3 between verses 24 and 25. Taken at its word the file would
/// put that word at 5:3 and give the chapter a second third verse. And a verse is now and then
/// numbered as the one before it where the one before is plainly done, as Revelation 2 reads 26, 26,
/// 28.
/// </para>
///
/// <para>
/// So a number that moves the sequence on by one opens the next verse; one that jumps further opens
/// it only if the line after it carries on from it, which is what separates a verse missing from the
/// file — Zechariah 2 runs 1, 2, 3, 12, 13 — from a stray number on a broken line. A number that
/// does not move the sequence on is the verse the numbering skipped where the next line confirms
/// exactly one is missing, and otherwise the continuation of the verse before. Nothing is dropped:
/// every word of every line is in some verse.
/// </para>
/// </summary>
internal static class GeezVerses
{
    /// <param name="unnumberedContinues">
    /// Whether a line with no number continues the verse before it, as in the Psalter, or is a
    /// heading the edition sets between verses, as in the church's printed Bible.
    /// </param>
    public static GeezSequence Sequence(IReadOnlyList<GeezLine> lines, bool unnumberedContinues)
    {
        var kept = lines
            .Where(line => HasWords(line.Text) && (line.Number is not null || unnumberedContinues))
            .ToList();
        var verses = new List<(int Number, List<string> Parts)>(kept.Count);
        int joined = 0, renumbered = 0;

        for (var i = 0; i < kept.Count; i++)
        {
            var line = kept[i];
            if (verses.Count == 0)
            {
                if (line.Number is { } first)
                {
                    verses.Add((first, [line.Text]));
                }

                continue;
            }

            var previous = verses[^1].Number;
            var next = NextNumber(kept, i);

            if (line.Number is not { } number)
            {
                verses[^1].Parts.Add(GeezReader.LineBreak + line.Text);
                continue;
            }

            if (number == previous + 1 || (number > previous + 1 && (next is null || next == number + 1)))
            {
                verses.Add((number, [line.Text]));
            }
            else if (number <= previous && next == previous + 2)
            {
                verses.Add((previous + 1, [line.Text]));
                renumbered++;
            }
            else
            {
                verses[^1].Parts.Add(" " + line.Text);
                joined++;
            }
        }

        return new GeezSequence(
            [.. verses.Select(verse => new GeezVerse(verse.Number, string.Concat(verse.Parts)))],
            joined,
            renumbered);
    }

    /// <summary>
    /// Whether a line holds anything to read. Luke 21 opens with an empty line numbered 21, and 1
    /// Esdras 8:20 and two verses of 4 Ezra are typed as a full stop or two dots and nothing else: a
    /// verse the typist did not reach, which is absent rather than a verse of no words.
    /// </summary>
    private static bool HasWords(string text) =>
        text.Any(character => char.IsLetterOrDigit(character)
                              || char.GetUnicodeCategory(character) == System.Globalization.UnicodeCategory.OtherNumber);

    private static int? NextNumber(IReadOnlyList<GeezLine> lines, int after)
    {
        for (var i = after + 1; i < lines.Count; i++)
        {
            if (lines[i].Number is { } number)
            {
                return number;
            }
        }

        return null;
    }
}
