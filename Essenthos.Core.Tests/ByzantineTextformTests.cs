using System.Text.Json;
using Essenthos.Core.Byzantine;
using Essenthos.Core.Loading;
using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Robinson and Pierpont's Byzantine Textform, read out of the beta code and checked against the
/// repository's own Unicode conversion of the same files.
///
/// The conversion is the part worth checking and the part nothing else would catch. The alphabet
/// here is not the one the Textus Receptus composite uses — <c>c</c> and <c>x</c> trade places, and
/// a final sigma is decided by position rather than written — so the wrong table produces Greek
/// that is wrong in every verse and looks like Greek. The answer key is the maintainers' own output
/// from the same source files, so the check is over all 140,149 words rather than over a sample.
/// </summary>
public class ByzantineTextformTests
{
    /// <summary>Every verse address the file writes, including the four it writes empty.</summary>
    private const int Addresses = 7_957;

    /// <summary>The verses this edition actually prints.</summary>
    private const int Verses = 7_953;

    private const int Words = 140_149;

    /// <summary>
    /// The four addresses the file records with no words under them. They are the best-known
    /// omissions of the majority text, and the Received Text has all four — so they are exactly
    /// what a reader comparing this edition against Scrivener is looking for.
    /// </summary>
    private static readonly (string Book, int Chapter, int Verse)[] Omitted =
    [
        ("03_LUK", 17, 36), ("05_ACT", 8, 37), ("05_ACT", 15, 34), ("05_ACT", 24, 7),
    ];

    private static IReadOnlyList<Bp5Verse> Book(string stem) =>
        Bp5Reader.Read(File.ReadAllText(TestResources.Byzantine(stem)));

    private static IEnumerable<(string Book, Bp5Verse Verse)> WholeNewTestament() =>
        ByzantineTextSource.Books.SelectMany(book => Book(book).Select(verse => (book, verse)));

    /// <summary>
    /// The Greek words of each verse as the repository's own converter published them, from the
    /// same beta-code files. Three columns, no quoting anywhere in the 27 files, and the numbers
    /// and parses interleaved with the words are dropped — they are not what is being checked.
    /// </summary>
    private static Dictionary<(int, int), List<string>> Published(string stem)
    {
        var verses = new Dictionary<(int, int), List<string>>();

        foreach (var line in File.ReadLines(TestResources.ByzantineUnicode(stem)).Skip(1))
        {
            var columns = line.Split(',', 3);
            if (columns.Length < 3)
            {
                continue;
            }

            verses[(int.Parse(columns[0]), int.Parse(columns[1]))] =
            [
                .. columns[2].Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Where(token => !token.StartsWith('{') && !token.All(char.IsAsciiDigit)),
            ];
        }

        return verses;
    }

    [Fact]
    public void TheWholeNewTestamentIsRead()
    {
        var read = WholeNewTestament().ToList();

        read.Should().HaveCount(Addresses);
        read.Count(entry => entry.Verse.Words.Count > 0).Should().Be(Verses);
        read.Sum(entry => entry.Verse.Words.Count).Should().Be(Words);
        read.Select(entry => entry.Book).Distinct().Should().HaveCount(27);
    }

    /// <summary>
    /// The four verses this edition does not have, which it states rather than skips. Reading the
    /// address as a verse would put four empty verses into the corpus and tell a reader the
    /// majority text has them; skipping the line without noticing would lose the one piece of
    /// evidence that says the omission is deliberate.
    /// </summary>
    [Fact]
    public void TheFourVersesTheMajorityTextOmitsAreWrittenEmptyAndNotLoaded()
    {
        foreach (var (book, chapter, verse) in Omitted)
        {
            Book(book).Single(v => v.Chapter == chapter && v.Number == verse)
                .Words.Should().BeEmpty();
        }

        var loaded = ByzantineTextSource.Read(TestResources.ByzantineFolder);
        var addresses = loaded.Books
            .SelectMany(book => book.Chapters.SelectMany(chapter => chapter.Verses
                .Select(v => (book.CanonicalOrdinal, chapter.Number, v.Number))))
            .ToHashSet();

        addresses.Should().HaveCount(Verses);
        addresses.Should().NotContain((42, 17, 36));
        addresses.Should().NotContain((44, 8, 37));
        addresses.Should().NotContain((44, 15, 34));
        addresses.Should().NotContain((44, 24, 7));
    }

    /// <summary>
    /// Every word carries a number and a parse. This is the property the whole task rests on: it is
    /// why joining this edition to the other three Greek witnesses needs no aligner, and if it ever
    /// stops being true the links become guesses without anything else changing.
    /// </summary>
    [Fact]
    public void EveryWordCarriesANumberAndAParse() =>
        WholeNewTestament()
            .SelectMany(entry => entry.Verse.Words)
            .Should().OnlyContain(word => word.Strong.Length > 0 && word.Morphology.Length > 0);

    [Fact]
    public void TheFirstVerseIsReadWordForWord()
    {
        var matthew = Book("01_MAT")[0];

        matthew.Chapter.Should().Be(1);
        matthew.Number.Should().Be(1);
        matthew.Words.Should().Equal(
            new Bp5Word("biblos", "976", "N-NSF"),
            new Bp5Word("genesews", "1078", "N-GSF"),
            new Bp5Word("ihsou", "2424", "N-GSM"),
            new Bp5Word("xristou", "5547", "N-GSM"),
            new Bp5Word("uiou", "5207", "N-GSM"),
            new Bp5Word("dauid", "1138", "N-PRI"),
            new Bp5Word("uiou", "5207", "N-GSM"),
            new Bp5Word("abraam", "11", "N-PRI"));
    }

