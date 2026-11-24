using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>Read once: the Slovak, Czech and Polish texts of TSK-1997.</summary>
public sealed class WestSlavicTexts
{
    internal TextSource Slovak { get; } = EbibleTextSource.Read(TestResources.EbibleFolder("BiblicaSlovak2023"));

    internal TextSource Kralice { get; } = EbibleTextSource.Read(TestResources.EbibleFolder("Kralice1613"));

    internal TextSource UpdatedGdansk { get; } = EbibleTextSource.Read(TestResources.EbibleFolder("UpdatedGdansk2018"));

    internal TextSource CzechStudy { get; } = SwordTextSource.Read(TestResources.Folder(SwordTextSource.Texts["CzeCSP"].Folder));

    internal TextSource Gdansk { get; } = SwordTextSource.Read(TestResources.Folder(SwordTextSource.Texts["PolGdanska"].Folder));
}

/// <summary>
/// The five texts as fetched by scripts/fetch-west-slavic.ps1: whole, every verse the archive holds, and nothing of the
/// editors' apparatus read as words of the text, which is served verbatim (two of them under NoDerivatives).
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class WestSlavicTextsTests(WestSlavicTexts texts) : IClassFixture<WestSlavicTexts>
{
    private static IEnumerable<VerseDraft> Verses(TextSource source) =>
        source.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses);

    private static string Text(TextSource source, int book, int chapter, int verse) => string.Concat(
        source.Books.Single(b => b.CanonicalOrdinal == book).Chapters.Single(c => c.Number == chapter).Verses
            .Single(v => v.Number == verse).Words.Select(word => word.Surface + word.Trailer)).TrimEnd();

    [Fact]
    public void EachTextHoldsEveryVerseItsArchiveHolds()
    {
        (texts.Slovak.Books.Count, Verses(texts.Slovak).Count()).Should().Be((27, 7_955));
        (texts.Kralice.Books.Count, Verses(texts.Kralice).Count()).Should().Be((66, 31_172));
        (texts.UpdatedGdansk.Books.Count, Verses(texts.UpdatedGdansk).Count()).Should().Be((66, 31_102));
        (texts.CzechStudy.Books.Count, Verses(texts.CzechStudy).Count()).Should().Be((66, 31_169));
        (texts.Gdansk.Books.Count, Verses(texts.Gdansk).Count()).Should().Be((66, 31_073));
    }

    /// <summary>
    /// Biblica's Slovak is the New Testament alone, its section headings are the editors' and are not words, and the
    /// three places it renders two verses as one stand at the first verse with the second in the label.
    /// </summary>
    [Fact]
    public void TheSlovakIsTheNewTestamentWithoutItsHeadings()
    {
        texts.Slovak.Books.Select(book => book.CanonicalOrdinal).Should().Equal(Enumerable.Range(40, 27));
        texts.Slovak.Definition.Language.Should().Be("slk");
        texts.Slovak.Definition.Redistribution.Should().Be(Redistribution.ShareAlike);
        Text(texts.Slovak, 40, 1, 1).Should().StartWith("Toto je záznam rodokmeňa Ježiša Krista");
        texts.Slovak.Books.Single(book => book.CanonicalOrdinal == 55).Chapters.Single(c => c.Number == 4).Verses
            .Should().Contain(verse => verse.Number == 3 && verse.Label == "-4");
        texts.Slovak.Books.First().NameNative.Should().Be("Evanjelium podľa Matúša");
    }

    /// <summary>
    /// The Czech Study Translation prints its section headings inside the verse they open: "Kniha původu Ježíše Krista"
    /// over Matthew 1:1 is no word of it. Its cross references are loaded as cross references, its notes as notes, and
    /// the notes that point to tables of the printed volume not at all.
    /// </summary>
    [Fact]
    public void TheCzechStudyTranslationLeavesItsHeadingsAndKeepsItsNotes()
    {
        Text(texts.CzechStudy, 40, 1, 1).Should().Be("Rodokmen Ježíše Krista, syna Davidova, syna Abrahamova.");
        Text(texts.CzechStudy, 19, 3, 1).Should().Be("⌈Žalm Davidův,⌉ když utíkal před svým synem Abšalómem.");
        var notes = Verses(texts.CzechStudy).SelectMany(verse => verse.Notes).ToList();
        notes.Should().Contain(note => note.Kind == VerseNoteKind.CrossReference && note.Content.Replace('\u00a0', ' ') == "Ga 3,16");
        notes.Should().Contain(note => note.Kind == VerseNoteKind.Footnote && note.Content.StartsWith("ř.: kniha původu"));
        notes.Should().NotContain(note => note.Content.Contains("v tabulce na str."));
        texts.CzechStudy.Definition.Redistribution.Should().Be(Redistribution.NonCommercialOnly);
    }

    [Fact]
    public void TheHistoricalTextsAreTheEditionsNamed()
    {
        Text(texts.Kralice, 1, 1, 1).Should().Be("Na počátku stvořil Bůh nebe a zemi.");
        Text(texts.Kralice, 62, 5, 7).Should().Contain("Otec, Slovo, a Duch Svatý");
        Text(texts.Gdansk, 1, 1, 1).Should().StartWith("Na początku stworzył Bóg niebo i ziemię");
        Text(texts.UpdatedGdansk, 1, 1, 1).Should().Be("Na początku Bóg stworzył niebo i ziemię.");
        texts.UpdatedGdansk.Definition.Redistribution.Should().Be(Redistribution.NonCommercialOnly);
    }
}
