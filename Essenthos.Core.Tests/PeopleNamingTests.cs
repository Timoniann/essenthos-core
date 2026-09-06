using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What a people is called, and which of a model's own sentences may be read as naming one.
///
/// Both are places where a heuristic decides something a reader will take for scholarship, so both
/// are pinned here. A naming rule that starts producing <em>Chittites</em> where the King James
/// says Hittite makes a page nobody finds; a classifier that starts reading <em>the kingdom of
/// Judah</em> as the tribe puts a polity's occurrences on a people, which is the exact failure this
/// layer was built to stop.
/// </summary>
public sealed class PeopleNamingTests
{
    /// <summary>
    /// Strong's own English is a nineteenth-century transliteration and the King James renderings
    /// beside it are the spellings a reader knows. Taking his gives <em>Chittites</em>; taking the
    /// King James's gives the word the page has to be under.
    /// </summary>
    [Theory]
    [InlineData("Hittite, Hittities.", "a Chittite, or descendant of Cheth", "Hittites")]
    [InlineData("Canaanite, merchant, trafficker.", "a Kenaanite or inhabitant of Kenaan", "Canaanites")]
    [InlineData("Philistine.", "a Pelishtite or inhabitant of Pelesheth", "Philistines")]
    [InlineData("Jebusite(-s).", "a Jebusite or inhabitant of Jebus", "Jebusites")]
    public void TheNameIsTheKingJamesRenderingRatherThanStrongsTransliteration(
        string kjv, string definition, string expected) =>
        GentilicNaming.Of(kjv, definition).Should().Be(expected);

    /// <summary>
    /// A King James entry that is a phrase names nobody once it is trimmed — <em>children of
    /// Reuben</em> minus the phrase is Reuben, who is a man. So phrases are skipped and the next
    /// entry, or Strong's own word, is taken instead.
    /// </summary>
    [Theory]
    [InlineData("children of Reuben, Reubenites.", "a Reubenite or descendant of Reuben", "Reubenites")]
    [InlineData("(woman) of Moab, Moabite(-ish, -ss).", "a Moabite or Moabitess", "Moabites")]
    [InlineData("of the house of Caleb.", "a Calebite or descendant of Caleb", "Calebites")]
    public void APhraseIsNotAName(string kjv, string definition, string expected) =>
        GentilicNaming.Of(kjv, definition).Should().Be(expected);

    /// <summary>
    /// The forms that are already Hebrew plurals keep the shape the King James prints them in.
    /// <em>Anakims</em> is a plural of a plural and nobody writes it.
    /// </summary>
    [Theory]
    [InlineData("Anakim.", "an Anakite or descendant of Anak", "Anakim")]
    [InlineData("Pathrusim.", "a Pathrusite, or inhabitant of Pathros", "Pathrusim")]
    public void AHebrewPluralIsLeftAsItStands(string kjv, string definition, string expected) =>
        GentilicNaming.Of(kjv, definition).Should().Be(expected);

    /// <summary>
    /// A plural form is preferred over a singular one where the entry offers both, because the
    /// record is titled in the plural and <em>Ziphims</em> would be the alternative.
    /// </summary>
    [Fact]
    public void ThePluralFormIsPreferred() =>
        GentilicNaming.Of("Ziphim, Ziphite.", "a Ziphite or inhabitant of Ziph")
            .Should().Be("Ziphites");

    /// <summary>
    /// Where the King James list is a Hebrew form or a phrase throughout, Strong's own word stands
    /// — and where neither yields anything, nothing is invented and the record is not written.
    /// </summary>
    [Fact]
    public void StrongsOwnWordIsTheFallback() =>
        GentilicNaming.Of("Japhleti.", "a Japhletite or descendant of Japhlet")
            .Should().Be("Japhletites");

    [Fact]
    public void NothingIsInventedWhereNeitherSourceSaysAnything() =>
        GentilicNaming.Of(null, null).Should().BeNull();

