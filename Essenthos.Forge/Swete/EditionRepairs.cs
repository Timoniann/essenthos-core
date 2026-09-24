namespace Essenthos.Core.Swete;

internal enum EditionRepairKind
{
    /// <summary>Every word at an address moves to another: a number the transcription misread.</summary>
    Renumber,

    /// <summary>
    /// A verse the transcription ran into the one before it: from the given words to the end of the
    /// verse they open a verse of their own.
    /// </summary>
    Divide,

    /// <summary>Words replaced by others, or by none.</summary>
    Replace,
}

/// <summary>
/// One place where this project reads a transcription differently from the way it is encoded, and
/// what establishes the reading.
/// </summary>
/// <param name="Chapter">The chapter the words stand in once the repairs before this one are made.</param>
/// <param name="Verse">The verse, as the transcription numbers it — not always a number.</param>
/// <param name="Digitised">
/// The tokens as the transcription has them, separated by single spaces, which must stand in the verse
/// exactly once: the tokens replaced, or the ones a divided verse opens with. Empty for a renumbering.
/// </param>
/// <param name="Printed">What the page prints in their place, in the same form; empty where it prints nothing.</param>
/// <param name="To">The verse the words move to, for a renumbering or a division.</param>
internal sealed record EditionRepair(
    EditionRepairKind Kind,
    int Chapter,
    string Verse,
    string Digitised,
    string Printed,
    (int Chapter, string Verse) To,
    string Why)
{
    public static EditionRepair Renumber(int chapter, string verse, int toChapter, string toVerse, string why) =>
        new(EditionRepairKind.Renumber, chapter, verse, string.Empty, string.Empty, (toChapter, toVerse), why);

    public static EditionRepair Divide(
        int chapter, string verse, string digitised, string printed, string toVerse, string why) =>
        new(EditionRepairKind.Divide, chapter, verse, digitised, printed, (chapter, toVerse), why);

    public static EditionRepair Replace(int chapter, string verse, string digitised, string printed, string why) =>
        new(EditionRepairKind.Replace, chapter, verse, digitised, printed, default, why);

    public override string ToString() => $"{Chapter}:{Verse} {Kind} \"{Digitised}\"";
}

/// <summary>
/// Makes <see cref="EditionRepair"/>s on the one-token-per-line form of a book, in the order they are
/// listed: a division can open a verse that a later repair then corrects.
///
/// <para>
/// Every repair must find what it was written against, exactly once, or the read stops and names it.
/// A transcription corrected upstream, or fetched at another commit, is then noticed rather than
/// repaired a second time in a place the repair was never read for.
/// </para>
/// </summary>
internal static class EditionRepairs
{
    public static IReadOnlyList<string> Apply(string book, IEnumerable<string> lines, IReadOnlyList<EditionRepair> repairs)
    {
        var tokens = new List<(int Book, int Chapter, string Verse, string Token)>(32_000);
        foreach (var line in lines)
        {
            var space = line.IndexOf(' ');
            var reference = space < 0 ? [] : line[..space].Split('.');
            if (reference.Length != 3 || !int.TryParse(reference[0], out var number) ||
                !int.TryParse(reference[1], out var chapter))
            {
                throw new InvalidOperationException(
                    $"\"{line}\" in {book} is not \"<book>.<chapter>.<verse> <word>\" with the chapter as a number, " +
                    "which is the only form a repair can be addressed in.");
            }

            tokens.Add((number, chapter, reference[2], line[(space + 1)..]));
        }

        foreach (var repair in repairs)
        {
            tokens = repair.Kind switch
            {
                EditionRepairKind.Renumber => Renumbered(book, tokens, repair),
                _ => Rewritten(book, tokens, repair),
            };
        }

        return [.. tokens.Select(t => $"{t.Book}.{t.Chapter}.{t.Verse} {t.Token}")];
    }

    private static List<(int Book, int Chapter, string Verse, string Token)> Renumbered(
        string book,
        List<(int Book, int Chapter, string Verse, string Token)> tokens,
        EditionRepair repair)
    {
        var moved = 0;
        var result = tokens.Select(t =>
        {
            if (t.Chapter != repair.Chapter || t.Verse != repair.Verse)
            {
                return t;
            }

            moved++;
            return (t.Book, repair.To.Chapter, repair.To.Verse, t.Token);
        }).ToList();

        return moved > 0
            ? result
            : throw Missed(book, repair, "holds no words to renumber");
    }

    private static List<(int Book, int Chapter, string Verse, string Token)> Rewritten(
        string book,
        List<(int Book, int Chapter, string Verse, string Token)> tokens,
        EditionRepair repair)
    {
        var first = tokens.FindIndex(t => t.Chapter == repair.Chapter && t.Verse == repair.Verse);
        if (first < 0)
        {
            throw Missed(book, repair, "is not a verse of the book");
        }

        var end = first;
        while (end < tokens.Count && tokens[end].Chapter == repair.Chapter && tokens[end].Verse == repair.Verse)
        {
            end++;
        }

        var digitised = repair.Digitised.Split(' ');
        var found = new List<int>(1);
        for (var i = first; i + digitised.Length <= end; i++)
        {
            if (tokens.Skip(i).Take(digitised.Length).Select(t => t.Token).SequenceEqual(digitised, StringComparer.Ordinal))
            {
                found.Add(i);
            }
        }

        if (found.Count != 1)
        {
            throw Missed(book, repair,
                $"holds \"{repair.Digitised}\" {found.Count} times where the repair needs it exactly once: " +
                $"\"{string.Join(' ', tokens.Skip(first).Take(end - first).Select(t => t.Token))}\"");
        }

        var at = found[0];
        var template = tokens[at];
        List<(int Book, int Chapter, string Verse, string Token)> printed = repair.Printed.Length == 0
            ? []
            : [.. repair.Printed.Split(' ').Select(word => (template.Book, template.Chapter, template.Verse, word))];

        if (repair.Kind == EditionRepairKind.Divide)
        {
            return
            [
                .. tokens.Take(at),
                .. printed.Concat(tokens.Skip(at + digitised.Length).Take(end - at - digitised.Length))
                    .Select(t => (t.Book, repair.To.Chapter, repair.To.Verse, t.Token)),
                .. tokens.Skip(end),
            ];
        }

        return [.. tokens.Take(at), .. printed, .. tokens.Skip(at + digitised.Length)];
    }

    private static InvalidOperationException Missed(string book, EditionRepair repair, string what) =>
        new($"{book} {repair.Chapter}:{repair.Verse} {what}. The transcription no longer reads as the one the " +
            $"repair \"{repair.Why}\" was made against — fetched again at another commit, or corrected upstream. " +
            "Read the verse afresh and correct or remove the entry.");
}
