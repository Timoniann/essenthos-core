using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Strong;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// The kinship Strong's dictionary states, and everything it declines to state clearly enough to be
/// read.
///
/// The refusals are the tests worth having. A parse over nineteenth-century prose that accepts too
/// much does not fail — it publishes a wrong ancestor on a people group, which no reader can see
/// and every reader will repeat. So each clause the dictionary phrases loosely is here as its own
/// case, with the sentence that has to keep being refused.
/// </summary>
public sealed class GentilicTests
{
    [Fact]
    public void TheMoabitesDescendFromMoab()
    {
        var stated = GentilicDerivations.Read(
            "H4125",
            "feminine מוֹאָבִיָּה; or מוֹאָבִית; patronymical from מוֹאָב (H4124);",
            out var refusal);

        refusal.Should().Be(GentilicRefusal.None);
        stated!.Value.OriginNumber.Should().Be("H4124");
        stated.Value.Kind.Should().Be(GentilicKinds.Patronymic);
        stated.Value.Statement.Should().Be("patronymical from מוֹאָב (H4124)");
    }

    /// <summary>A people named after where it lives reaches a map, not a man.</summary>
    [Fact]
    public void TheCanaanitesAreNamedAfterTheLand()
    {
        var stated = GentilicDerivations.Read("H3669", "patrial from כְּנַעַן (H3667);", out _);

        stated!.Value.OriginNumber.Should().Be("H3667");
        stated.Value.Kind.Should().Be(GentilicKinds.Patrial);
    }

    /// <summary>
    /// Strong writes both words and chooses neither. The origin is stated and the kind is not, so
    /// the claim is kept and nothing is read out of it — which is what stops the resolution
    /// guessing between the man Agag and a place of the same name.
    /// </summary>
    [Fact]
    public void WhereStrongWritesBothWordsTheKindStaysUndetermined()
    {
        var stated = GentilicDerivations.Read("H91", "patrial or patronymic from אֲגַג (H90);", out _);

        stated!.Value.OriginNumber.Should().Be("H90");
        stated.Value.Kind.Should().Be(GentilicKinds.Either);
    }

    /// <summary>
    /// The clause names a number and the number is not the origin: it is the place the origin is
    /// merely <em>similar</em> to. Reading it as an origin would say the Arkites descend from
    /// Erech, which Strong is explicitly denying in the same sentence.
    /// </summary>
    [Fact]
    public void AComparisonIsNotADerivation()
    {
        GentilicDerivations
            .Read("H757", "patrial from another place (in Palestine) of similar name with אֶרֶךְ (H751);", out var refusal)
            .Should().BeNull();

        refusal.Should().Be(GentilicRefusal.NamesNoNumber);
    }

    [Theory]
    [InlineData("H5284", "patrial from a place corresponding in name (but not identical) with נַעֲמָה (H5279);")]
    [InlineData("H2741", "a patrial from (probably) a collateral form of חָרִיף (H2756);")]
    [InlineData("H512", "patrial from a name of uncertain derivation;")]
    [InlineData("H5525", "patrial from an unknown name (perhaps סֹךְ (H5520));")]
    [InlineData("H6324", "patronymically from an unused name meaning a turn;")]
    public void AnOriginDescribedInEnglishIsNotAnOrigin(string number, string derivation)
    {
        GentilicDerivations.Read(number, derivation, out var refusal).Should().BeNull();
        refusal.Should().Be(GentilicRefusal.NamesNoNumber);
    }

    /// <summary>
    /// Strong's own doubt survives into the store as silence. He hedged the Hagrites and the
    /// Meunites; publishing either as a fact would be this corpus asserting what its source would
    /// not.
    /// </summary>
    [Theory]
    [InlineData("H1905", "or (prolonged) הַגְרִיא; perhaps patronymically from הָגָר (H1904);")]
    [InlineData("H4586", "or מְעִינִי; probably patrial from מָעוֹן (H4584);")]
    public void AHedgeIsRefused(string number, string derivation)
    {
        GentilicDerivations.Read(number, derivation, out var refusal).Should().BeNull();
        refusal.Should().Be(GentilicRefusal.Hedged);
    }

    /// <summary>
    /// A hedge about the spelling is not a hedge about the ancestry. H3614 doubts how the word was
    /// transcribed and then states its origin flatly, in a separate clause.
    /// </summary>
    [Fact]
    public void AHedgeInAnotherClauseLeavesTheClaimStanding()
    {
        var stated = GentilicDerivations.Read(
            "H3614",
            "probably by erroneous transcription for כָּלֵבִי; patronymically from כָּלֵב (H3612);",
            out _);

        stated!.Value.OriginNumber.Should().Be("H3612");
    }

    /// <summary>
    /// The one entry that states two origins and prefers the second in prose. Choosing between them
    /// is reading the argument rather than reading the derivation.
    /// </summary>
    [Fact]
    public void TwoOriginsInOneEntryAreRefused()
    {
        GentilicDerivations.Read(
                "H1511",
                "(in the m patrial from גֶּזֶר (H1507); a Gezerite (collectively) or inhabitants of Gezer; " +
                "but better (as in the text) bytransposition גִּזְרִי; patrial of גְּרִזִים (H1630);",
                out var refusal)
            .Should().BeNull();

        refusal.Should().Be(GentilicRefusal.TwoCandidates);
    }

    [Fact]
    public void AnEntryThatSaysNothingAboutAPeopleIsNotRead()
    {
        GentilicDerivations.Read("H430", "plural of אֱלוֹהַּ (H433);", out var refusal).Should().BeNull();
        refusal.Should().Be(GentilicRefusal.NotAGentilic);
    }
}

