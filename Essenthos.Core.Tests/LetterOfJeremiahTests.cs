using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Frame;
using FluentAssertions;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Letter of Jeremiah under both its names: a book of its own in the Greek and the Synodal, the
/// sixth chapter of Baruch in the Latin and the English, and one passage in the standard's
/// seventy-three verses whichever way an edition divides it.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class LetterOfJeremiahFrameTests(VersificationFrames frames) : IClassFixture<VersificationFrames>
{
    private const int Letter = 76;

    private const int Baruch = 67;

    private const int Esther = 17;

    [Fact]
    public void TheGreekLetterStandsInItsOwnBookAndCoversBaruchSix()
    {
        var placed = Placed(Versification.Septuagint, [.. Chapter(Letter, 1, 73)], Letter, 1, 5);

        placed.Should().Equal("76.1.5*", "67.6.5");
    }

    [Fact]
    public void TheKingJamessBaruchSixStandsInBaruchAndCoversTheLetter()
    {
        var placed = Placed(Versification.English,
            [.. Chapter(Baruch, 3, 37), .. Chapter(Baruch, 6, 73)], Baruch, 6, 73);

        placed.Should().Equal("67.6.73*", "76.1.73");
    }

    /// <summary>
    /// The Synodal runs the standard's 5 and 6 into one verse and stands one behind from there, as
    /// Brenton's English reads beside it.
    /// </summary>
    [Theory]
    [InlineData(1, new[] { "76.1.1*", "67.6.1" })]
    [InlineData(5, new[] { "76.1.5*", "76.1.6", "67.6.5", "67.6.6" })]
    [InlineData(13, new[] { "76.1.14*", "67.6.14" })]
    [InlineData(44, new[] { "76.1.44*", "67.6.44" })]
    [InlineData(72, new[] { "76.1.73*", "67.6.73" })]
    public void TheSynodalsLetterStandsAtTheStandardsVerses(int verse, string[] expected)
    {
        var placed = Placed(Versification.English,
            [.. Chapter(Letter, 1, 72), (Esther, 5, 1, string.Empty, 50)], Letter, 1, verse);

        placed.Should().Equal(expected);
    }

    /// <summary>
    /// The Vulgate prints the letter's title as a heading, so its first verse is the standard's second.
    /// </summary>
    [Theory]
    [InlineData(1, new[] { "67.6.2*", "76.1.2" })]
    [InlineData(44, new[] { "67.6.44*", "76.1.44" })]
    [InlineData(50, new[] { "67.6.50*", "67.6.51", "76.1.50", "76.1.51" })]
    [InlineData(72, new[] { "67.6.73*", "76.1.73" })]
    public void TheVulgatesBaruchSixStandsAtTheStandardsVerses(int verse, string[] expected)
    {
        var placed = Placed(Versification.Vulgate,
            [.. Chapter(Baruch, 3, 38), .. Chapter(Baruch, 6, 72)], Baruch, 6, verse);

        placed.Should().Equal(expected);
    }

    /// <summary>The Latin and Greek divide the standard's Baruch 3:34 in two and number on from there.</summary>
    [Theory]
    [InlineData(34, 34)]
    [InlineData(35, 34)]
    [InlineData(38, 37)]
    public void ADividedBaruchThreeStandsAtTheStandardsVerses(int verse, int standard)
    {
        Placed(Versification.Vulgate, [.. Chapter(Baruch, 3, 38)], Baruch, 3, verse)
            .Should().Equal($"67.3.{standard}*");
    }

    [Fact]
    public void OneNameOfTheLetterNamesTheOther()
    {
        LetterOfJeremiah.Twin(Baruch, 6).Should().Be((Letter, 1));
        LetterOfJeremiah.Twin(Letter, 1).Should().Be((Baruch, 6));
        LetterOfJeremiah.Twin(Baruch, 5).Should().BeNull();
        LetterOfJeremiah.Joined((Baruch, 6, 12)).Should().Be((Letter, 1, 12));
        LetterOfJeremiah.Joined((Baruch, 5, 9)).Should().Be((Baruch, 5, 9));
    }

    private List<string> Placed(
        Versification tradition,
        IReadOnlyList<(int Book, int Chapter, int Verse, string Label, int Length)> edition,
        int book,
        int chapter,
        int verse)
    {
        var verses = edition
            .Select((printed, index) => new PlacedVerse(
                index + 1, printed.Book, printed.Chapter, printed.Verse, printed.Label, printed.Length))
            .ToList();
        var id = verses.Single(v => v.Book == book && v.Chapter == chapter && v.Number == verse).Id;

        return
        [
            .. CanonicalFrameLoader.Expected(frames.Rules, tradition, verses)
                .Where(reference => reference.VerseId == id)
                .Select(reference =>
                    $"{reference.Book}.{reference.Chapter}.{reference.Verse}{(reference.IsPrimary ? "*" : string.Empty)}"),
        ];
    }

    private static IEnumerable<(int, int, int, string, int)> Chapter(int book, int chapter, int verses) =>
        Enumerable.Range(1, verses).Select(verse => (book, chapter, verse, string.Empty, 50));
}

/// <summary>A reader asking for either name of the letter reads each text under the name it prints.</summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LetterOfJeremiahReadingTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IDbContextTransaction _transaction;

    public LetterOfJeremiahReadingTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _transaction = _db.Database.BeginTransaction();
    }

    public void Dispose()
    {
        _transaction.Rollback();
        _transaction.Dispose();
        _db.Dispose();
    }

    [Fact]
    public async Task EachTextIsReadUnderTheNameItPrintsTheLetterUnder()
    {
        var english = Corpus.Add(_db, "KJV", TextKind.Translation, "eng", (6, 1, ["A", "copy"]));
        var greek = Corpus.Add(_db, "GRCBRENT", TextKind.ManuscriptTradition, "grc", (1, 1, ["Ἀντίγραφον"]));
        _db.SaveChanges();
        _db.In(english, LetterOfJeremiah.Baruch);
        _db.In(greek, LetterOfJeremiah.Book);

        (await ParallelEndpoints.Held(_db, greek.Id, LetterOfJeremiah.Baruch, 6, default))
            .Should().Be((LetterOfJeremiah.Book, 1));
        (await ParallelEndpoints.Held(_db, english.Id, LetterOfJeremiah.Baruch, 6, default))
            .Should().Be((LetterOfJeremiah.Baruch, 6));
        (await ParallelEndpoints.Held(_db, english.Id, LetterOfJeremiah.Book, 1, default))
            .Should().Be((LetterOfJeremiah.Baruch, 6));
        (await ParallelEndpoints.Held(_db, english.Id, LetterOfJeremiah.Baruch, 5, default))
            .Should().Be((LetterOfJeremiah.Baruch, 5));
    }
}
