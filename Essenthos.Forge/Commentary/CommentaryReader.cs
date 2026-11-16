using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Sword;

namespace Essenthos.Core.Commentary;

/// <param name="FirstChapter">Null with <paramref name="LastChapter"/> for an introduction to the book.</param>
/// <param name="FirstVerse">Null with <paramref name="LastVerse"/> for an introduction.</param>
/// <param name="Body">The comment as <see cref="CommentBuilder"/> writes it.</param>
internal sealed record CommentaryRow(
    string Commentary,
    int Book,
    int? FirstChapter,
    int? FirstVerse,
    int? LastChapter,
    int? LastVerse,
    int Sequence,
    string Body,
    string Source);

/// <summary>
/// A commentary's SWORD module read into comments, each over the span it was written on.
///
/// <para>
/// **The span is the module's own.** SWORD stores a comment written on several verses once and points every verse of
/// the run at it, so a run of consecutive verse slots holding the same entry is one comment over that run. A book's
/// heading slot holds its introduction and a chapter's heading slot the chapter's.
/// </para>
///
/// <para>
/// **What comes before a chapter's first comment.** The OSIS conversion puts a book's introduction and its first
/// chapter's introduction (Matthew Henry's preface to the Pentateuch, his argument of Genesis, his summary of
/// chapter 1) into the entry of the chapter's verse 1, ahead of the comment. The chapter's introduction is the division marked
/// <c>introduction</c> there, from its opening milestone to its closing one; what stands before it is the book's
/// introduction in a book's first chapter and part of the chapter's in any other. A book's introductions in two
/// slots (its heading, then its first verse) are one introduction.
/// </para>
/// </summary>
internal static partial class CommentaryReader
{
    public const string Folder = "Commentaries";

    private const string Osis = "OSIS";

