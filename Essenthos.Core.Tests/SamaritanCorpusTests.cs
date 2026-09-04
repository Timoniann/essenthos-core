using Essenthos.Core.Bhsa;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Samaritan Pentateuch loaded and linked against BHSA in a real database, over Genesis.
///
/// Genesis rather than the whole Pentateuch because what is under test here is the path — the text
/// written, the round trip checked, the links written with an empty side and with two words against
/// one — and a fifth of the corpus proves that as well as all of it does at a fifth of the cost.
/// The counts over all five books are measured in <see cref="SamaritanLinkTests"/>, which needs no
/// database.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class SamaritanCorpusTests(WitnessDatabase database, ITestOutputHelper output)
    : IAsyncLifetime
{
    /// <summary>
    /// Time enough for two Hebrew texts on a machine also running the rest of the suite. The
    /// default thirty seconds is right for the classes that hold a few dozen words.
    /// </summary>
    private static readonly TimeSpan LongEnoughForTwoWitnesses = TimeSpan.FromMinutes(10);

    private const int SamaritanGenesisWords = 28_929;
    private const int MasoreticGenesisWords = 28_764;

    public Task InitializeAsync() => Clear();

    public Task DisposeAsync() => Clear();

    /// <summary>
    /// <c>TRUNCATE</c> rather than <c>DELETE</c>: deleting a text here cascades through sixty
    /// thousand words and as many links, which does not finish inside the command timeout, and a
    /// cleanup that gives up half way leaves the corpus behind for the next class.
    /// </summary>
    private async Task Clear()
    {
        await using var db = database.NewContext();
        db.Database.SetCommandTimeout(LongEnoughForTwoWitnesses);
        await db.Database.ExecuteSqlRawAsync("TRUNCATE text, strong_entry CASCADE");
    }

    private static TextSource Genesis(TextSource source) => source with { Books = [source.Books[0]] };

    [Fact]
    public async Task TheSamaritanIsLoadedAndLinkedToTheMasoretic()
    {
        await using var db = database.NewContext();
        db.Database.SetCommandTimeout(LongEnoughForTwoWitnesses);

        var loader = new CorpusLoader(db, NullLogger<CorpusLoader>.Instance);
        var samaritan = await loader.Load(Genesis(SamaritanTextSource.Read(TestResources.Samaritan)));
        var masoretic = await loader.Load(Genesis(BhsaTextSource.Build(BhsaProject.Load(TestResources.Etcbc))));

        samaritan.Words.Should().Be(SamaritanGenesisWords);
        masoretic.Words.Should().Be(MasoreticGenesisWords);

        var links = new SamaritanLinkLoader(db, NullLogger<SamaritanLinkLoader>.Instance);
        var outcome = await links.Load(SamaritanTextSource.Slug, BhsaTextSource.Slug);
        output.WriteLine(outcome.ToString());

        outcome.AlreadyLoaded.Should().BeFalse();
        outcome.Verses.Should().Be(1_533);
        outcome.Unpaired.Should().Be(0);
        outcome.ByBook.Should().ContainSingle().Which.Book.Should().Be("Genesis");

        // Every Samaritan word reaches a Masoretic one except the ones the Masoretic does not have,
        // and the same the other way. A word in no link is a word the reader is never shown beside
        // the other tradition.
        var reached = await Reached(db, SamaritanTextSource.Slug, LinkSide.From);
        var answered = await Reached(db, BhsaTextSource.Slug, LinkSide.To);

        output.WriteLine($"{reached} of {SamaritanGenesisWords} Samaritan words stand in a link, "
                         + $"{answered} of {MasoreticGenesisWords} Masoretic");
        reached.Should().Be(SamaritanGenesisWords);
        answered.Should().Be(MasoreticGenesisWords);
    }

    /// <summary>
    /// The text as a row: what it is, whose it is, and what it may be used for. A text is loaded
    /// once and read for years, so this is the moment the licence has to be right.
    /// </summary>
    [Fact]
    public async Task TheTextRowSaysWhatItIsAndWhatItMayBeUsedFor()
    {
        await using var db = database.NewContext();
        db.Database.SetCommandTimeout(LongEnoughForTwoWitnesses);

        await new CorpusLoader(db, NullLogger<CorpusLoader>.Instance)
            .Load(Genesis(SamaritanTextSource.Read(TestResources.Samaritan)));

        var text = await db.Texts.SingleAsync(t => t.Slug == SamaritanTextSource.Slug);

        text.Kind.Should().Be(TextKind.ManuscriptTradition);
        text.TextualFamily.Should().Be("Samaritan");
        text.Versification.Should().Be(Versification.Original);
        text.Licence.Should().Be("CC-BY-NC-4.0");
        text.Redistribution.Should().Be(Redistribution.NonCommercialOnly);
        text.Editors.Should().Contain("Schorch");
        text.Edition.Should().Contain("editio maior");
    }

    private static async Task<int> Reached(AppDbContext db, string slug, LinkSide side) =>
        await db.LinkWords
            .Where(lw => lw.Side == side && lw.Word!.Text!.Slug == slug)
            .Select(lw => lw.WordId)
            .Distinct()
            .CountAsync();
}
