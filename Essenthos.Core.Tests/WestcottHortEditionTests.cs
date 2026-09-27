using System.Text.Json;
using Essenthos.Core.Loading;
using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which of the two texts in Robinson's Westcott-Hort file is the one Westcott and Hort printed.
///
/// The file is not one edition. Westcott and Hort published a second set of readings in their
/// margin, and Robinson carries both inline in the same three-pipe notation his Textus Receptus
/// uses for Stephanus against Scrivener — at 1,653 places rather than 261. A transcription that
/// silently preferred one side is a different edition from one that preferred the other, and both
/// would load without complaint, so the side is proved here rather than picked.
///
/// The proof is the repository's own plain transcription of the same text, which carries no
/// variants at all. Taking the first alternative reproduces it; taking the second does not, and by
/// a margin nothing could close by accident. This is the same shape of check as
/// <see cref="ScrivenerExtractionTests"/> and for the same reason: the rule is a rule, and this is
/// what says the rule was right.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public sealed class WestcottHortEditionTests(ITestOutputHelper output)
{
    /// <summary>Every address the parsed file writes, including the four it writes empty.</summary>
    private const int Addresses = 7_941;

    /// <summary>
    /// The one address the file writes that the printed text has no words for. Sixteen verses the
    /// critical text omits outright are simply not numbered here, and Matthew 12:47 is the
    /// seventeenth: Westcott and Hort left it out of the text and put it in the margin, so the
    /// group that holds it is empty on the side this edition is read from — which is one more thing
    /// that would be the wrong way round if the sides were swapped.
    /// </summary>
    private const int Empty = 1;

    /// <summary>
    /// How many verses the printed side has to reproduce exactly. Today it is 7,887 of 7,940 and
    /// the other side manages 6,774; the floor is set below the first and far above the second, so
    /// a transcription change moves it and a swapped side breaks it.
    /// </summary>
    private const int Reproduced = 7_800;

    /// <summary>
    /// The plain transcription's beta code is not the parsed file's: there <c>y</c> is theta and
    /// <c>q</c> is psi, and here it is the other way round. Compared unfolded, every theta and
    /// every psi in the New Testament is a false mismatch.
    /// </summary>
    private static string Fold(string word) =>
        new([.. word.Select(letter => letter switch { 'q' => 'y', 'y' => 'q', _ => letter })]);

    /// <summary>
    /// The letters of a word, with the editors' brackets and Robinson's angle marks off. What is
    /// being compared is which words the two files hold, and the marks are in only one of them.
    /// </summary>
    private static string Bare(string word) => word.Trim('[', ']', '<', '>');

    private static IReadOnlyList<UtrVerse> Parsed(string book, Reading reading) =>
        UtrReader.Read(File.ReadAllText(TestResources.WestcottHort(book)), reading);

    /// <summary>
    /// The plain file, read by the reader that already knows this format — it is the Online Bible
    /// layout the Scrivener answer key is in. Its own bracketed words are the editors' brackets
    /// rather than a book title, so the title-skipping that reader does is checked against here
    /// rather than relied on: a WH book whose first piece was bracketed would lose words.
    /// </summary>
    private static Dictionary<(int, int), List<string>> Plain(string book)
    {
        var verses = new Dictionary<(int, int), List<string>>();
        var chapter = 0;
        var number = 0;

        foreach (var piece in File.ReadAllText(TestResources.WestcottHortPlain(book))
                     .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var colon = piece.IndexOf(':', StringComparison.Ordinal);
            if (colon > 0 && int.TryParse(piece[..colon], out var read)
                          && int.TryParse(piece[(colon + 1)..], out var verse))
            {
                (chapter, number) = (read, verse);
                verses[(chapter, number)] = [];
                continue;
            }

            if (chapter > 0)
            {
                verses[(chapter, number)].Add(Fold(Bare(piece)));
            }
        }

        return verses;
    }

    private static int Agreeing(Reading reading)
    {
        var agreeing = 0;

        foreach (var book in WestcottHortTextSource.Books)
        {
            var plain = Plain(book);
            foreach (var verse in Parsed(book, reading))
            {
                if (plain.TryGetValue((verse.Chapter, verse.Number), out var expected)
                    && expected.SequenceEqual(verse.Words.Select(word => Bare(word.Surface))))
                {
                    agreeing++;
                }
            }
        }

        return agreeing;
    }

    [Fact]
    public void TheFirstAlternativeIsTheTextAndTheSecondIsTheMargin()
    {
        var printed = Agreeing(Reading.First);
        var margin = Agreeing(Reading.Second);

        output.WriteLine($"of {Addresses} addresses, the first alternative reproduces the plain "
                         + $"transcription in {printed} verses and the second in {margin}");

        printed.Should().BeGreaterThan(Reproduced,
            "the side loaded as Westcott and Hort's text no longer reproduces the plain "
            + "transcription of it, so either the file changed or the wrong side is being taken");
        margin.Should().BeLessThan(printed - 1_000,
            "the two sides of the variant groups have come to agree with the plain transcription "
            + "about equally, which would mean nothing here can tell the text from the margin");
    }

    /// <summary>
    /// The whole New Testament, and the one address the printed text leaves empty. A verse row
    /// means the edition has that verse, so that one gets none.
    /// </summary>
    [Fact]
    public void TheEditionHoldsEveryVerseButTheOneItDoesNotPrint()
    {
        var verses = WestcottHortTextSource.Books.SelectMany(book => Parsed(book, Reading.First)).ToList();

        verses.Should().HaveCount(Addresses);
        verses.Count(verse => verse.Words.Count == 0).Should().Be(Empty);

        // Matthew 12:47, and only from this side. The margin has the verse, which is why reading
        // the wrong side would quietly put it back.
        Parsed("MT", Reading.First).Single(verse => verse is { Chapter: 12, Number: 47 })
            .Words.Should().BeEmpty();
        Parsed("MT", Reading.Second).Single(verse => verse is { Chapter: 12, Number: 47 })
            .Words.Should().NotBeEmpty();
    }

    /// <summary>
    /// The editors' double brackets, which are the reason to hold their text rather than a
    /// transcription of it. They open on one word and close on another eleven verses later, so a
    /// reader that took the bracket characters at face value would mark the two ends of the long
    /// ending of Mark and leave everything between them unmarked — saying the opposite of what the
    /// edition says.
    /// </summary>
    [Theory]
    [InlineData(41, 16, 9, "rejected")]
    [InlineData(41, 16, 14, "rejected")]
    [InlineData(41, 16, 20, "rejected")]
    [InlineData(43, 8, 6, "rejected")]
    [InlineData(42, 22, 43, "rejected")]
    [InlineData(42, 24, 12, "rejected")]
    public void EveryWordInsideTheEditorsDoubleBracketsSaysSo(
        int canonical, int chapter, int verse, string mark) =>
        Words(canonical, chapter, verse).Should()
            .OnlyContain(word => Brackets(word) == mark)
            .And.NotBeEmpty();

    /// <summary>
    /// A word Westcott and Hort merely doubted, against the words around it that they did not. The
    /// bracket opens and closes on the same word here, which is the common shape and the one an
    /// off-by-one in the span tracking would get wrong in the other direction.
    /// </summary>
    [Fact]
    public void ASingleBracketMarksTheOneWordItEncloses()
    {
        var matthew = Words(40, 1, 18);

        matthew.Should().HaveCount(26);
        matthew.Count(word => Brackets(word) == "doubtful").Should().Be(1);
        Brackets(matthew[2]).Should().Be("doubtful", "Matthew 1:18 prints [Ἰησοῦ] in brackets");
    }

    /// <summary>
    /// The brackets are marks and not letters. Left on the surface they would be searched for,
    /// folded, and shown to a reader as part of the word.
    /// </summary>
    [Fact]
    public void NoWordCarriesABracketInItsLetters() =>
        Loaded().Books.SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Words)
            .Should().OnlyContain(word => !word.Surface.Any(letter => "[]<>".Contains(letter)));

    /// <summary>
    /// Romans 10:5 closes a bracket after the Strong number rather than after the word. Unrepaired,
    /// <c>3588]</c> is not a number the parser recognises, so it becomes a word of Romans and every
    /// word after it in the verse is parsed as the one before. It is the only occurrence in the 27
    /// books, which is why it is a repair and not a rule.
    /// </summary>
    [Fact]
    public void TheOneMisplacedBracketDoesNotBecomeAWord()
    {
        var romans = Words(45, 10, 5);

        romans.Should().OnlyContain(word => word.StrongNumber != null);
        romans.Select(word => word.Surface).Should().NotContain("3588");
    }

    /// <summary>Read once: the whole New Testament, and eight of these tests ask about it.</summary>
    private static readonly Lazy<TextSource> Edition =
        new(() => WestcottHortTextSource.Read(TestResources.WestcottHortFolder));

    private static TextSource Loaded() => Edition.Value;

    private static IReadOnlyList<WordDraft> Words(int canonical, int chapter, int verse) =>
        Loaded().Books.Single(book => book.CanonicalOrdinal == canonical)
            .Chapters.Single(read => read.Number == chapter)
            .Verses.Single(read => read.Number == verse).Words;

    private static string? Brackets(WordDraft word) =>
        word.Morphology is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(word.Morphology)!
                .GetValueOrDefault("brackets");
}