    /// <param name="unread">Elements of the module's markup the reader does not know, counted.</param>
    public static List<CommentaryRow> Read(string resources, CommentaryWorks.Work work, Dictionary<string, int>? unread = null)
    {
        var folder = Path.Combine(resources, Folder, work.Module);
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"The commentary {work.Id} is read from {folder}, which is not there. Run scripts/fetch-commentaries.ps1, or point " +
                "Dataset:ResourcesPath at a corpus that has it.");
        }

        var osis = SwordModule.Configuration(folder).Values.GetValueOrDefault("SourceType") == Osis;
        var corrections = Corrections.Where(c => c.Commentary == work.Id).ToDictionary(c => c.From);
        var rows = new List<CommentaryRow>();
        string? lastMarkup = null;
        var slots = SwordModule.Slots(folder);
        for (var at = 0; at < slots.Count; at++)
        {
            var slot = slots[at];
            if (slot.Verse == 0)
            {
                var chapter = slot.Chapter == 0 ? (int?)null : slot.Chapter;
                Add(slot.Book, chapter, null, chapter, null, slot.Text);
                continue;
            }

            // The run of verses pointing at this entry, unbroken within the book.
            var last = at;
            while (last + 1 < slots.Count && slots[last + 1].Stored == slot.Stored && slots[last + 1].Verse > 0
                   && slots[last + 1].Book == slot.Book && Follows(slots[last], slots[last + 1]))
            {
                last++;
            }

            var text = slot.Text;
            var opens = osis && slot.Verse == 1 ? IntroductionOpens().Match(text) : Match.Empty;
            var closes = opens.Success ? text.IndexOf($"eID=\"{opens.Groups[1].Value}\"", opens.Index, StringComparison.Ordinal) : -1;
            if (closes >= 0)
            {
                var start = text.LastIndexOf('<', opens.Index);
                var end = text.IndexOf('>', closes) + 1;
                var before = slot.Chapter == 1 ? start : 0;
                Add(slot.Book, null, null, null, null, text[..before]);
                Add(slot.Book, slot.Chapter, null, slot.Chapter, null, text[before..end]);
                text = text[end..];
            }

            var span = new Span(slot.Book, slot.Chapter, slot.Verse, slots[last].Chapter, slots[last].Verse);
            if (corrections.TryGetValue(span, out var corrected))
            {
                span = corrected.To;
            }

            Add(span.Book, span.FirstChapter, span.FirstVerse, span.LastChapter, span.LastVerse, text);
            at = last;
        }

        return rows;

        void Add(int book, int? firstChapter, int? firstVerse, int? lastChapter, int? lastVerse, string markup)
        {
            var bookIntroduction = firstChapter is null;
            var joined = bookIntroduction && rows.Count > 0 && rows[^1] is { FirstChapter: null } previous && previous.Book == book;
            if (joined)
            {
                markup = lastMarkup + markup;
            }

            var comment = new CommentBuilder();
            if (osis)
            {
                CommentMarkup.Osis(markup, comment, joined ? null : unread);
            }
            else
            {
                CommentMarkup.Html(markup, comment, unread);
            }

            if (comment.Json() is not { } body)
            {
                return;
            }

            if (joined)
            {
                rows[^1] = rows[^1] with { Body = body };
            }
            else
            {
                rows.Add(new CommentaryRow(work.Id, book, firstChapter, firstVerse, lastChapter, lastVerse, rows.Count + 1, body, work.Source));
            }

            lastMarkup = markup;
        }
    }

    internal sealed record Span(int Book, int FirstChapter, int FirstVerse, int LastChapter, int LastVerse);

    /// <param name="From">The span the module gives the comment: the run of verses that point at it.</param>
    /// <param name="To">The span the comment is written on, as its own printed text says.</param>
    /// <param name="Why">What in the comment says so.</param>
    internal sealed record Correction(string Commentary, Span From, Span To, string Why);

    /// <summary>
    /// Comments the module points at from the wrong verses, each shown wrong by the comment itself, in the modules the
    /// fetch pins (MHC 2.2, Rieger 1.0); a test holds each to the comment it was read from.
    ///
    /// <para>
    /// Matthew Henry prints the verses he expounds, numbered, before his exposition; in seven places the module's run
    /// is one verse off what he prints (it leaves the first verse of a chapter pointing at nothing, or draws a boundary
    /// one verse early), and his numbers are the span.
    /// </para>
    ///
    /// <para>
    /// Rieger heads each reflection with the passage in his Bible's numbering, which is the King James's except at the
    /// end of Hosea and in Jonah 1-2, where it is the Hebrew's: his Hosea 14 has ten verses (14:1 is the King James's
    /// 13:16) and his Jonah 2 eleven (2:1 is 1:17, the great fish). The module put every reflection at the King James
    /// verses of the same numbers, so these run one verse late, and the last of each chapter fell into the next
    /// chapter or book (Hosea 14:10 at Joel 1:1, pushing Joel 1:1-12 to 1:2-12; Jonah 2:11 at 3:1).
    /// </para>
    /// </summary>
    internal static readonly Correction[] Corrections =
    [
        new("henry", new(6, 18, 2, 18, 10), new(6, 18, 1, 18, 10), "Joshua 18: Henry prints verses 1-10"),
        new("henry", new(9, 25, 2, 25, 11), new(9, 25, 1, 25, 11), "1 Samuel 25: Henry prints verses 1-11"),
        new("henry", new(10, 22, 2, 22, 51), new(10, 22, 1, 22, 51), "2 Samuel 22: Henry prints verses 1-51"),
        new("henry", new(20, 11, 19, 11, 19), new(20, 11, 19, 11, 20), "Proverbs 11: Henry expounds verses 19 and 20 together"),
        new("henry", new(18, 28, 1, 28, 11), new(18, 28, 1, 28, 13), "Job 28: Henry prints verses 1-13"),
        new("henry", new(18, 28, 12, 28, 19), new(18, 28, 14, 28, 19), "Job 28: Henry prints verses 14-19"),
        new("henry", new(24, 46, 1, 46, 12), new(24, 46, 1, 46, 11), "Jeremiah 46: Henry prints verses 1-11"),
        new("henry", new(24, 46, 13, 46, 28), new(24, 46, 12, 46, 28), "Jeremiah 46: Henry prints verses 12-28"),
        new("henry", new(65, 1, 8, 1, 15), new(65, 1, 8, 1, 14), "Jude: Henry prints verses 8-14"),
        new("henry", new(65, 1, 16, 1, 25), new(65, 1, 15, 1, 25), "Jude: Henry prints verses 15-25"),
        new("rieger", new(28, 13, 10, 14, 1), new(28, 13, 10, 13, 16), "Text: Hosea 13,10-14,1"),
        new("rieger", new(28, 14, 2, 14, 4), new(28, 14, 1, 14, 3), "Text: Hosea 14,2-4"),
        new("rieger", new(28, 14, 5, 14, 8), new(28, 14, 4, 14, 7), "Text: Hosea 14,5-8"),
        new("rieger", new(28, 14, 9, 14, 9), new(28, 14, 8, 14, 8), "Text: Hosea 14,9"),
        new("rieger", new(29, 1, 1, 1, 1), new(28, 14, 9, 14, 9), "Text: Hosea 14,10"),
        new("rieger", new(29, 1, 2, 1, 12), new(29, 1, 1, 1, 12), "Text: Joel 1,1-12"),
        new("rieger", new(32, 2, 1, 2, 1), new(32, 1, 17, 1, 17), "Text: Jona 2,1"),
        new("rieger", new(32, 2, 2, 2, 10), new(32, 2, 1, 2, 9), "Text: Jona 2,2-10"),
        new("rieger", new(32, 3, 1, 3, 1), new(32, 2, 10, 2, 10), "Text: Jona 2,11"),
        new("rieger", new(32, 3, 2, 3, 2), new(32, 3, 1, 3, 2), "Text: Jona 3,1-2"),
    ];

    /// <summary>Whether the second verse comes straight after the first in the module's numbering: the next verse, or a new chapter's first.</summary>
    private static bool Follows(SwordSlot first, SwordSlot second) =>
        second.Chapter == first.Chapter && second.Verse == first.Verse + 1 || second.Chapter == first.Chapter + 1 && second.Verse == 1;

    [GeneratedRegex("""<div sID="([^"]+)" type="introduction"\s*/>""")]
    private static partial Regex IntroductionOpens();
}
