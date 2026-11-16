using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Products;
using Essenthos.Core.Usfm;

namespace Essenthos.Core.Conversion;

/// <summary>
/// Where the King James starts a paragraph or a line, from eBible's eng-kjv2006, the same 1769 text in USFM: bible4u's
/// file prints one verse a line and no paragraphs at all.
/// <list type="bullet">
/// <item>eBible's <c>\p</c> and <c>\b</c> open a paragraph and <c>\q1</c> a line of a psalm, as <see cref="UsfmReader"/>
/// reads them for every USFM text;</item>
/// <item>the 1611 printing's pilcrow, which eng-kjv2006 keeps as a ¶ in the text (Genesis to Acts), opens a paragraph on
/// the word after it;</item>
/// <item>a heading eBible prints between verses (the Hebrew letters over the stanzas of Psalm 119) is not bible4u's text,
/// and the verse after it opens a paragraph.</item>
/// </list>
/// A mark at the head of eBible's verse opens the verse's first word, since both editions start the verse there. Any
/// other is laid on the word eBible's word pairs with by its letters, as <see cref="KingJamesNumbers"/> pairs them for
/// the numbers, and a mark whose word finds no partner is dropped rather than put on a neighbour. Words are never added,
/// dropped or moved. The apocrypha, which eng-kjv2006 does not hold, keep the marks their own edition gives them.
/// </summary>
internal sealed class KingJamesParagraphs : ITextStage
{
    private const string Ebible = "KingJames2006";
    private const int LastBook = 66;

    /// <summary>
    /// What a pilcrow is read as: a word of letters no edition prints, so that the reader keeps its place among the
    /// words and leaves the markup around it as it is.
    /// </summary>
    private const string PilcrowWord = "zzpilcrowzz";

    public bool AppliesTo(string slug) => slug == Bible4uTextSource.KingJames;

    public IReadOnlyList<string> Sources(string slug) => [Ebible];

    public void Apply(ConvertedText text, string resources)
    {
        var laid = Lay(text.Verses, Path.Combine(resources, Ebible));
        text.Provenance.Add(new("break", $"{Ebible} (eBible eng-kjv2006, Public Domain): {laid.Paragraphs} paragraphs and "
                                         + $"{laid.Lines} lines, {laid.Dropped.Count} marks on a word with no partner left out"));
    }

    /// <param name="Dropped">The verses whose mark found no word to stand on, one entry a mark.</param>
    internal sealed record Laid(int Paragraphs, int Lines, IReadOnlyList<VerseKey> Dropped);

    internal static Laid Lay(IReadOnlyList<ConvertedVerse> printed, string folder)
    {
        var verses = printed.Where(v => v.Key.VerseLabel.Length == 0).ToDictionary(v => (v.Key.Book, v.Key.Chapter, v.Key.Verse));
        var paragraph = EnumSpelling.Of(TextBreak.Paragraph);
        var line = EnumSpelling.Of(TextBreak.Line);
        var dropped = new List<VerseKey>();

        foreach (var file in Directory.EnumerateFiles(folder, "*.usfm").Order(StringComparer.Ordinal))
        {
            var book = UsfmReader.Read(File.ReadAllText(file).Replace("¶", $" {PilcrowWord} ", StringComparison.Ordinal));
            if (BookCodes.TryGetOrdinal(book.Book, out var ordinal) is false || ordinal > LastBook)
            {
                continue;
            }

            TextBreak? pending = null;
            foreach (var chapter in book.Chapters)
            {
                foreach (var verse in chapter.Verses.Where(v => v.Label.Length == 0))
                {
                    var (theirs, marks) = Marked(verse.Words, ref pending);
                    if (marks.Count == 0
                        || !verses.TryGetValue((ordinal, chapter.Number, verse.Number), out var ours) || ours.Words.Count == 0)
                    {
                        continue;
                    }

                    var partner = KingJamesNumbers.Paired(ours.Words, theirs).ToDictionary(pair => pair.Theirs, pair => pair.Mine);
                    foreach (var (at, kind) in marks)
                    {
                        int? mine = at == 0 ? 0 : partner.TryGetValue(at, out var paired) ? paired : null;
                        if (mine is not { } position)
                        {
                            dropped.Add(ours.Key);
                            continue;
                        }

                        var word = ours.Words[position];
                        if (word.Break != paragraph)
                        {
                            word.Break = kind == TextBreak.Paragraph ? paragraph : line;
                        }
                    }
                }
            }
        }

        var opened = verses.Values.Where(v => v.Key.Book <= LastBook).SelectMany(v => v.Words).Select(word => word.Break).ToList();
        return new Laid(opened.Count(b => b == paragraph), opened.Count(b => b == line), dropped);
    }

    /// <summary>
    /// eBible's words of the verse with the pilcrows and heading words taken out, and the breaks on the words kept: a
    /// pilcrow or a heading, and a break the reader set on one, opens the word after it, in the next verse when nothing
    /// follows in this one.
    /// </summary>
    private static (List<(string Surface, string? StrongNumber)> Words, List<(int At, TextBreak Kind)> Marks) Marked(
        IReadOnlyList<UsfmWord> words, ref TextBreak? pending)
    {
        var kept = new List<(string, string?)>(words.Count);
        var marks = new List<(int, TextBreak)>();
        foreach (var word in words)
        {
            pending = Stronger(pending, word.Break);
            if (word.Surface == PilcrowWord || word.Heading)
            {
                pending = TextBreak.Paragraph;
                continue;
            }

            if (pending is { } kind)
            {
                marks.Add((kept.Count, kind));
                pending = null;
            }

            kept.Add((word.Surface, word.StrongNumber));
        }

        return (kept, marks);
    }

    private static TextBreak? Stronger(TextBreak? held, TextBreak? next) =>
        held == TextBreak.Paragraph || next is null ? held : next;
}
