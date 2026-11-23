using Essenthos.Core.Loading;
using Essenthos.Core.Usfm;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// Young's square brackets (PRB-1993): the words he supplied for the English, printed in brackets in eBible's file, are
/// read as supplied spans with the brackets out of their letters, the way the American Standard's <c>\add</c> is. Each
/// case is a shape the file prints.
/// </summary>
public class YoungBracketsTests
{
    private static List<VerseDraft> Read(string verses)
    {
        var book = UsfmReader.Read($"\\id JOB\n\\c 29\n\\p\n{verses}");
        return [.. SquareBrackets.Supplied(book.Chapters.Select(chapter => EnglishTextSource.Chapter(chapter, tagged: false))).Single().Verses];
    }

    private static List<(string Surface, string Trailer, int? Supplied)> Words(VerseDraft verse) =>
        [.. verse.Words.Select(word => (word.Surface, word.Trailer, word.SuppliedSpan))];

    [Fact]
    public void ABracketedWordIsSuppliedAndPrintedWithoutItsBrackets()
    {
        var verse = Read("\\v 3 By His light I walk [through] darkness.\n").Single();

        Words(verse).Should().Equal(
            ("By", " ", null), ("His", " ", null), ("light", " ", null), ("I", " ", null), ("walk", " ", null),
            ("through", " ", 1), ("darkness", ". ", null));
    }

    [Fact]
    public void EachSpanIsNumberedInItsVerseAndEveryWordOfOneSharesItsNumber()
    {
        var verse = Read("\\v 4 And [it is] good, and the waters which [are] under [it];\n").Single();

        Words(verse).Should().Equal(
            ("And", " ", null), ("it", " ", 1), ("is", " ", 1), ("good", ", ", null), ("and", " ", null), ("the", " ", null),
            ("waters", " ", null), ("which", " ", null), ("are", " ", 2), ("under", " ", null), ("it", "; ", 3));
    }

    /// <summary>A quotation mark or Young's hyphen before the bracket is the edition's and stays; the word is still supplied.</summary>
    [Fact]
    public void PunctuationBeforeTheBracketStaysWithTheSuppliedWord()
    {
        var verse = Read("\\v 12 saying, '[It is] a sea -[monster], A [thing];'\n").Single();

        Words(verse).Should().Equal(
            ("saying", ", ", null), ("'It", " ", 1), ("is", " ", 1), ("a", " ", null), ("sea", " ", null), ("-monster", ", ", 2),
            ("A", " ", null), ("thing", ";' ", 3));
    }

    /// <summary>
    /// A bracket inside a word marks letters, not the word: «god[dess]» renders אֱלֹהֵי. And in «[evening -]present» only
    /// «evening» is supplied: the letters of «-present» stand after the bracket closes.
    /// </summary>
    [Fact]
    public void AWordIsSuppliedOnlyWhereItsFirstLetterStandsInsideTheBrackets()
    {
        var verse = Read("\\v 5 after Ashtoreth god[dess] of the [evening -]present, that\n").Single();

        Words(verse).Should().Equal(
            ("after", " ", null), ("Ashtoreth", " ", null), ("goddess", " ", null), ("of", " ", null), ("the", " ", null),
            ("evening", " ", 1), ("-present", ", ", null), ("that", " ", null));
    }

    /// <summary>1 John 5:7-8 opens a bracket in one verse and closes it in the next.</summary>
    [Fact]
    public void ASpanRunsOnIntoTheNextVerseUnderThatVersesOwnNumber()
    {
        var verses = Read("\\v 7 because three [in the heaven, are one;\n\\v 8 and three in the earth], the Spirit [is] one.\n");

        Words(verses[0]).Select(word => word.Supplied).Should().Equal(null, null, 1, 1, 1, 1, 1);
        Words(verses[1]).Should().Equal(
            ("and", " ", 1), ("three", " ", 1), ("in", " ", 1), ("the", " ", 1), ("earth", ", ", 1), ("the", " ", null),
            ("Spirit", " ", null), ("is", " ", 2), ("one", ". ", null));
    }

    [Fact]
    public void AVerseWithoutBracketsIsReadAsBefore()
    {
        const string verse = "\\v 9 Princes have kept in words, And a hand they place on their mouth.\n";
        var plain = UsfmReader.Read($"\\id JOB\n\\c 29\n\\p\n{verse}").Chapters.Select(chapter => EnglishTextSource.Chapter(chapter, tagged: false))
            .Single().Verses.Single();

        Words(Read(verse).Single()).Should().Equal(Words(plain));
    }
}
