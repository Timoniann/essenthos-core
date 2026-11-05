using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.XmlBible;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>
/// What the Synodal and Ohienko's Ukrainian say about their own numbering, which their publisher
/// overwrote and their text still records. Psalm 119 is the case a reader meets: bible4u numbers it
/// 119 because that is how the King James numbers it, every printed Synodal numbers it 118, and the
/// file opens the verse with "(118-1)" to say so.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class StatedNumberTests(ITestOutputHelper output)
{
    /// <summary>
    /// The Synodal's Psalm 119:1 as bible4u writes it, which is the verse the whole of this is
    /// about.
    /// </summary>
    private const string Psalm1191 = "(118-1) Блаженны непорочные в пути, ходящие в законе Господнем.";

    [Fact]
    public void TheAddressTheEditionPrintsIsKept()
    {
        VerseWords.StatedAddresses(Psalm1191).Should().Equal(new VerseAddress(118, 1));
    }

    /// <summary>
    /// And it is still not a word of the psalm. Keeping the address and stripping it from the text
    /// are the same decision read two ways, and a change to either must not move the other.
    /// </summary>
    [Fact]
    public void TheAddressIsStillNoPartOfTheVerse()
    {
        VerseWords.StripMarkup(Psalm1191)
            .Should().Be("Блаженны непорочные в пути, ходящие в законе Господнем.");
    }

    /// <summary>
    /// Job 2:9 and Job 9:9 write "(1)" where the edition footnotes a variant reading. It is not an
    /// address — it names no chapter and no verse — and storing it as one would have the Synodal
    /// claim its Job 2:9 is printed as chapter one.
    /// </summary>
    [Fact]
    public void AFootnoteMarkerIsNotAnAddress()
    {
        VerseWords.StatedAddresses("похули Бога и умри. (1)").Should().BeEmpty();
        VerseWords.StripMarkup("похули Бога и умри. (1)").Should().Be("похули Бога и умри.");
    }

    /// <summary>
    /// Psalm 12:1 of the Synodal is two of the edition's verses: it counts the superscription as
    /// 11:1 and the body as 11:2. Both are kept and in the order printed, because reporting the
    /// first alone says the edition divides the psalm the way this corpus does.
    /// </summary>
    [Fact]
    public void AVerseHoldingTwoOfTheEditionsKeepsBoth()
    {
        VerseWords.StatedAddresses("(11-1) ^^Начальнику хора.^^ (11-2) Спаси, Господи.")
            .Should().Equal(new VerseAddress(11, 1), new VerseAddress(11, 2));
    }

    /// <summary>
    /// An address standing after a superscription addresses the words that follow it, which is a
    /// third of the Ukrainian's. Requiring it at the head of the verse would drop exactly those.
    /// </summary>
    [Fact]
    public void AnAddressAfterTheSuperscriptionIsStillAnAddress()
    {
        VerseWords.StatedAddresses("Для дириґетна хору. Псалом Давидів. (13-2) Доки, Господи?")
            .Should().Equal(new VerseAddress(13, 2));
    }

    [Fact]
    public void AVerseTheEditionSaysNothingAboutStatesNothing()
    {
        VerseWords.StatedAddresses("В начале сотворил Бог небо и землю.").Should().BeEmpty();
    }

    /// <summary>
    /// Counted over the published files rather than assumed, because what a marker means was
    /// decided from these counts: the King James carries none at all, every marker of the Ukrainian
    /// is an address, and the Synodal's only two exceptions are the Job footnotes above.
    ///
    /// Where they are is the point. 2,414 of the Synodal's 2,676 addressed verses are in the
    /// Psalms, which is where the Greek and the Hebrew traditions number differently and where a
    /// Russian reader is most likely to be looking at a number they do not recognise.
    /// </summary>
    [Theory]
    [InlineData("KJV", 0, 0, 0)]
    [InlineData("RUSV", 2676, 2735, 2414)]
    [InlineData("UKR", 1928, 1931, 1013)]
    public void TheEditionsOwnNumberingIsWhereTheTwoTraditionsDisagree(
        string translation,
        int expectedVerses,
        int expectedAddresses,
        int expectedInThePsalms)
    {
        var bible = new XmlBibleParser().Parse(File.ReadAllText(TestResources.Bible4u(translation)));

        var verses = 0;
        var addresses = 0;
        var psalms = 0;
        foreach (var book in bible.Books)
        foreach (var chapter in book.Chapters)
        foreach (var verse in chapter.Verses)
        {
            var stated = VerseWords.StatedAddresses(verse.Text);
            if (stated.Count == 0)
            {
                continue;
            }

            verses++;
            addresses += stated.Count;
            if (book.BNumber == Psalms)
            {
                psalms++;
            }

            stated.Should().OnlyContain(
                address => address.Chapter > 0 && address.Number > 0,
                $"{translation} {book.BsName} {chapter.CNumber}:{verse.VNumber} states an address");
        }

        output.WriteLine($"{translation}: {addresses} addresses over {verses} verses, {psalms} in the Psalms");
        verses.Should().Be(expectedVerses);
        addresses.Should().Be(expectedAddresses);
        psalms.Should().Be(expectedInThePsalms);
    }

    /// <summary>
    /// The case the reader reported, read straight out of the file: the Synodal's Psalm 119 is
    /// numbered 118 verse for verse over all 176 of them, and its Psalm 147 is two psalms — 146
    /// through verse 11 and 147 from verse 12 — which is the Septuagint numbering the Slavic
    /// tradition kept and the reason its psalms end level with the Hebrew again.
    /// </summary>
    [Fact]
    public void TheSynodalNumbersThePsalmsAsTheSlavicTraditionDoes()
    {
        var bible = new XmlBibleParser().Parse(File.ReadAllText(TestResources.Bible4u("RUSV")));
        var psalms = bible.Books.Single(b => b.BNumber == Psalms);

        var acrostic = psalms.Chapters.Single(c => c.CNumber == 119);
        acrostic.Verses.Should().HaveCount(176);
        acrostic.Verses.Select(v => VerseWords.StatedAddresses(v.Text).Single())
            .Should().OnlyContain(address => address.Chapter == 118);

        var split = psalms.Chapters.Single(c => c.CNumber == 147);
        VerseWords.StatedAddresses(split.Verses.First().Text).Should().Equal(new VerseAddress(146, 1));
        VerseWords.StatedAddresses(split.Verses.Last().Text).Should().Equal(new VerseAddress(147, 9));
    }

    private const int Psalms = 19;
}
