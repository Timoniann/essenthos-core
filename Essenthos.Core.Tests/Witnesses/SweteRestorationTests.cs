using Essenthos.Core.Conversion;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Swete;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Witnesses;

/// <summary>The words Swete printed and the transcription lost, put back where the witnesses agree.</summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteRestorationTests(Swete swete) : IClassFixture<Swete>
{
    private const int Genesis = 1;

    [Theory]
    [InlineData(9, 28, "Ἔζησεν δὲ Νῶε μετὰ τὸν κατακλυσμὸν τριακόσια πεντήκοντα ἔτη.")]
    [InlineData(11, 22, "Καὶ ἔζησεν Σερούχ ἑκατὸν τριάκοντα ἴτη, καὶ ἐγέννησεν τὸν Ναχώρ.")]
    [InlineData(11, 25, "καὶ ἔζησεν Ναχὼρ μετὰ τὸ γεννῆσαι αὐτὸν τὸν Θαρά ἴτη ἑκατὸν εἴκοσι ἐννέα, καὶ ἐγέννησεν υἱοὺς καὶ θυγατέρας, καὶ ἀπέθανεν.")]
    [InlineData(12, 4, "καὶ ἐπορεύθη Ἀβρὰμ καθάπερ ἐλάλησεν αὐτῷ κύριος, καὶ ᾤχετο μετ’ αὐτοῦ Λώτ· Ἀβρὰμ δὲ ἦν ἐτῶν ἑβδομήκοντα πέντε ὅτε ἐξῆλθεν ἐκ Χαρράν.")]
    public void TheVerseReadsAsSwetePrintedIt(int chapter, int verse, string printed) =>
        Swete.Text(swete.Verse(Genesis, chapter, verse)).Should().Be(printed);

    [Fact]
    public void ARestorationThatNoLongerFindsItsWordsStopsTheRead()
    {
        var rows = ShippedCorrections.Swete.Of(TextCorrection.Tokens).Where(row => row.Own == "1.9.28").ToList();
        var act = () => EditionRepairs.Apply("01.Genesis", ["1.9.28 τριακόσια", "1.9.28 πεντήκοντα"], rows);

        act.Should().Throw<InvalidOperationException>().WithMessage("*1.9.28*");
    }

    [Fact]
    public void EveryOtherLinePassesThroughUnchanged()
    {
        string[] lines = ["40.1.1 Ὅρασις", "40.1.1 Ἀβδειού.", "40.1.2 ἰδοὺ"];

        EditionRepairs.Apply("40.Abdias", 40, lines, ShippedCorrections.Swete).Should().Equal(lines);
    }

    /// <summary>
    /// Brenton for Exodus and Kings, as the reckoning reads them; Genesis wholly from Swete.
    /// </summary>
    private Func<int, int, int, string?> Genesis5To12()
    {
        var brenton = SeptuagintTextSource.Read(TestResources.SeptuagintFolder, ShippedCorrections.Brenton);
        return (book, chapter, verse) =>
        {
            var source = book == Genesis ? swete.Source : brenton;
            var draft = source.Books.Single(b => b.CanonicalOrdinal == book)
                .Chapters.SingleOrDefault(c => c.Number == chapter)?
                .Verses.FirstOrDefault(v => v.Number == verse && v.Label.Length == 0);
            return draft is null ? null : string.Join(' ', draft.Words.Select(word => word.Surface));
        };
    }
}

