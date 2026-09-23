using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The commandments file as it is committed: Maimonides' count, whole, each commandment resting on
/// verses of the Torah in the shared numbering.
/// </summary>
public sealed class CommandmentFileTests
{
    private const int Genesis = 1;
    private const int Deuteronomy = 5;

    private static readonly string Shipped = Path.Combine([AppContext.BaseDirectory, .. CommandmentLoader.FilePath]);

    private static readonly List<Commandment> Commandments = CommandmentLoader.Read(Shipped);

    private static Commandment Of(string kind, int number) =>
        Commandments.Single(c => c.Kind == kind && c.Number == number);

    [Fact]
    public void TheCountIsWholeAndRestsOnTheTorah()
    {
        Commandments.Should().HaveCount(613);
        Commandments.Count(c => c.Kind == CommandmentKinds.Positive).Should().Be(248);
        Commandments.Should().OnlyContain(c => c.References.Count > 0 && c.Title.Length > 0);
        Commandments.SelectMany(c => c.References)
            .Should().OnlyContain(r => r.CanonicalBook >= Genesis && r.CanonicalBook <= Deuteronomy);
    }

    [Fact]
    public void TheFirstCommandmentIsToKnowThereIsAGod()
    {
        var first = Of(CommandmentKinds.Positive, 1);

        first.Title.Should().Be("To know that there is a God");
        first.References.Select(r => (r.CanonicalBook, r.CanonicalChapter, r.FirstVerse))
            .Should().Equal((2, 20, 2), (Deuteronomy, 5, 6));
        first.Note.Should().BeNull();
    }

    /// <summary>
    /// The numbers are the Sefer HaMitzvot's where the Mishneh Torah's list orders two differently:
    /// the king's Torah scroll comes before every man's.
    /// </summary>
    [Fact]
    public void TheNumbersAreTheSeferHaMitzvotsWhereTheListDiffers()
    {
        var king = Of(CommandmentKinds.Positive, 17);

        king.Title.Should().StartWith("That the King shall write a scroll of the Torah");
        king.MishnehTorahNumber.Should().Be(18);
        Of(CommandmentKinds.Negative, 329).Title.Should().Be("Not to do work on the Day of Atonement");
    }

    /// <summary>
    /// Hyamson prints Deuteronomy 23:2 in the Hebrew numbering; the words he quotes are the King
    /// James's 23:1, and the row says it was moved.
    /// </summary>
    [Fact]
    public void AVersePrintedInTheHebrewNumberingIsAtItsEnglishAddressAndSaysSo()
    {
        var eunuch = Of(CommandmentKinds.Negative, 360);

        eunuch.References.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new { CanonicalBook = Deuteronomy, CanonicalChapter = 23, FirstVerse = 1, LastVerse = 1 },
            options => options.ExcludingMissingMembers());
        eunuch.Note.Should().Contain("Hebrew numbering");
    }

    /// <summary>Runs read from the file, or none where any piece of it does not read.</summary>
    [Theory]
    [InlineData("EXO 20:2; DEU 5:6", 2)]
    [InlineData("LEV 16:3-34", 1)]
    [InlineData("LEV 16:34-3", 0)]
    [InlineData("Lev. 16:3", 0)]
    [InlineData("XYZ 1:1", 0)]
    public void AReferenceReadsOrIsRefused(string verses, int runs) =>
        (CommandmentLoader.References(verses)?.Count ?? 0).Should().Be(runs);
}

[Collection(WitnessDatabaseCollection.Name)]
public sealed class CommandmentLoadTests : IDisposable
{
    private readonly AppDbContext _db;

    public CommandmentLoadTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        Clear();
    }

    public void Dispose()
    {
        Clear();
        _db.Dispose();
    }

    [Fact]
    public async Task TheCountIsWrittenOnceAndListedPositiveFirst()
    {
        var loader = new CommandmentLoader(_db, NullLogger<CommandmentLoader>.Instance);
        var path = Path.Combine([AppContext.BaseDirectory, .. CommandmentLoader.FilePath]);

        var outcome = await loader.Load(path);
        var again = await loader.Load(path);

        (outcome.Positive, outcome.Negative).Should().Be((248, 365));
        again.AlreadyLoaded.Should().BeTrue();

        var all = await CommandmentEndpoints.All(_db, default);
        all.Should().HaveCount(613);
        all[0].Should().Match<CommandmentResponse>(c => c.Kind == CommandmentKinds.Positive && c.Number == 1);
        all[^1].Should().Match<CommandmentResponse>(c => c.Kind == CommandmentKinds.Negative && c.Number == 365);
        all[0].References[0].Book.Slug.Should().Be("exodus");
    }

    [Fact]
    public void AFileWithoutTheWholeCountIsRefused()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "kind\tnumber\tmishneh_torah\ttitle\tverses\tnote\npositive\t1\t1\tTo know\tEXO 20:2\t\n");

            var read = () => CommandmentLoader.Read(path);

            read.Should().Throw<InvalidDataException>().WithMessage("*248*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private void Clear()
    {
        _db.CommandmentReferences.ExecuteDelete();
        _db.Commandments.ExecuteDelete();
    }
}
