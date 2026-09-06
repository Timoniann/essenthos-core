using Essenthos.Core.Loading;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The inline markers, which the reader learned about for this text.
///
/// Brenton's files carry none, so until now a marker standing inside a line would have been split
/// on whitespace and put into the corpus as words. The Ukrainian carries 204 footnotes written that
/// way, in which Kulish glosses his own transliterations — so the failure would not have looked
/// like corruption. It would have looked like Genesis saying <c>Червоний</c>.
///
/// Its other 2,079 inline markers are the opposite case and have to be told apart from the first:
/// <c>\wj</c> encloses words of the text, and dropping what it encloses would take most of what
/// Jesus says out of the Gospels.
/// </summary>
public class UsfmNoteTests
{
    private const string WithANote =
        """
        \id GEN
        \c 25
        \p
        \v 30 прізвище йому Єдом\f + \ft Червоний.\f*.
        """;

    [Fact]
    public void AFootnoteIsNotScripture()
    {
        var words = UsfmReader.Read(WithANote).Chapters[0]!.Verses[0]!.Words;

        words.Select(word => word.Surface).Should().Equal("прізвище", "йому", "Єдом");
        words.Should().NotContain(word => word.Surface == "Червоний");
    }

    /// <summary>
    /// The note leaves, and the sentence closes as it was printed: the full stop after the closing
    /// marker belongs to the last word of the verse, not to the note.
    /// </summary>
    [Fact]
    public void TheWordTheNoteHangsOffKeepsItsPunctuation() =>
        UsfmReader.Read(WithANote).Chapters[0]!.Verses[0]!.Words[^1]!.Trailer.Should().Be(". ");

    /// <summary>
    /// The other half of the same decision. A marker that wraps words of the text loses the marker
    /// and keeps the words — the corpus records no speaker, so the claim goes and the verse stays.
    /// </summary>
    [Fact]
    public void WordsOfJesusAreStillWords()
    {
        const string spoken =
            """
            \id MAT
            \c 3
            \p
            \v 15 рече до него: \wj Допусти тепер.\wj* Тодї допустив Його.
            """;

        UsfmReader.Read(spoken).Chapters[0]!.Verses[0]!.Words.Select(word => word.Surface)
            .Should().Equal("рече", "до", "него", "Допусти", "тепер", "Тодї", "допустив", "Його");
    }

    [Fact]
    public void AnInlineMarkerItHasNotBeenToldAboutIsAnError()
    {
        const string unknown =
            """
            \id GEN
            \c 1
            \v 1 alpha \add beta\add* gamma
            """;

        var act = () => UsfmReader.Read(unknown);

        act.Should().Throw<InvalidOperationException>().WithMessage("*add*");
    }
}

/// <summary>Read once; sixty-six USFM files is about a second.</summary>
public sealed class Kulish
{
    internal TextSource Source { get; } = KulishTextSource.Read(TestResources.KulishFolder);

    internal BookDraft Book(int canonical) => Source.Books.Single(book => book.CanonicalOrdinal == canonical);
}

/// <summary>
/// The first complete Ukrainian Bible, as it came off eBible: 66 books, no annotation, and nothing
/// to reconcile with anybody's numbering. It is the plainest text in the corpus, which is worth
/// checking rather than assuming — a load that quietly dropped a book would look exactly like this.
/// </summary>
public class KulishCorpusTests(Kulish kulish) : IClassFixture<Kulish>
{
    /// <summary>
    /// The counts eBible's own catalogue states for this text: 23,127 Old Testament verses in 929
    /// chapters and 7,955 New Testament verses in 260. They are checked here rather than trusted
    /// because a partial download is the failure this load can actually have.
    /// </summary>
    [Fact]
    public void TheWholeBibleIsRead()
    {
        kulish.Source.Books.Should().HaveCount(66);
        kulish.Source.Books.Sum(book => book.Chapters.Count).Should().Be(929 + 260);
        kulish.Source.Books.Sum(book => book.Chapters.Sum(chapter => chapter.Verses.Count))
            .Should().Be(23127 + 7955);
    }

