using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>The SBL Greek New Testament's own lines, read without its apparatus.</summary>
public class SblgntReaderTests
{
    private static BookDraft Read(params string[] lines) => SblgntTextSource.Book(41, 2, lines);

    private static string Text(VerseDraft verse) =>
        string.Concat(verse.Words.Select(word => word.Surface + word.Trailer));

    private static string? Mark(WordDraft word) =>
        word.Morphology is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(word.Morphology)!["brackets"];

    [Fact]
    public void TheApparatusSignsAreNotLettersAndTheTitleIsTheBooksGreekName()
    {
        var book = Read(
            "ΚΑΤΑ ΜΑΡΚΟΝ",
            "Mark 1:2\t⸀Καθὼς γέγραπται ἐν ⸂τῷ Ἠσαΐᾳ τῷ προφήτῃ⸃· ⸀1Ἰδοὺ ἀποστέλλω ");

        book.NameNative.Should().Be("ΚΑΤΑ ΜΑΡΚΟΝ");
        var verse = book.Chapters.Single().Verses.Single();
        verse.Words.Select(word => word.Surface).Should().Equal("Καθὼς", "γέγραπται", "ἐν", "τῷ", "Ἠσαΐᾳ", "τῷ", "προφήτῃ", "Ἰδοὺ", "ἀποστέλλω");
        Text(verse).Should().Be("Καθὼς γέγραπται ἐν τῷ Ἠσαΐᾳ τῷ προφήτῃ· Ἰδοὺ ἀποστέλλω");
        verse.Words.Should().OnlyContain(word => word.Morphology == null);
    }

    /// <summary>
    /// A double bracket opened in one verse and closed many verses later marks every word between
    /// it, and a single bracket the words Holmes doubts.
    /// </summary>
    [Fact]
    public void BracketsMarkEveryWordTheyEncloseAcrossVerses()
    {
        var book = Read(
            "ΚΑΤΑ ΜΑΡΚΟΝ",
            "Mark 16:8\tἐφοβοῦντο ⸁γάρ. ⟦Πάντα δὲ.⟧",
            "Mark 16:9\t⟦Ἀναστὰς δὲ πρωῒ",
            "Mark 16:20\tβεβαιοῦντος ⸀σημείων.⟧ ",
            "Mark 16:99\tκαὶ ⸂[ἐν Ἐφέσῳ]⸃ τοῖς");

        var verses = book.Chapters.Single().Verses;
        verses[0].Words.Select(Mark).Should().Equal(null, null, "rejected", "rejected");
        verses[1].Words.Select(Mark).Should().OnlyContain(mark => mark == "rejected");
        verses[2].Words.Select(Mark).Should().OnlyContain(mark => mark == "rejected");
        Text(verses[2]).Should().Be("βεβαιοῦντος σημείων.");
        verses[3].Words.Select(Mark).Should().Equal(null, "doubtful", "doubtful", null);
        verses[3].Words.Select(word => word.Surface).Should().Equal("καὶ", "ἐν", "Ἐφέσῳ", "τοῖς");
    }

    [Fact]
    public void ABracketLeftOpenIsRefused()
    {
        var act = () => Read("ΚΑΤΑ ΜΑΡΚΟΝ", "Mark 16:9\t⟦Ἀναστὰς δὲ");

        act.Should().Throw<InvalidOperationException>().WithMessage("*brackets still open*");
    }

    /// <summary>
    /// A parenthesis opening before a word stands between it and the word before, and a dash on
    /// its own goes on the word it follows.
    /// </summary>
    [Fact]
    public void PunctuationBeforeAWordStaysBetweenTheWords()
    {
        var book = Read(
            "ΚΑΤΑ ΜΑΡΚΟΝ",
            "Mark 3:17\tἸακώβου (καὶ ἐπέθηκεν αὐτοῖς ὀνόματα Βοανηργές, ὅ ἐστιν Υἱοὶ Βροντῆς)· ",
            "Mark 3:18\tκαὶ Ἀνδρέαν — καὶ διʼ αὐτοῦ");

        var verses = book.Chapters.Single().Verses;
        Text(verses[0]).Should().Be("Ἰακώβου (καὶ ἐπέθηκεν αὐτοῖς ὀνόματα Βοανηργές, ὅ ἐστιν Υἱοὶ Βροντῆς)·");
        verses[0].Words[0].Trailer.Should().Be(" (");
        verses[0].Words[1].Surface.Should().Be("καὶ");
        Text(verses[1]).Should().Be("καὶ Ἀνδρέαν — καὶ διʼ αὐτοῦ");
        verses[1].Words.Select(word => word.Surface).Should().Contain("διʼ");
    }
}

/// <summary>The SBL Greek New Testament as fetched.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SblgntTests
{
    private static readonly Lazy<TextSource> Source = new(() => SblgntTextSource.Read(TestResources.Folder(".")));

    private static VerseDraft? Verse(int book, int chapter, int verse) =>
        Source.Value.Books.Single(one => one.CanonicalOrdinal == book).Chapters
            .SingleOrDefault(one => one.Number == chapter)?.Verses.SingleOrDefault(one => one.Number == verse);

    [Fact]
    public void ItIsACriticalEditionUnderPlainAttribution()
    {
        var definition = SblgntTextSource.Definition;
        definition.Validate();
        definition.Slug.Should().Be("SBLGNT");
        definition.Kind.Should().Be(TextKind.CriticalEdition);
        definition.Redistribution.Should().Be(Redistribution.PermittedWithAttribution);
        definition.Licence.Should().Be("CC-BY-4.0");
        TextCorpus.Slugs.Should().Contain("SBLGNT");
    }

    [Fact]
    public void EveryLineIsAVerseAndEveryBookIsThere()
    {
        Source.Value.Books.Should().HaveCount(27);
        Source.Value.Books.Select(book => book.CanonicalOrdinal).Should().Equal(Enumerable.Range(40, 27));
        Source.Value.Books.SelectMany(book => book.Chapters).Sum(chapter => chapter.Verses.Count).Should().Be(7939);
        Source.Value.Books.Should().OnlyContain(book => book.NameNative != null && book.NameNative.Length > 0);
        Source.Value.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses)
            .SelectMany(verse => verse.Words)
            .Should().OnlyContain(word => !word.Surface.Any(c => "⸀⸁⸂⸃⸄⸅⟦⟧[]()".Contains(c)) && word.Surface.Length > 0);
    }

    [Fact]
    public void TheEndingsOfMarkAndTheAdulteressAreMarkedAsHolmesPrintsThem()
    {
        static string? Mark(WordDraft word) =>
            word.Morphology is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(word.Morphology)!["brackets"];

        Verse(41, 16, 9)!.Words.Select(Mark).Should().OnlyContain(mark => mark == "rejected");
        Verse(41, 16, 20)!.Words.Select(Mark).Should().OnlyContain(mark => mark == "rejected");
        Verse(41, 16, 7)!.Words.Select(Mark).Should().OnlyContain(mark => mark == null);
        Verse(43, 7, 53)!.Words.Select(Mark).Should().OnlyContain(mark => mark == "rejected");
        Verse(43, 8, 11)!.Words.Select(Mark).Should().OnlyContain(mark => mark == "rejected");
        Verse(43, 8, 12)!.Words.Select(Mark).Should().OnlyContain(mark => mark == null);
    }
}
