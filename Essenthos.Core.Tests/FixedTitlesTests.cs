using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which words a title the text fixes to one bearer names, and which words of the same number it
/// does not: the article, the number, the words after it and the book each decide, one case each.
/// </summary>
public sealed class FixedTitlesTests
{
    private const int Matthew = 40;

    private const int John = 43;

    private const int Revelation = 66;

    private const int Ezekiel = 26;

    private const int Daniel = 27;

    private static readonly FixedTitleMorphology TheGenitive = new("det", "genitive", "singular", null, "T-GSM");

    private static FixedTitleWord Greek(
        string number, string form, FixedTitleMorphology? previous, int book = Matthew, params string[] following) =>
        new(1, NestleTextSource.Slug, book, number,
            new FixedTitleMorphology("adj", form[2] switch
            {
                'N' => "nominative",
                'G' => "genitive",
                'D' => "dative",
                _ => "accusative",
            }, null, null, form),
            previous, following);

    private static FixedTitleWord Hebrew(
        string number, string? previousPos, int book, string state = "a", params string[] following) =>
        new(1, BhsaTextSource.Slug, book, number, new FixedTitleMorphology("subs", null, "sg", state, null),
            previousPos is null ? null : new FixedTitleMorphology(previousPos, null, null, null, null), following);

    private static string? SlugOf(FixedTitleWord word) => FixedTitles.Of(word)?.Slug;

    /// <summary>τοῦ διαβόλου, Matthew 4:1.</summary>
    [Fact]
    public void TheDevilWithTheArticleIsSatan() =>
        SlugOf(Greek("G1228", "A-GSM", TheGenitive)).Should().Be("satan");

    /// <summary>εἷς διάβολός ἐστιν, John 6:70, of Judas.</summary>
    [Fact]
    public void ADevilWithoutTheArticleIsNobody() =>
        FixedTitles.Of(Greek("G1228", "A-NSM", new FixedTitleMorphology("adj", "nominative", null, null, "A-NSM"), John))
            .Should().BeNull();

    /// <summary>μὴ διαβόλους, 1 Timothy 3:11: slanderers.</summary>
    [Fact]
    public void SlanderersAreNobody() =>
        FixedTitles.Of(Greek("G1228", "A-APF", new FixedTitleMorphology("ptcl", null, null, null, "PRT-N")))
            .Should().BeNull();

    /// <summary>An article in another case belongs to another word.</summary>
    [Fact]
    public void AnArticleInAnotherCaseIsNotTheDevils() =>
        FixedTitles.Of(Greek("G1228", "A-NSM", TheGenitive)).Should().BeNull();

    /// <summary>ὁ καλούμενος Διάβολος καὶ ὁ Σατανᾶς, Revelation 12:9.</summary>
    [Fact]
    public void TheDevilCalledSatanInTheSameBreathIsSatan() =>
        SlugOf(Greek("G1228", "A-NSM", new FixedTitleMorphology("verb", "nominative", "singular", null, "V-PPP-NSM"),
            Revelation, "G2532", "G3588", "G4567")).Should().Be("satan");

    /// <summary>ὁ δράκων in Revelation, and ὡς δράκων, a likeness, in 13:11.</summary>
    [Fact]
    public void TheDragonOfRevelationIsSatanAndALikenessIsNot()
    {
        var the = new FixedTitleMorphology("det", "nominative", "singular", null, "T-NSM");
        SlugOf(Greek("G1404", "N-NSM", the, Revelation)).Should().Be("satan");
        FixedTitles.Of(Greek("G1404", "N-NSM", new FixedTitleMorphology("adv", null, null, null, "ADV"), Revelation))
            .Should().BeNull();
    }

    /// <summary>הַשָּׂטָן in Job 1:6, and שָׂטָן without the article in Numbers 22:22.</summary>
    [Fact]
    public void TheAdversaryWithTheArticleIsSatanAndAnAdversaryIsNot()
    {
        var job = FixedTitles.Of(Hebrew("H7854", "art", 18));
        job.Should().NotBeNull();
        job!.Slug.Should().Be("satan");
        job.Note.Should().Contain("title");
        FixedTitles.Of(Hebrew("H7854", "prep", 4)).Should().BeNull();
    }

    /// <summary>
    /// Χριστός is Jesus where the text says so, and nobody's where the verse asks or denies: John
    /// 1:20, John 9:22 and both of the two in Acts 17:3 (the owner's ruling of 2026-09-30), and Jesus in
    /// Revelation 11:15 and 12:10.
    /// </summary>
    [Fact]
    public void TheChristIsJesusExceptWhereTheVerseLeavesItOpen()
    {
        const int Acts = 44;
        FixedTitleWord Christ(int book, int chapter, int verse, int nth = 1) =>
            Greek("G5547", "N-NSM", null, book) with { Chapter = chapter, Verse = verse, Nth = nth };

        SlugOf(Christ(Matthew, 1, 16)).Should().Be("jesus");
        SlugOf(Christ(Matthew, 16, 16)).Should().Be("jesus");
        FixedTitles.Of(Christ(John, 1, 20)).Should().BeNull();
        FixedTitles.Of(Christ(Matthew, 24, 5)).Should().BeNull();
        FixedTitles.Of(Christ(Acts, 17, 3, 1)).Should().BeNull();
        FixedTitles.Of(Christ(Acts, 17, 3, 2)).Should().BeNull();
        FixedTitles.Of(Christ(John, 9, 22)).Should().BeNull();
        SlugOf(Christ(Revelation, 11, 15)).Should().Be("jesus");
        SlugOf(Christ(Revelation, 12, 10)).Should().Be("jesus");
    }

    /// <summary>ὁ υἱὸς τοῦ ἀνθρώπου, and υἱὸς ἀνθρώπου without the article, John 5:27.</summary>
    [Fact]
    public void TheSonOfManIsJesusAndASonOfManIsNot()
    {
        var the = new FixedTitleMorphology("det", "nominative", "singular", null, "T-NSM");
        var son = new FixedTitleMorphology("noun", "nominative", "singular", null, "N-NSM");
        SlugOf(new FixedTitleWord(1, NestleTextSource.Slug, Matthew, "G5207", son, the, ["G3588", "G444", "G2192"]))
            .Should().Be("jesus");
        FixedTitles.Of(new FixedTitleWord(1, NestleTextSource.Slug, John, "G5207", son, null, ["G444", "G1510"]))
            .Should().BeNull();
    }

    /// <summary>בֶּן־אָדָם in Ezekiel is the prophet; in Daniel 8:17 it addresses Daniel.</summary>
    [Fact]
    public void SonOfManInEzekielIsEzekielAndNowhereElse()
    {
        SlugOf(Hebrew("H1121", null, Ezekiel, "c", "H120")).Should().Be("ezekiel");
        FixedTitles.Of(Hebrew("H1121", null, Daniel, "c", "H120")).Should().BeNull();
        FixedTitles.Of(Hebrew("H1121", null, Ezekiel, "c", "H1732")).Should().BeNull();
    }

    /// <summary>Χριστός anywhere in the New Testament.</summary>
    [Fact]
    public void ChristIsJesus() =>
        SlugOf(Greek("G5547", "N-GSM", null, John)).Should().Be("jesus");
}
