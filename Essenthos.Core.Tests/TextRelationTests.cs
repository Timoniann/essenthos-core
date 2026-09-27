using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>The list of relations between texts as it is committed.</summary>
public sealed class TextRelationFileTests
{
    private static IReadOnlyList<TextRelationEntry> All => TextRelationLoader.All;

    [Fact]
    public void EveryClaimNamesTwoTextsOfTheCorpusAndASource()
    {
        All.Should().NotBeEmpty();
        All.Should().OnlyContain(r =>
            TextCorpus.Slugs.Contains(r.From) && TextCorpus.Slugs.Contains(r.To) &&
            !string.IsNullOrWhiteSpace(r.Source));
    }

    /// <summary>The claim the table was designed around: two sources, one for each half of the canon.</summary>
    [Fact]
    public void TheKingJamesRestsOnTheMasoreticAndOnTheReceivedText()
    {
        All.Should().ContainSingle(r => r.From == "KJV" && r.To == "BHSA")
            .Which.Should().Match<TextRelationEntry>(r => r.Relation == "same-family-as" && r.Scope == "1-39");
        All.Should().ContainSingle(r => r.From == "KJV" && r.To == "TR1894")
            .Which.Should().Match<TextRelationEntry>(r => r.Relation == "translated-from" && r.Scope == "40-66");
    }

    [Theory]
    [InlineData("1-39", 1)]
    [InlineData("67-79,80", 2)]
    [InlineData("40", 1)]
    [InlineData("39-1", 0)]
    [InlineData("1-93", 0)]
    [InlineData("OT", 0)]
    [InlineData("", 0)]
    public void AScopeReadsAsCanonicalBookRangesOrIsRefused(string scope, int ranges) =>
        (TextRelationLoader.Ranges(scope)?.Count ?? 0).Should().Be(ranges);

    [Fact]
    public void AClaimWithoutASourceIsRefused()
    {
        var validate = () => TextRelationLoader.Validate(
            [new TextRelationEntry("KJV", "BHSA", "translated-from", null, null, " ")]);

        validate.Should().Throw<InvalidDataException>().WithMessage("*no source*");
    }

    [Fact]
    public void AnUnknownKindOfRelationIsRefused()
    {
        var validate = () => TextRelationLoader.Validate(
            [new TextRelationEntry("KJV", "BHSA", "descended-from", null, null, "somebody")]);

        validate.Should().Throw<Exception>();
    }
}

[Collection(WitnessDatabaseCollection.Name)]
public sealed class TextRelationLoadTests : IAsyncLifetime
{
    private const string Prefix = "RELATION-";
    private static readonly string English = Prefix + "KJV";
    private static readonly string Hebrew = Prefix + "BHSA";
    private static readonly string Greek = Prefix + "TR";
    private static readonly string American = Prefix + "ASV";

    private readonly AppDbContext _db;

    public TextRelationLoadTests(WitnessDatabase database) => _db = database.NewContext();

    public async Task InitializeAsync()
    {
        await Clear();
        Corpus.Add(_db, English, TextKind.Translation, "eng", (1, 1, ["beginning"]));
        Corpus.Add(_db, Hebrew, TextKind.ManuscriptTradition, "hbo", (1, 1, ["רֵאשִׁית"]));
        Corpus.Add(_db, Greek, TextKind.CriticalEdition, "grc", (1, 1, ["ἀρχῇ"]));
        Corpus.Add(_db, American, TextKind.Translation, "eng", (1, 1, ["beginning"]));
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await Clear();
        await _db.DisposeAsync();
    }

    private static readonly TextRelationEntry[] Listed =
    [
        new(English, Hebrew, "same-family-as", "1-39", "the Masoretic", "Tov"),
        new(English, Greek, "translated-from", "40-66", null, "Scrivener"),
        new(American, English, "revised-from", null, null, "its title page"),
    ];

    [Fact]
    public async Task TheListIsWrittenOnceAndASecondRunChangesNothing()
    {
        var loader = new TextRelationLoader(_db, NullLogger<TextRelationLoader>.Instance);

        var first = await loader.Load(Listed);
        var again = await loader.Load(Listed);

        (first.Written, first.Removed).Should().Be((3, 0));
        (again.Written, again.Removed, again.Kept).Should().Be((0, 0, 3));
        (await _db.TextRelations.CountAsync()).Should().Be(3);
    }

    [Fact]
    public async Task AClaimNoLongerListedIsRemovedAndOneNamingAnAbsentTextIsPassedOver()
    {
        var loader = new TextRelationLoader(_db, NullLogger<TextRelationLoader>.Instance);
        await loader.Load(Listed);

        var outcome = await loader.Load(
            [Listed[0], Listed[1], new(American, Prefix + "ABSENT", "revised-from", null, null, "nobody")]);

        (outcome.Removed, outcome.Skipped).Should().Be((1, 1));
        (await _db.TextRelations.AnyAsync(r => r.FromText!.Slug == American)).Should().BeFalse();
    }

    /// <summary>
    /// A text's page answers from both ends: the King James says what it rests on, and the Hebrew
    /// says which texts rest on it.
    /// </summary>
    [Fact]
    public async Task ATextIsServedItsOwnClaimsFirstAndTheClaimsOthersMakeOfIt()
    {
        await new TextRelationLoader(_db, NullLogger<TextRelationLoader>.Instance).Load(Listed);
        var english = await _db.Texts.SingleAsync(t => t.Slug == English);
        var hebrew = await _db.Texts.SingleAsync(t => t.Slug == Hebrew);

        var ofEnglish = await TextRelations.Of(_db, english.Id, default);
        var ofHebrew = await TextRelations.Of(_db, hebrew.Id, default);

        ofEnglish.Select(r => (r.Relation, r.Text, r.Incoming, r.Scope)).Should().Equal(
            ("same-family-as", Hebrew, false, "1-39"),
            ("translated-from", Greek, false, "40-66"),
            ("revised-from", American, true, null));
        ofEnglish[0].Should().Match<TextRelationResponse>(r => r.Note == "the Masoretic" && r.Source == "Tov");
        ofHebrew.Should().ContainSingle()
            .Which.Should().Be(new TextRelationResponse("same-family-as", English, true, "1-39", "the Masoretic", "Tov"));
    }

    private async Task Clear()
    {
        await _db.TextRelations.ExecuteDeleteAsync();
        await _db.Texts.Where(t => t.Slug.StartsWith(Prefix)).ExecuteDeleteAsync();
    }
}