/// <summary>
/// The same parse over the whole dictionary, and the resolution over the whole encyclopedia.
///
/// Both numbers are asserted rather than printed because both are the point: a parse that quietly
/// starts accepting eight more entries has changed what the corpus asserts about eight peoples'
/// ancestry, and a resolution that quietly starts reaching more pages has changed which man a
/// reader is sent to.
/// </summary>
public sealed class StatedGentilicCoverageTests : IClassFixture<BibleDataCorpus>
{
    private readonly BibleDataCorpus _corpus;
    private readonly ITestOutputHelper _output;

    private static readonly IReadOnlyList<StrongParsedEntry> Lexicon = Read();

    public StatedGentilicCoverageTests(BibleDataCorpus corpus, ITestOutputHelper output)
    {
        _corpus = corpus;
        _output = output;
    }

    private static IReadOnlyList<StrongParsedEntry> Read()
    {
        var parser = new StrongXmlParser();
        return
        [
            .. parser.ParseHebrew(File.ReadAllText(TestResources.Path("Strong", "StrongHebrew.xml"))),
            .. parser.ParseGreek(File.ReadAllText(TestResources.Path("Strong", "StrongGreek.xml"))),
        ];
    }

    private static List<StatedGentilic> Stated() =>
    [
        .. Lexicon
            .Select(entry => GentilicDerivations.Read(entry.StrongNumber, entry.Derivation, out _))
            .Where(claim => claim is not null)
            .Select(claim => claim!.Value),
    ];

    [Fact]
    public void WhatTheDictionaryStatesAndWhatItWithholds()
    {
        var claiming = Lexicon.Where(entry => GentilicDerivations.Claims(entry.Derivation)).ToList();
        var refusals = new Dictionary<GentilicRefusal, int>();
        var stated = 0;

        foreach (var entry in claiming)
        {
            if (GentilicDerivations.Read(entry.StrongNumber, entry.Derivation, out var refusal) is not null)
            {
                stated++;
                continue;
            }

            refusals[refusal] = refusals.GetValueOrDefault(refusal) + 1;
        }

        _output.WriteLine($"{claiming.Count} entries name a gentilic, {stated} state an origin");
        foreach (var (refusal, count) in refusals.OrderByDescending(r => r.Value))
        {
            _output.WriteLine($"  refused {count} — {refusal}");
        }

        claiming.Should().HaveCount(192);
        stated.Should().Be(160);
        refusals[GentilicRefusal.NamesNoNumber].Should().Be(29);
        refusals[GentilicRefusal.Hedged].Should().Be(2);
        refusals[GentilicRefusal.TwoCandidates].Should().Be(1);
    }

    /// <summary>
    /// Strong catalogued the Greek from the New Testament, where a gentilic is a word like any
    /// other and he never marks one. So this whole layer is the Hebrew's, and a Greek reader gets
    /// nothing from it — which is worth knowing before anyone builds on it.
    /// </summary>
    [Fact]
    public void NoGreekEntryStatesOne()
    {
        Lexicon.Where(entry => entry.StrongNumber.StartsWith('G'))
            .Should().NotContain(entry => GentilicDerivations.Claims(entry.Derivation));
    }

    /// <summary>
    /// The measurement the whole shape rests on: the near end of one of these claims is a people,
    /// and the encyclopedia holds no peoples. Where a gentilic number does match a name, the name
    /// belongs to a man described by his people — Goliath is <em>The Philistine</em> — so an edge
    /// drawn between two entities here would say that Goliath descends from Philistia.
    /// </summary>
    [Fact]
    public void AGentilicIsNotAnEntity()
    {
        var named = Named();
        var matched = Stated()
            .Where(claim => named.Any(name => name.Number == claim.StrongNumber))
            .ToList();

        _output.WriteLine($"{matched.Count} of the stated gentilics match a name in the encyclopedia");

        matched.Should().HaveCountLessThan(20);
        Slugs(named, "H6430").Should().Contain("goliath");
    }

    [Fact]
    public void TheOriginsThatReachExactlyOnePage()
    {
        var named = Named();
        var origins = StrongGentilicLoader.Resolve(named);
        var stated = Stated();

        var reached = stated.Count(claim => origins.ContainsKey((claim.OriginNumber, claim.Kind)));
        _output.WriteLine($"{reached} of {stated.Count} stated origins reach exactly one entity");

        reached.Should().Be(51);

        // Moab is two entities under one number, the man and the land, and the word Strong chose is
        // what tells them apart: a Moabite is patronymically from Moab, so the page is the man's.
        var moab = origins[("H4124", GentilicKinds.Patronymic)];
        named.Single(name => name.EntityId == moab && name.Number == "H4124").Kind
            .Should().Be(EntityKind.Person);
        origins.Should().ContainKey(("H3667", GentilicKinds.Patrial));
    }

    /// <summary>
    /// Every name in the encyclopedia that carries a single Hebrew number. A name whose column
    /// holds a list is a title — <em>King of Judah</em>, two words and two numbers — and a people
    /// is not named after a title.
    /// </summary>
    private List<NamedEntity> Named() =>
    [
        .. _corpus.Names
            .Where(name => name.HebrewStrongNumber is { Length: > 0 } number && !number.Contains(','))
            .Select(name => new NamedEntity(
                name.HebrewStrongNumber!,
                name.EntityId,
                _corpus.Entities.Values.Single(entity => entity.Id == name.EntityId).Kind))
            .Distinct(),
    ];

    private IEnumerable<string> Slugs(IEnumerable<NamedEntity> named, string number) =>
        named.Where(name => name.Number == number)
            .Select(name => _corpus.Entities.Values.Single(entity => entity.Id == name.EntityId).Slug);
}
