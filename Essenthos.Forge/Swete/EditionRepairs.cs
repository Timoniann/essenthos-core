using Essenthos.Core.Conversion;

namespace Essenthos.Core.Swete;

/// <summary>
/// Makes a text's <c>tokens</c>, <c>opens</c> and <c>moves</c> rows on the one-token-per-line form of a book
/// (<c>book.chapter.verse token</c>), in the order its corrections file lists them: a division can open a verse that
/// a later row then corrects, and a row is addressed to the verse as the rows before it left it.
///
/// <para>
/// Every row must find what it was written against, in the first stretch of lines its verse stands in and exactly
/// once, or the read stops and names it. A transcription corrected upstream, or fetched at another commit, is then
/// noticed rather than repaired a second time in a place the row was never read for.
/// </para>
/// </summary>
internal static class EditionRepairs
{
    public static readonly string[] Kinds = [TextCorrection.Tokens, TextCorrection.Opens, TextCorrection.Moves];

    /// <summary>The book number a row's reference names, as the transcription's lines print it.</summary>
    public static int Book(TextCorrection row) =>
        Reference(row.Own) is { } reference && int.TryParse(reference.Book, out var book)
            ? book
            : throw new FormatException($"\"{row.Own}\" ({row.Kind}) is not a transcription reference, book.chapter.verse.");

    /// <summary>The lines of a book with the text's rows for it made; a book no row names passes through as it reads.</summary>
    public static IEnumerable<string> Apply(string file, int book, IEnumerable<string> lines, TextCorrections corrections)
    {
        var rows = corrections.Of(Kinds).Where(row => Book(row) == book).ToList();
        return rows.Count == 0 ? lines : Apply(file, lines, rows);
    }

    /// <summary>
    /// The rows of the given kinds whose book is none of those read, which would otherwise be left out without a word.
    /// </summary>
    public static void CheckBooks(string slug, TextCorrections corrections, IReadOnlySet<int> read)
    {
        if (corrections.Of(Kinds).FirstOrDefault(row => !read.Contains(Book(row))) is { } stray)
        {
            throw new InvalidOperationException(
                $"{slug} corrections: the {stray.Kind} row at {stray.Own} names a book of the transcription this text does not read.");
        }
    }

    public static IReadOnlyList<string> Apply(string file, IEnumerable<string> lines, IReadOnlyList<TextCorrection> rows)
    {
        var runs = new List<Run>();
        foreach (var line in lines)
        {
            var space = line.IndexOf(' ');
            if (space < 0 || Reference(line[..space]) is not { } reference || !int.TryParse(reference.Book, out _))
            {
                throw new InvalidOperationException(
                    $"\"{line}\" in {file} is not \"<book>.<chapter>.<verse> <word>\", which is the only form a repair can be addressed in.");
            }

            if (runs.Count > 0 && runs[^1].Is(reference.Chapter, reference.Verse) && runs[^1].Book == reference.Book)
            {
                runs[^1].Tokens.Add(line[(space + 1)..]);
            }
            else
            {
                runs.Add(new Run(reference.Book, reference.Chapter, reference.Verse, [line[(space + 1)..]]));
            }
        }

        foreach (var row in rows)
        {
            var (_, chapter, verse) = Reference(row.Own)!.Value;
            if (row.Kind == TextCorrection.Moves)
            {
                var to = Target(row, row.After);
                var moved = 0;
                foreach (var run in runs.Where(run => run.Is(chapter, verse)))
                {
                    run.Chapter = to.Chapter;
                    run.Verse = to.Verse;
                    moved++;
                }

                if (moved == 0)
                {
                    throw Missed(file, row, "holds no words to renumber");
                }

                Normalise(runs);
                continue;
            }

            var at = runs.FindIndex(run => run.Is(chapter, verse));
            if (at < 0)
            {
                throw Missed(file, row, "is not a verse of the book");
            }

            var tokens = runs[at].Tokens;
            var digitised = (row.Before ?? string.Empty).Split(' ');
            var found = new List<int>(1);
            for (var i = 0; i + digitised.Length <= tokens.Count; i++)
            {
                if (tokens.Skip(i).Take(digitised.Length).SequenceEqual(digitised, StringComparer.Ordinal))
                {
                    found.Add(i);
                }
            }

            if (found.Count != 1)
            {
                throw Missed(file, row,
                    $"holds \"{row.Before}\" {found.Count} times where the row needs it exactly once: \"{string.Join(' ', tokens)}\"");
            }

            var start = found[0];
            if (row.Kind == TextCorrection.Tokens)
            {
                tokens.RemoveRange(start, digitised.Length);
                tokens.InsertRange(start, Tokens(row.After));
                if (tokens.Count == 0)
                {
                    Normalise(runs);
                }

                continue;
            }

            var space = (row.After ?? string.Empty).IndexOf(' ');
            var opens = Target(row, space < 0 ? row.After : row.After![..space]);
            var printed = space < 0 ? [] : Tokens(row.After![(space + 1)..]);
            var rest = tokens.GetRange(start + digitised.Length, tokens.Count - start - digitised.Length);
            tokens.RemoveRange(start, tokens.Count - start);
            runs.Insert(at + 1, new Run(runs[at].Book, opens.Chapter, opens.Verse, [.. printed, .. rest]));
            Normalise(runs);
        }

        return [.. runs.SelectMany(run => run.Tokens.Select(token => $"{run.Book}.{run.Chapter}.{run.Verse} {token}"))];
    }

    private static string[] Tokens(string? printed) => string.IsNullOrEmpty(printed) ? [] : printed.Split(' ');

    /// <summary>Lines of one verse that now stand together are one stretch, and a stretch with no line left is none.</summary>
    private static void Normalise(List<Run> runs)
    {
        runs.RemoveAll(run => run.Tokens.Count == 0);
        for (var i = runs.Count - 1; i > 0; i--)
        {
            if (runs[i - 1].Book == runs[i].Book && runs[i - 1].Is(runs[i].Chapter, runs[i].Verse))
            {
                runs[i - 1].Tokens.AddRange(runs[i].Tokens);
                runs.RemoveAt(i);
            }
        }
    }

    private static (string Chapter, string Verse) Target(TextCorrection row, string? reference)
    {
        if (reference is null || Reference(reference) is not { } target || target.Book != Reference(row.Own)!.Value.Book)
        {
            throw new FormatException(
                $"The {row.Kind} row at {row.Own} names \"{reference}\" where it needs a reference of the same book, book.chapter.verse.");
        }

        return (target.Chapter, target.Verse);
    }

    private static (string Book, string Chapter, string Verse)? Reference(string text) =>
        text.Split('.') is [var book, var chapter, var verse] && book.Length > 0 && chapter.Length > 0 && verse.Length > 0
            ? (book, chapter, verse)
            : null;

    private static InvalidOperationException Missed(string file, TextCorrection row, string what) =>
        new($"{file} {row.Own} {what}. The transcription no longer reads as the one the {row.Kind} row \"{row.Why}\" was "
            + "made against — fetched again at another commit, or corrected upstream. Read the verse afresh and correct or "
            + "remove the row in the text's corrections file.");

    private sealed class Run(string book, string chapter, string verse, List<string> tokens)
    {
        public string Book { get; } = book;

        public string Chapter { get; set; } = chapter;

        public string Verse { get; set; } = verse;

        public List<string> Tokens { get; } = tokens;

        public bool Is(string chapter, string verse) => Chapter == chapter && Verse == verse;
    }
}