    /// <summary>
    /// A second parse offered for one word is a second parse, not a second word. Read as a word it
    /// puts "1093" into the text and shifts everything after it in the verse, which is the same
    /// mistake the Textus Receptus reader had to be taught not to make.
    /// </summary>
    [Fact]
    public void ASecondParseForOneWordDoesNotBecomeAWord()
    {
        var verse = Book("01_MAT").Single(v => v is { Chapter: 4, Number: 15 });

        verse.Words.Should().HaveCount(13);
        verse.Words[0].Should().Be(new Bp5Word("gh", "1093", "N-NSF", ["1093", "N-VSF"]));
        verse.Words[1].Surface.Should().Be("zaboulwn");
        verse.Words.Should().OnlyContain(word => word.Surface.All(char.IsLetter));
    }

    /// <summary>
    /// The alphabet, against the repository's own conversion of the same files, word for word over
    /// the whole New Testament. Deriving the table from this output is not the same as it being
    /// right, and one letter wrong is thousands of words wrong.
    /// </summary>
    [Fact]
    public void EveryWordConvertsToTheGreekTheEditionItselfPublishes()
    {
        var wrong = new List<string>();
        var checkedWords = 0;

        foreach (var stem in ByzantineTextSource.Books)
        {
            var answer = Published(stem);

            foreach (var verse in Book(stem).Where(v => v.Words.Count > 0))
            {
                var key = (verse.Chapter, verse.Number);
                answer.Should().ContainKey(key,
                    $"{stem} {verse.Chapter}:{verse.Number} is in the beta code and not in the Unicode " +
                    "the same repository generated from it");

                var published = answer[key];
                published.Should().HaveCount(verse.Words.Count);

                for (var at = 0; at < verse.Words.Count; at++)
                {
                    var converted = Bp5BetaCode.ToGreek(verse.Words[at].Surface);
                    checkedWords++;
                    if (converted != published[at])
                    {
                        wrong.Add($"{stem} {verse.Chapter}:{verse.Number} word {at + 1}: " +
                                  $"{verse.Words[at].Surface} became {converted}, published as {published[at]}");
                    }
                }
            }
        }

        checkedWords.Should().Be(Words);
        wrong.Should().BeEmpty();
    }

    /// <summary>
    /// The other Greek editions are read with the composite's alphabet, and it is a different one.
    /// Using either table on the other file is silent — it produces Greek letters throughout — so
    /// this is the check that says the two are not interchangeable.
    /// </summary>
    [Fact]
    public void TheCompositesAlphabetIsNotThisOne()
    {
        Bp5BetaCode.ToGreek("xristou").Should().Be("χριστου");
        BetaCode.ToGreek("xristou").Should().Be("ξριστου");

        Bp5BetaCode.ToGreek("biblos").Should().Be("βιβλος");
        BetaCode.ToGreek("biblos").Should().Be("βιβλοσ");
    }

    /// <summary>
    /// A sigma inside a word and a sigma closing it are the same letter in the file and two
    /// characters in Unicode. The fold used to compare witnesses treats them as one, so getting
    /// this wrong would not break a link — it would only put a letter on the page that no printed
    /// edition of the Greek New Testament uses.
    /// </summary>
    [Fact]
    public void OnlyASigmaClosingAWordIsAFinalSigma()
    {
        Bp5BetaCode.ToGreek("ihsous").Should().Be("ιησους");
        Bp5BetaCode.ToGreek("estin").Should().Be("εστιν");
        Bp5BetaCode.ToGreek("swsei").Should().Be("σωσει");
    }

    /// <summary>
    /// The text as the corpus stores it: canonical ordinals, Strong numbers with their G, and the
    /// parse kept as Robinson wrote it rather than expanded.
    /// </summary>
    [Fact]
    public void TheTextIsPlacedInTheCanonAsTheOtherGreekEditionsAre()
    {
        var source = ByzantineTextSource.Read(TestResources.ByzantineFolder);

        source.Books.Should().HaveCount(27);
        source.Books.Select(book => book.CanonicalOrdinal).Should().Equal(Enumerable.Range(40, 27));
        source.Books[0].Name.Should().Be("Matthew");
        source.Books[^1].Name.Should().Be("Revelation");
        var verses = source.Books.SelectMany(book => book.Chapters).SelectMany(c => c.Verses).ToList();
        verses.Should().HaveCount(Verses);
        verses.Sum(verse => verse.Words.Count).Should().Be(Words);

        var first = source.Books[0].Chapters[0].Verses[0].Words;
        first[0].Surface.Should().Be("βιβλος");
        first[0].StrongNumber.Should().Be("G976");
        first[0].Trailer.Should().Be(" ");
        first[^1].Trailer.Should().BeEmpty();

        JsonSerializer.Deserialize<Dictionary<string, string>>(first[0].Morphology!)
            .Should().ContainKey("robinson").WhoseValue.Should().Be("N-NSF");
    }

    /// <summary>
    /// A text is refused at load without a licence and a redistribution decision, and this one is
    /// the rare case where both statements attached to the bytes agree. The check is that the row
    /// says so rather than that somebody remembered.
    /// </summary>
    [Fact]
    public void TheEditionSaysWhoseItIsAndUnderWhatTerms()
    {
        var definition = ByzantineTextSource.Definition;

        definition.Invoking(d => d.Validate()).Should().NotThrow();
        definition.Editors.Should().Contain("Robinson").And.Contain("Pierpont");
        definition.TextualFamily.Should().Be("Byzantine");
        definition.EditionYear.Should().Be(2018);
        definition.RightsNote.Should().NotBeNullOrWhiteSpace();
    }
}