/// <summary>
/// The corrections a rule settles in every book: a Latin letter for the Greek one it looks like, the
/// margin number run into a verse's first word, two words run together.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SweteCorrectionTests(Swete swete) : IClassFixture<Swete>
{
    [Theory]
    [InlineData(1, 9, 1, "Καὶ")]
    [InlineData(19, 1, 1, "ΜΑΚΑΡΙΟΣ")]
    [InlineData(20, 3, 22, "ἔσται")]
    public void TheVerseOpensWithItsWordAndNotItsNumber(int book, int chapter, int verse, string first)
    {
        var words = Words(book, chapter, verse, verse == 22 && book == 20 ? "a" : "");

        words[0].Should().Be(first);
    }

    [Theory]
    [InlineData(1, 40, 12, "’Ιωσήφ")]
    [InlineData(13, 12, 40, "Νεφθαλεὶ")]
    [InlineData(26, 30, 8, "Αἴγυπτον")]
    [InlineData(1, 6, 21, "βρωμάτων")]
    [InlineData(1, 6, 22, "κύριος")]
    public void TheWordReadsInGreekLetters(int book, int chapter, int verse, string word) =>
        Words(book, chapter, verse, "").Should().Contain(word);

    /// <summary>
    /// Where no rule settles a Latin letter the token is left as the transcription reads it: Psalm 63
    /// opens Bἰς where every witness has Εἰς, so the letter on the page is not a B at all.
    /// </summary>
    [Fact]
    public void AMisreadLetterIsLeftAsItIs() =>
        Words(19, 63, 1, "")[0].Should().Be("Bἰς");

    /// <summary>The reasons of the corrections a rule settles from the token itself, one per kind.</summary>
    private static readonly HashSet<string> RuleReasons =
    [
        "A Latin letter for the Greek one it looks exactly like",
        "The chapter or verse number Swete prints in the margin, run into the verse's first word",
        "Two words run together; Brenton's Greek and GLAUx both read them apart, and Swete prints each",
        "The verse number Swete prints in figures, let into the text",
        "The verse number Swete prints in figures, over the first letters of the word the verses beside it print",
        "A figure inside a word: the letter it looks like, or a stray mark, where Swete prints the word it leaves",
        "The lunate sigma, which the conversion wrote as its code point",
    ];

    private static IReadOnlyList<TextCorrection> Rows =>
    [
        .. ShippedCorrections.Swete.Of(TextCorrection.Tokens).Where(row => !row.Own.StartsWith("48.", StringComparison.Ordinal)),
        .. ShippedCorrections.Of(SweteOldGreekTextSource.Slug).Of(TextCorrection.Tokens),
    ];

    [Fact]
    public void EveryCorrectionChangesSomethingAndSaysWhy()
    {
        Rows.Count(row => RuleReasons.Contains(row.Why)).Should().Be(1001);
        Rows.Should().OnlyContain(row => row.Before != row.After && row.Why.Length > 0);
    }

    [Fact]
    public void NoCorrectionIsMadeInAVerseRestoredByHand()
    {
        var byHand = Rows.Where(row => row.Why.StartsWith("Brenton's Greek and GLAUx's both read", StringComparison.Ordinal)
                                       || row.Why.StartsWith("Two words run together, the article misread", StringComparison.Ordinal)
                                       || row.Why.StartsWith("Printed in Swete, vol. 1 (Cambridge, 1901), p. 143, beside", StringComparison.Ordinal))
            .Select(row => row.Own).ToHashSet();

        byHand.Should().HaveCount(16);
        Rows.Where(row => RuleReasons.Contains(row.Why)).Should().NotContain(row => byHand.Contains(row.Own));
    }

    private IReadOnlyList<string> Words(int book, int chapter, int verse, string label) =>
        [.. swete.Book(book).Chapters.Single(c => c.Number == chapter).Verses
            .Single(v => v.Number == verse && v.Label == label).Words.Select(w => w.Surface)];
}

/// <summary>
/// The words read back off the printed page: what the page prints where the transcription has none,
/// and nothing where the page prints the verse as the transcription does.
/// </summary>
[Trait(TestCategory.Name, TestCategory.Corpus)]
public class SwetePageTests(Swete swete) : IClassFixture<Swete>
{
    private const int Genesis = 1;

    [Theory]
    [InlineData(12, 9, "καὶ ἀπῆρεν Ἀβρὰμ καὶ πορευθεὶς ἐστρατοπέδευσεν ἐν τῇ ἐρήμῳ.")]
    [InlineData(13, 4, "εἰς τὸν τόπον τοῦ θυσιαστηρίου οὗ")]
    [InlineData(25, 33, "καὶ εἶπεν αὐτῷ Ἰακώβ Ὄμοσόν μοι σήμερον.")]
    public void TheVerseReadsAsThePagePrintsIt(int chapter, int verse, string printed) =>
        Swete.Text(swete.Verse(Genesis, chapter, verse)).Should().Contain(printed);

    /// <summary>
    /// At 26:1 the page prints ἐγενήθη where Brenton and GLAUx read ἐγένετο: what goes back is the
    /// page's word, not the witnesses'.
    /// </summary>
    [Fact]
    public void ThePagesWordGoesInWhereItIsNotTheWitnesses() =>
        Swete.Text(swete.Verse(Genesis, 26, 1)).Should().Contain("ὃς ἐγενήθη ἐν τῷ χρόνῳ");

    /// <summary>
    /// At 29:25 Brenton and GLAUx read ἐδούλευσα παρὰ σοί, and the page prints ἐδούλευσα σοί as the
    /// transcription does: a reading of the manuscript, left as it is.
    /// </summary>
    [Fact]
    public void AVerseThePagePrintsAsTheTranscriptionDoesIsLeft() =>
        Swete.Text(swete.Verse(Genesis, 29, 25)).Should().Contain("ἐδούλευσα σοί;");

    [Fact]
    public void EveryRestorationAddsWordsAndCitesItsPage()
    {
        var page = ShippedCorrections.Swete.Of(TextCorrection.Tokens)
            .Where(row => row.Why.EndsWith("; the transcription lost it", StringComparison.Ordinal)).ToList();
        page.Should().HaveCount(834);
        page.Where(r => Words(r.After) <= Words(r.Before) || !r.Why.StartsWith("Printed in Swete, vol. ", StringComparison.Ordinal))
            .Should().BeEmpty();
    }

    private static int Words(string? tokens) => tokens is null ? 0 : tokens.Split(' ').Length;
}
