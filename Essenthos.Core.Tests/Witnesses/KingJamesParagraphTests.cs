using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// The King James's paragraphs and psalm lines, laid from eBible's eng-kjv2006 onto bible4u's words: the mark
/// lands on the word eBible's word pairs with and the words themselves stay as they are.
/// </summary>
public sealed class KingJamesParagraphTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("kjv-paragraphs-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void AParagraphMarkOrAPilcrowOpensTheWordItStandsBeforeInsideAVerseAsWellAsAtItsHead()
    {
        Write("02-GENeng-kjv2006.usfm",
            """
            \id GEN
            \c 2
            \p
            \v 3 And God blessed the seventh day.
            \v 4 These are the generations of the heavens. ¶ In the day that the LORD God made
            \v 5 ¶ And every plant of the field
            """);
        var verses = new List<ConvertedVerse>
        {
            Verse(1, 2, 3, "And God blessed the seventh day"),
            Verse(1, 2, 4, "These are the generations of the heavens In the day that the LORD God made"),
            Verse(1, 2, 5, "And every plant of the field"),
        };

        var laid = KingJamesParagraphs.Lay(verses, _folder);

        Opened(verses).Should().Equal(("2:3", "And", "paragraph"), ("2:4", "In", "paragraph"), ("2:5", "And", "paragraph"));
        laid.Should().BeEquivalentTo(new KingJamesParagraphs.Laid(3, 0, []));
    }

    [Fact]
    public void APsalmVerseOpensALineAndABlankLineBetweenStanzasAParagraphAfterTheTitle()
    {
        Write("20-PSAeng-kjv2006.usfm",
            """
            \id PSA
            \c 3
            \d A Psalm of David, when he fled from Absalom his son.
            \q1
            \v 1 LORD, how are they increased that trouble me!
            \q1
            \v 2 Many there be which say of my soul
            \b
            \q1
            \v 3 But thou, O LORD, art a shield for me
            """);
        var verses = new List<ConvertedVerse>
        {
            Verse(19, 3, 1, "A Psalm of David when he fled from Absalom his son LORD how are they increased that trouble me"),
            Verse(19, 3, 2, "Many there be which say of my soul"),
            Verse(19, 3, 3, "But thou O LORD art a shield for me"),
        };

        KingJamesParagraphs.Lay(verses, _folder);

        Opened(verses).Should().Equal(("3:1", "LORD", "line"), ("3:2", "Many", "line"), ("3:3", "But", "paragraph"));
    }

    [Fact]
    public void AStanzaHeadingBibleForYouDoesNotPrintOpensAParagraphOnTheVerseAfterIt()
    {
        Write("20-PSAeng-kjv2006.usfm",
            """
            \id PSA
            \c 119
            \q1
            \v 32 I will run the way of thy commandments, when thou shalt enlarge my heart.
            \b
            \s1 \tl ה HE.\tl*
            \q1
            \v 33 Teach me, O LORD, the way of thy statutes
            \q1
            \v 40 Behold, I have longed after thy precepts
            \s1 \tl ו VAU.\tl*
            \q1
            \v 41 Let thy mercies come also unto me
            """);
        var verses = new List<ConvertedVerse>
        {
            Verse(19, 119, 32, "I will run the way of thy commandments when thou shalt enlarge my heart"),
            Verse(19, 119, 33, "Teach me O LORD the way of thy statutes"),
            Verse(19, 119, 40, "Behold I have longed after thy precepts"),
            Verse(19, 119, 41, "Let thy mercies come also unto me"),
        };

        var laid = KingJamesParagraphs.Lay(verses, _folder);

        Opened(verses).Should().Equal(
            ("119:32", "I", "line"), ("119:33", "Teach", "paragraph"), ("119:40", "Behold", "line"), ("119:41", "Let", "paragraph"));
        laid.Dropped.Should().BeEmpty();
    }

    [Fact]
    public void AMarkAtAVersesHeadOpensItsFirstWordAndOneInsideItWhoseWordHasNoPartnerIsDropped()
    {
        Write("02-GENeng-kjv2006.usfm",
            """
            \id GEN
            \c 4
            \p
            \v 1 Adam knew Eve his wife. ¶ Sheweth she conceived
            """);
        var verses = new List<ConvertedVerse> { Verse(1, 4, 1, "And Adam knew Eve his wife Showeth she conceived") };

        var laid = KingJamesParagraphs.Lay(verses, _folder);

        Opened(verses).Should().Equal(("4:1", "And", "paragraph"));
        laid.Dropped.Should().Equal(verses[0].Key);
        verses[0].Words.Select(word => word.Surface).Should().Equal("And Adam knew Eve his wife Showeth she conceived".Split(' '));
    }

    [Fact]
    public void TheBooksEbibleDoesNotHoldKeepTheirOwnMarks()
    {
        Write("02-GENeng-kjv2006.usfm",
            """
            \id GEN
            \c 1
            \p
            \v 1 In the beginning
            """);
        var apocrypha = Verse(67, 1, 1, "And Josias held the feast");
        apocrypha.Words[0].Break = "line";

        var laid = KingJamesParagraphs.Lay([Verse(1, 1, 1, "In the beginning"), apocrypha], _folder);

        apocrypha.Words.Select(word => word.Break).Should().Equal("line", null, null, null, null);
        laid.Paragraphs.Should().Be(1);
    }

    private void Write(string name, string usfm) => File.WriteAllText(Path.Combine(_folder, name), usfm);

    private static ConvertedVerse Verse(int book, int chapter, int verse, string words) =>
        new(new VerseKey(book, chapter, string.Empty, verse, string.Empty), verse,
            [.. words.Split(' ').Select(surface => new ConvertedWord { Surface = surface, Trailer = " " })]);

    private static IEnumerable<(string Verse, string Word, string? Break)> Opened(IEnumerable<ConvertedVerse> verses) =>
        verses.SelectMany(verse => verse.Words.Where(word => word.Break != null)
            .Select(word => ($"{verse.Key.Chapter}:{verse.Key.Verse}", word.Surface, word.Break)));
}
