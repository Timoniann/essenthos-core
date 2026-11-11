using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Strong;
using Essenthos.Core.Tests.Records;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Lexicon;

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

    /// <summary>
    /// Strong abbreviates the word in four entries, and Benjamin's own gentilic is one of them: H1145
    /// gives five spellings of the compound before it says <em>patron from בִּנְיָמִין (H1144)</em>.
    /// </summary>
    [Theory]
    [InlineData("H1145", "a Jeminite; (plural) בְּנֵי יְמִינִיxlit Bᵉnîy corrected to Bᵉnêy; patron from בִּנְיָמִין (H1144);", "H1144")]
    [InlineData("H440", "patron from אַלּוֹן (H438);", "H438")]
    public void AnAbbreviatedPatronymicIsStillOne(string number, string derivation, string origin)
    {
        var stated = GentilicDerivations.Read(number, derivation, out var refusal);

        refusal.Should().Be(GentilicRefusal.None);
        stated!.Value.OriginNumber.Should().Be(origin);
        stated.Value.Kind.Should().Be(GentilicKinds.Patronymic);
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

    /// <summary>
    /// Strong derives the Arodites from the Arvadites, whose name is spelled with the same letters,
    /// while his own definition makes them the descendants of Arod. Neither kind of gentilic is
    /// named after a people, so a claim whose origin is another claim's people is refused once the
    /// entries are read together.
    /// </summary>
    [Fact]
    public void APeopleIsNotDerivedFromAPeople()
    {
        var arvadites = GentilicDerivations.Read("H721", "patrial from אַרְוַד (H719);", out _)!.Value;
        var arodites = GentilicDerivations.Read("H722", "patronymic from אַרְוָדִי (H721);", out _)!.Value;

        var kept = GentilicDerivations.Together([arvadites, arodites], out var refused);

        kept.Should().ContainSingle().Which.StrongNumber.Should().Be("H721");
        refused.Should().ContainSingle().Which.StrongNumber.Should().Be("H722");
    }

    [Fact]
    public void AnEntryThatSaysNothingAboutAPeopleIsNotRead()
    {
        GentilicDerivations.Read("H430", "plural of אֱלוֹהַּ (H433);", out var refusal).Should().BeNull();
        refusal.Should().Be(GentilicRefusal.NotAGentilic);
    }
}