    /// <summary>
    /// The sentences a model wrote for the referents nobody held. A people is annotated from one;
    /// a kingdom or a territory is not, because the encyclopedia has no kind for those either and
    /// folding them in here would be the page absorbing every occurrence of the word.
    /// </summary>
    [Theory]
    [InlineData("tribe of Benjamin", true)]
    [InlineData("The tribe of Judah, Caleb's tribal affiliation", true)]
    [InlineData("the people of Judah (remnant)", true)]
    [InlineData("Census total 'families of Judah'", true)]
    [InlineData("the kingdom of Judah", false)]
    [InlineData("the territory of Judah", false)]
    [InlineData("territory of Judah (Bethlehem-judah)", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void OnlyASentenceAboutAPeopleIsReadAsOne(string? describes, bool expected) =>
        CollectiveReadings.NamesAPeople(describes).Should().Be(expected);

    /// <summary>
    /// A sentence naming both decides nothing, and there are eighty-three of them. Refusing them is
    /// the point: which of the two a verse means is a question about the verse, and this pass does
    /// not read verses.
    /// </summary>
    [Theory]
    [InlineData("the kingdom/tribe of Judah")]
    [InlineData("the tribe/territory of Judah")]
    [InlineData("kingdom/people of Judah")]
    public void ASentenceNamingBothIsRefused(string describes) =>
        CollectiveReadings.NamesAPeople(describes).Should().BeFalse();

    /// <summary>
    /// Two peoples the King James spells alike cannot share an address, so the second takes the
    /// Strong number of its own gentilic — which is the only thing that tells the Sabeans of Seba
    /// from the Sabeans of Sheba.
    /// </summary>
    [Fact]
    public void APeopleWhoseNameIsTakenGetsItsNumber()
    {
        PeopleFiles.Slug("Sabeans", "H7615", _ => false).Should().Be("sabeans");
        PeopleFiles.Slug("Sabeans", "H7615", taken => taken == "sabeans").Should().Be("sabeans-h7615");
    }
}

/// <summary>
/// The peoples file itself, read as a reader would meet it. A tribe whose ancestor slug drifted, a
/// gentilic number claimed by two tribes at once, or a ruling naming a people that is not in the
/// file are all silent failures on a live database — the record is written without its link, or the
/// word is left unannotated, and nothing says so.
/// </summary>
public sealed class PeopleFileTests
{
    private static readonly PeopleFile File = PeopleFiles.Read();

    [Fact]
    public void EveryTribeIsNamedOnceAndSaysWhatEstablishedIt()
    {
        File.Tribes.Should().NotBeEmpty();
        File.Tribes.Select(t => t.Slug).Should().OnlyHaveUniqueItems();
        File.Tribes.Select(t => t.Name).Should().OnlyHaveUniqueItems();
        File.Tribes.Should().OnlyContain(t => t.Why.Length > 0);
        File.Tribes.Should().OnlyContain(t => t.Origin.Length > 0);
        File.Tribes.Should().OnlyContain(t => t.CollectiveNumber.StartsWith('H'));
    }

    /// <summary>
    /// A collective number reaching two peoples would annotate one word to both of them, and a
    /// gentilic claimed twice would make the resolution ambiguous where its whole warrant is that
    /// it is not.
    /// </summary>
    [Fact]
    public void NoNumberNamesTwoPeoples()
    {
        File.Tribes.Select(t => t.CollectiveNumber).Should().OnlyHaveUniqueItems();
        File.Tribes.SelectMany(t => t.GentilicNumbers ?? []).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void EveryRulingNamesAPeopleTheFileHolds()
    {
        var slugs = File.Tribes.Select(t => t.Slug).ToHashSet(StringComparer.Ordinal);

        File.Rulings.Should().NotBeEmpty();
        File.Rulings.Should().OnlyContain(r => slugs.Contains(r.People));
        File.Rulings.Should().OnlyContain(r => r.Why.Length > 0);
        File.Rulings.Select(r => r.WordId).Should().OnlyHaveUniqueItems();
    }

    /// <summary>
    /// A correction to a dictionary's own English is a claim, and a claim with no reason beside it
    /// is indistinguishable from a silent rewrite of somebody else's work.
    /// </summary>
    [Fact]
    public void EveryCorrectedNameSaysWhyItWasCorrected()
    {
        File.Namings.Select(n => n.Number).Should().OnlyHaveUniqueItems();
        File.Namings.Should().OnlyContain(n => n.Why.Length > 0);
        File.Namings.Should().OnlyContain(n => n.Name.Length > 0);
    }
}