    /// <summary>
    /// What the reader gets out of it, which no catalogue states and only counting establishes.
    /// A number here that moves without the verse counts moving is the tokeniser changing its mind,
    /// which is the change that would silently re-align every link ever built from this text.
    ///
    /// Splitting the files on whitespace instead gives 587,433. The whole of the difference is
    /// 2,774 tokens with no letter in them, 2,770 of which are a lone em-dash: the reader hangs
    /// punctuation standing on its own off the word before it rather than counting it as a word.
    /// </summary>
    [Fact]
    public void EveryWordIsCounted() =>
        kulish.Source.Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .Sum(verse => verse.Words.Count)
            .Should().Be(584659);

    /// <summary>
    /// Its order is the canonical one, which is what lets the ordinal be the position. Checked
    /// rather than assumed: the file names carry eBible's own numbering, in which Genesis is 02 and
    /// Matthew is 70, and reading either as an ordinal would put every book in the wrong place.
    /// </summary>
    [Fact]
    public void EveryBookStandsWhereTheCanonPutsIt()
    {
        kulish.Source.Books.Select(book => book.CanonicalOrdinal).Should().Equal(Enumerable.Range(1, 66));
        kulish.Source.Books.Should().OnlyContain(book => book.Position == book.CanonicalOrdinal);
    }

    /// <summary>
    /// The numbering is the English one throughout, which is what lets this text stand beside the
    /// King James in the shared frame with no rule of its own: 150 psalms with 9 and 10 apart,
    /// Malachi in four chapters where the Hebrew has three, Joel in three where it has four.
    /// </summary>
    [Theory]
    [InlineData(19, 150)]
    [InlineData(39, 4)]
    [InlineData(29, 3)]
    public void ItIsNumberedTheWayTheKingJamesIs(int ordinal, int chapters) =>
        kulish.Book(ordinal).Chapters.Should().HaveCount(chapters);

    /// <summary>
    /// The one line every reader of this text will recognise, and the shortest proof that the file
    /// is Kulish's and not somebody else's: Ohienko opens Genesis "На початку Бог створив Небо та
    /// землю" and this opens it in a spelling ninety years older.
    /// </summary>
    [Fact]
    public void GenesisOpensInKulishsOwnUkrainian()
    {
        var verse = kulish.Book(1).Chapters[0]!.Verses[0]!;

        verse.Words.Select(word => word.Surface)
            .Should().Equal("У", "початку", "сотворив", "Бог", "небо", "та", "землю");
    }

    /// <summary>
    /// Every book is named in Ukrainian as well as in the canon's English, so a Ukrainian pane can
    /// be headed in Ukrainian. The edition's own name for a book is the one thing it says about the
    /// book as a whole, and it is Kulish's own: he calls Genesis the first book of Moses.
    /// </summary>
    [Fact]
    public void EveryBookIsNamedInItsOwnLanguage()
    {
        kulish.Source.Books.Should().OnlyContain(book => book.NameNative != null && book.NameNative != "");
        kulish.Book(1).NameNative.Should().Be("Перва книга Мойсея");
        kulish.Book(19).NameNative.Should().Be("Псалтир");
    }

    /// <summary>
    /// No word carries a lemma, a Strong number or a gloss, and the row says so. This text arrives
    /// with nothing but its words, and a check that it stays that way is what would catch a later
    /// pass writing derived annotation onto it as though the edition had supplied it.
    /// </summary>
    [Fact]
    public void ItCarriesNoAnnotationAtAll() =>
        kulish.Source.Books
            .SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses)
            .SelectMany(verse => verse.Words)
            .Should().OnlyContain(word =>
                word.Lemma == null && word.StrongNumber == null && word.Gloss == null
                && word.Morphology == null);

    /// <summary>
    /// A book missing from the folder stops the load rather than shortening the Bible. The
    /// Septuagint may be read from whichever of its files are present, because Brenton's canon is
    /// not fixed; a complete Bible short of a book is a broken download.
    /// </summary>
    [Fact]
    public void APartialFolderIsRefused()
    {
        var empty = Directory.CreateTempSubdirectory("kulish-partial");

        try
        {
            var act = () => KulishTextSource.Read(empty.FullName);

            act.Should().Throw<InvalidOperationException>().WithMessage("*GEN*");
        }
        finally
        {
            empty.Delete(recursive: true);
        }
    }
}
