using Essenthos.Core.ClearBible;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The Reina-Valera's records that put a possessive determiner on another word of the verse than the one it renders lose
/// the possessive and keep their other words (PRB-1765).
/// </summary>
public class ClearBibleStrayTests
{
    private static readonly HashSet<string> Spanish = new(["su", "sus", "tu", "tus", "mi", "mis"], StringComparer.Ordinal);

    private static ClearBibleToken Target(string id, string text) => new(id, text, text is "," or ":", null);

    private static ClearBibleToken Source(string id, string text, string part, string? morph = null) =>
        new(id, text, false, null, null, part, null, morph);

    /// <summary>Exodus 21:3: «saldrá él y su mujer», where the set puts «su» on הוּא and leaves אִשְׁתּוֹ's suffix unnamed.</summary>
    [Fact]
    public void APossessiveOnAnIndependentPronounIsTakenOff()
    {
        var possessives = new ClearBibleLinks.Possessives(Spanish,
            [Target("02021003012", "saldrá"), Target("02021003013", "él"), Target("02021003014", "y"), Target("02021003015", "su"),
             Target("02021003016", "mujer")],
            [Source("o020210030091", "הוּא", "pronoun"), Source("o020210030111", "אִשְׁתּ", "noun"), Source("o020210030112", "וֹ", "suffix")]);

        ClearBibleLinks.Stray(new ClearBibleRecord(["o020210030091"], ["02021003015"]), possessives).Should().Equal("02021003015");
        ClearBibleLinks.Stray(new ClearBibleRecord(["o020210030111", "o020210030112"], ["02021003015", "02021003016"]), possessives)
            .Should().BeEmpty();
    }

    /// <summary>Genesis 4:11: «de tu hermano de tu mano», each record naming the other phrase's «tu».</summary>
    [Fact]
    public void APossessiveApartFromItsNounWhereTheSameSpellingStandsBesideItIsTakenOff()
    {
        var possessives = new ClearBibleLinks.Possessives(Spanish,
            [Target("01004011018", "de"), Target("01004011019", "tu"), Target("01004011020", "hermano"), Target("01004011021", "de"),
             Target("01004011022", "tu"), Target("01004011023", "mano"), Target("01004011024", ":")],
            [Source("o010040110131", "אָחִי", "noun"), Source("o010040110142", "יָּדֶ", "noun")]);

        ClearBibleLinks.Stray(new ClearBibleRecord(["o010040110131"], ["01004011020", "01004011022"]), possessives)
            .Should().Equal("01004011022");
        ClearBibleLinks.Stray(new ClearBibleRecord(["o010040110142"], ["01004011019", "01004011023"]), possessives)
            .Should().Equal("01004011019");
        ClearBibleLinks.Stray(new ClearBibleRecord(["o010040110142"], ["01004011022", "01004011023"]), possessives).Should().BeEmpty();
    }

    /// <summary>John 1:12: the set puts «su nombre»'s «su» on αὐτοῖς; on αὐτοῦ it is the genitive it renders.</summary>
    [Fact]
    public void AGreekPersonalPronounOutsideTheGenitiveRendersNoPossessive()
    {
        var possessives = new ClearBibleLinks.Possessives(Spanish,
            [Target("43001012023", "su"), Target("43001012024", "nombre")],
            [Source("n43001012006", "αὐτοῖς", "pron", "P-DPM"), Source("n43001012016", "αὐτοῦ", "pron", "P-GSM"),
             Source("n43001012004", "ὅσοι", "pron", "K-NPM")]);

        ClearBibleLinks.Stray(new ClearBibleRecord(["n43001012006"], ["43001012023"]), possessives).Should().Equal("43001012023");
        ClearBibleLinks.Stray(new ClearBibleRecord(["n43001012016"], ["43001012023"]), possessives).Should().BeEmpty();
        ClearBibleLinks.Stray(new ClearBibleRecord(["n43001012004"], ["43001012023"]), possessives).Should().BeEmpty();
    }
}
