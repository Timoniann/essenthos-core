using Essenthos.Core.Corpus;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Loading;

/// <summary>
/// One place where eBible's Brenton Greek prints words twice or lost them, read as Brenton's English
/// and two Greek witnesses read it.
/// </summary>
/// <param name="Book">The book, by its canonical ordinal.</param>
/// <param name="Digitised">
/// The words as the file prints them, as <see cref="BrentonDivisions.Printed"/> writes them, which must
/// stand in the verse exactly once.
/// </param>
/// <param name="Printed">What the edition prints in their place, in the same form.</param>
/// <param name="Why">The witnesses that read it so.</param>
internal sealed record BrentonEdit(int Book, int Chapter, int Verse, string Digitised, string Printed, string Why);

/// <summary>
/// The words eBible's Brenton Greek repeats or drops, made right where Brenton's English reads the
/// verse once or whole and the Greek of Swete and of the Wikisource text GLAUx annotates prints it so.
///
/// <para>
/// Three faults of the file, each of which its copyist's eye made: at Exodus 33:10 it begins the verse
/// with the clause that ends 33:9, at Job 3:14 with the whole of 3:13, and at 1 Esdras 8:17-18 it skips
/// from one <c>τοῦ Θεοῦ σου</c> to the next and loses the words between. What goes in is GLAUx's Greek,
/// which divides those two verses as Brenton does; Swete has the same words in his own spelling and
/// division. Only the file's capital at the head of a verse and its comma before <c>δώσεις</c> are
/// kept where GLAUx has neither.
/// </para>
///
/// <para>
/// A cold load reads the verses so; <see cref="BrentonEditLoader"/> writes them into a corpus loaded
/// before, keeping the row of every word the two readings share.
/// </para>
/// </summary>
internal static class BrentonEdits
{
    private const int Exodus = 2;

    private const int Job = 18;

    private const int FirstEsdras = 68;

    /// <summary>What the text's row says about these words, on a cold load and on a warm one.</summary>
    public const string Note =
        "Modified: where eBible's file prints words twice (Exodus 33:10, Job 3:14) or loses a clause between "
        + "two that end alike (1 Esdras 8:17-18), Essenthos reads the verses as Brenton's English, Swete and the "
        + "GLAUx treebank's Greek read them, in GLAUx's words.";

    public static readonly IReadOnlyList<BrentonEdit> All =
    [
        new(Exodus, 33, 10, "καὶ ἐλάλει Μωυσῇ. Καὶ", "Καὶ",
            "33:9 already ends with καὶ ἐλάλει Μωσῇ; Swete and GLAUx print the clause once, at the end of 33:9, and "
            + "Brenton's English renders it once"),
        new(Job, 3, 14, "Νῦν ἂν κοιμηθεὶς ἡσύχασα, ὑπνώσας δὲ ἀνεπαυσάμην μετὰ", "μετὰ",
            "3:13 is these seven words; Swete and GLAUx print them once, and Brenton's English begins 3:14 with "
            + "\"with kings and councillors\""),
        new(FirstEsdras, 8, 17, "Θεοῦ σου,", "Θεοῦ σου τοῦ ἐν Ἱερουσαλὴμ,",
            "GLAUx ends 8:17 τοῦ Θεοῦ σου τοῦ ἐν Ἱερουσαλὴμ, Swete reads τοῦ ἐν Ἰερουσαλήμ there, and Brenton's "
            + "English 8:17 reads \"which is in Jerusalem\""),
        new(FirstEsdras, 8, 18, "δώσεις", "Καὶ τὰ λοιπὰ ὅσα ἂν ὑποπίπτῃ σοι εἰς τὴν χρείαν τοῦ ἱεροῦ τοῦ Θεοῦ σου, δώσεις",
            "GLAUx opens 8:18 with these words, Swete prints them in his own spelling, and Brenton's English 8:18 "
            + "opens \"And whatsoever thing else thou shalt remember for the use of the temple of thy God\""),
    ];

    /// <summary>A book's chapters with its edits made; any other book's pass through unchanged.</summary>
    public static IReadOnlyList<ChapterDraft> Apply(
        int canonical,
        IReadOnlyList<ChapterDraft> chapters,
        IReadOnlyList<BrentonEdit>? list = null)
    {
        var edits = (list ?? All).Where(e => e.Book == canonical).ToList();
        if (edits.Count == 0)
        {
            return chapters;
        }

        var made = new HashSet<BrentonEdit>();
        var result = chapters.Select(chapter => chapter with
        {
            Verses =
            [
                .. chapter.Verses.Select(verse =>
                {
                    var words = verse.Words;
                    foreach (var edit in edits.Where(e => e.Chapter == chapter.Number && e.Verse == verse.Number
                                                          && verse.Label.Length == 0))
                    {
                        words = Edit(words, edit);
                        made.Add(edit);
                    }

                    return verse with { Words = words };
                }),
            ],
        }).ToList();

        var missed = edits.Except(made).ToList();
        return missed.Count == 0
            ? result
            : throw Unread(missed[0], "is not a verse of the book");
    }

    /// <summary>The verse's words with the edit made, or a refusal where the file does not read as it was made against.</summary>
    public static IReadOnlyList<WordDraft> Edit(IReadOnlyList<WordDraft> words, BrentonEdit edit)
    {
        var length = Words(edit.Digitised).Count;
        var found = Enumerable.Range(0, Math.Max(0, words.Count - length + 1))
            .Where(at => BrentonDivisions.Printed(words.Skip(at).Take(length)) == edit.Digitised)
            .ToList();
        if (found.Count != 1)
        {
            throw Unread(edit, $"holds \"{edit.Digitised}\" {found.Count} times where the edit needs it once");
        }

        var at = found[0];
        var printed = Words(edit.Printed).ToList();
        printed[0] = printed[0] with { Break = words[at].Break };
        printed[^1] = printed[^1] with { Trailer = words[at + length - 1].Trailer };
        return [.. words.Take(at), .. printed, .. words.Skip(at + length)];
    }

    /// <summary>Words as the file's own reader makes them of a line of the edition.</summary>
    public static IReadOnlyList<WordDraft> Words(string printed) =>
        [.. UsfmReader.Read($"\\id XXX\n\\c 1\n\\v 1 {printed}\n").Chapters[0].Verses[0].Words
            .Select(word => new WordDraft(word.Surface, word.Trailer))];

    private static InvalidOperationException Unread(BrentonEdit edit, string what) =>
        new($"Brenton's Greek {BookReferences.Name(edit.Book)} {edit.Chapter}:{edit.Verse} {what}. The file is not the "
            + "one these edits were made against: read the verse against Brenton's English and GLAUx again and correct "
            + $"the entry in {nameof(BrentonEdits)} before loading.");
}
