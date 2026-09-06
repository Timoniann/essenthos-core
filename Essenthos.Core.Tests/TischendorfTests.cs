using System.Text;
using System.Text.Json;
using Essenthos.Core.Loading;
using Essenthos.Core.Strong;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.Tischendorf;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Tischendorf's eighth edition: that the file holds what its filename claims, and that the two
/// things in it that no other text here does are handled rather than absorbed.
///
/// Which edition a file is gets established from the text, not from where it was downloaded. The
/// eighth is where Tischendorf weighed the Sinaiticus he had found, and the readings asserted below
/// are the ones that separate it from the Received Text on one side and from Westcott and Hort on
/// the other — so a file that turned out to be a seventh edition, or somebody's critical text under
/// Tischendorf's name, fails here rather than loading.
/// </summary>
public sealed class TischendorfTests
{
    private const int Words = 137_711;

    /// <summary>
    /// Every verse the edition prints. The file also prints John 7:53-8:11 twice, which is 154
    /// further lines and no further verses.
    /// </summary>
    private const int Verses = 7_939;

    /// <summary>Read once: the whole New Testament, and most of these tests ask about it.</summary>
    private static readonly Lazy<TextSource> Loaded =
        new(() => TischendorfTextSource.Read(TestResources.TischendorfFolder));

    private static IReadOnlyList<WordDraft> Verse(int canonical, int chapter, int verse) =>
        Loaded.Value.Books.Single(book => book.CanonicalOrdinal == canonical)
            .Chapters.Single(read => read.Number == chapter)
            .Verses.Single(read => read.Number == verse).Words;

    private static string Line(int canonical, int chapter, int verse) =>
        string.Concat(Verse(canonical, chapter, verse).Select(word => word.Surface + word.Trailer));

    private static bool Holds(int canonical, int chapter, int verse) =>
        Loaded.Value.Books.Single(book => book.CanonicalOrdinal == canonical)
            .Chapters.SingleOrDefault(read => read.Number == chapter)?
            .Verses.Any(read => read.Number == verse) ?? false;

    [Fact]
    public void TheEditionHoldsTheWholeNewTestament()
    {
        var books = Loaded.Value.Books;

        books.Should().HaveCount(27);
        books.Select(book => book.CanonicalOrdinal).Should().BeInAscendingOrder()
            .And.OnlyHaveUniqueItems();
        books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses)
            .Should().HaveCount(Verses);
    }

    /// <summary>
    /// A critical text at all, which the Received Text readings are what say. Any edition that had
    /// these would not be Tischendorf's however it was labelled.
    /// </summary>
    [Theory]
    [InlineData(40, 6, 13, "ῥῦσαι ἡμᾶς ἀπὸ τοῦ πονηροῦ.")]
    [InlineData(54, 3, 16, "ὃς ἐφανερώθη")]
    public void ItReadsAsACriticalTextAndNotAsTheReceivedText(
        int canonical, int chapter, int verse, string reading) =>
        Line(canonical, chapter, verse).Should().Contain(reading);

    /// <summary>
    /// Acts 8:37 and the Johannine comma, which the Received Text has and this does not. The comma
    /// is a clause rather than a verse, so it is checked by what 1 John 5:7 does say.
    /// </summary>
    [Fact]
    public void ItHasNeitherOfTheTwoPassagesTheReceivedTextAddsAndTheCriticalTextDoesNot()
    {
        Holds(44, 8, 37).Should().BeFalse();
        Line(62, 5, 7).Should().NotContain("οὐρανῷ").And.Contain("τρεῖς εἰσιν οἱ μαρτυροῦντες");
    }

    /// <summary>
    /// And Tischendorf's rather than Westcott and Hort's. He had found Sinaiticus and followed it,
    /// so these four places are where the two poles of nineteenth-century criticism come apart —
    /// and they are four of the places where Nestle had to take two of the three votes.
    /// </summary>
    [Theory]
    [InlineData(41, 1, 1, "Ἰησοῦ Χριστοῦ", "υἱοῦ θεοῦ")]
    [InlineData(43, 1, 18, "ὁ μονογενὴς υἱὸς", "μονογενὴς θεὸς")]
    [InlineData(44, 20, 28, "τὴν ἐκκλησίαν τοῦ κυρίου", "τοῦ θεοῦ,")]
    [InlineData(52, 2, 7, "ἐγενήθημεν ἤπιοι", "νήπιοι")]
    public void ItFollowsSinaiticusWhereWestcottAndHortDoNot(
        int canonical, int chapter, int verse, string reads, string doesNotRead) =>
        Line(canonical, chapter, verse).Should().Contain(reads).And.NotContain(doesNotRead);

    /// <summary>
    /// The sweat like blood, which Tischendorf prints on Sinaiticus's authority and Westcott and
    /// Hort print inside their double brackets. It is the clearest case of the two editions holding
    /// the same words and disagreeing about them, and no word-level comparison can see it.
    /// </summary>
    [Fact]
    public void ItPrintsTheSweatLikeBloodWithoutQualification()
    {
        Holds(42, 22, 43).Should().BeTrue();
        Verse(42, 22, 44).Should().NotBeEmpty()
            .And.OnlyContain(word => !word.Morphology!.Contains("brackets"));
    }

    /// <summary>
    /// The one passage this edition prints twice, and the only doubled address in the 27 books.
    /// Tischendorf gives the woman taken in adultery in the ordinary form and, beneath it, the
    /// divergent text of Codex Bezae; the file carries both under the same verse numbers, Bezae
    /// first, unaccented and with its itacisms.
    ///
    /// Loaded naively that is John 7:53-8:11 with every word in it twice — 163 extra words, and a
    /// hundred and sixty-three spurious variants the first time anything is linked to it. So the
    /// ordinary form is kept and the reader refuses any other doubled address rather than making
    /// the same choice where nobody has looked.
    /// </summary>
    [Fact]
    public void ThePericopeAdulteraeIsLoadedOnceAndInTheOrdinaryForm()
    {
        var john = TischendorfReader.Read(File.ReadAllText(TestResources.Tischendorf("JOH")));

        john.Doubled.Should().HaveCount(12)
            .And.Contain((7, 53)).And.Contain((8, 11));

        Line(43, 8, 11).Should().Contain("εἶπε δὲ αὐτῇ ὁ Ἰησοῦς")
            .And.NotContain("κατακρείνω", "that spelling is Codex Bezae's, which is the copy not loaded");
        Verse(43, 8, 2).Should().HaveCount(18);
    }

    [Fact]
    public void EveryOtherBookWritesEachAddressOnce() =>
        TischendorfTextSource.Books.Where(book => book != "JOH")
            .Should().OnlyContain(book =>
                TischendorfReader.Read(File.ReadAllText(TestResources.Tischendorf(book))).Doubled.Count == 0);

    /// <summary>
    /// A lemma is an identifier, so two spellings of one is the same defect as a text answering to
    /// two slugs. This release is composed already and the check is here for the next one: the day
    /// a Greek lemma arrives decomposed, every join to it returns nothing and reports no error.
    /// PRB-0384.
    /// </summary>
    [Fact]
    public void EveryLemmaIsComposed() =>
        Loaded.Value.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses)
            .SelectMany(verse => verse.Words)
            .Should().OnlyContain(word =>
                word.Lemma != null && word.Lemma == word.Lemma.Normalize(NormalizationForm.FormC));

    /// <summary>
    /// The word as printed rather than as corrected. The Kethiv is Tischendorf's and the Qere is the
    /// digital edition's editor saying what he thinks it should have been; the corpus holds editions
    /// rather than improvements of them, and where the two differ it says so on the word.
    /// </summary>
    [Fact]
    public void ItLoadsTheWordAsPrintedAndRecordsTheCorrection()
    {
        var corrected = Loaded.Value.Books.SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Words)
            .Where(word => Feature(word, "qere") != null)
            .ToList();

        corrected.Should().HaveCount(11);

        // Revelation 14:18, where the printed text reads τοὶς βότρυας and the editor thinks the
        // grammar wants τοὺς.
        Verse(66, 14, 18).Should().Contain(word =>
            word.Surface == "τοὶς" && Feature(word, "qere") == "τοὺς");
    }

    /// <summary>
    /// The homonym marker the analytical lexicon uses is not part of the lemma. Left on, 201 words
    /// carry a lemma spelt "δοῦλος (II)" that no other text in the corpus can ever equal, and the
    /// join fails silently — which is the same failure PRB-0384 was, arriving by a different route.
    /// </summary>
    [Fact]
    public void TheHomonymMarkerIsKeptOffTheLemma()
    {
        var words = Loaded.Value.Books.SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Words).ToList();

        // Not any parenthesis: ἔξεστι(ν) is a headword the lexicon writes that way, 32 times, and
        // it is the spelling rather than a note about which entry.
        words.Should().OnlyContain(word => !word.Lemma!.Contains(" ("));
        words.Count(word => Feature(word, "homonym") != null).Should().Be(169);
    }

    [Fact]
    public void EveryWordCarriesAStrongNumberAndAParse() =>
        Loaded.Value.Books.SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses)
            .SelectMany(verse => verse.Words)
            .Should().HaveCount(Words - Doubled)
            .And.OnlyContain(word =>
                StrongNumbers.Normalize(word.StrongNumber) == word.StrongNumber
                && word.Morphology!.Contains("robinson"));

    private static string? Feature(WordDraft word, string name) =>
        word.Morphology is null
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, string>>(word.Morphology)!
                .GetValueOrDefault(name);

    /// <summary>The Bezan copy of the pericope, which is read and not loaded.</summary>
    private const int Doubled = 163;

    /// <summary>
    /// The numbers this edition writes that no Robinson-tagged text in the corpus does, which is
    /// the whole of what <see cref="GreekLemmaNumbers"/> is for. Asserted from the file rather than
    /// from the list, so a release that started numbering something else the old way is caught here
    /// instead of turning up as 2,729 invented textual variants.
    /// </summary>
    [Fact]
    public void EveryNumberThisEditionAloneUsesIsEitherMappedOrDeliberatelyNot()
    {
        var mine = Loaded.Value.Books.SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Words)
            .Select(word => word.StrongNumber!).ToHashSet(StringComparer.Ordinal);

        TextSource[] others =
        [
            WestcottHortTextSource.Read(TestResources.WestcottHortFolder),
            TextusReceptusTextSource.Read(TestResources.TextusReceptusFolder, Edition.Scrivener1894),
            TextusReceptusTextSource.Read(TestResources.TextusReceptusFolder, Edition.Stephanus1550),
            ByzantineTextSource.Read(TestResources.ByzantineFolder),
        ];

        var theirs = others.SelectMany(text => text.Books)
            .SelectMany(book => book.Chapters).SelectMany(chapter => chapter.Verses)
            .SelectMany(verse => verse.Words)
            .Where(word => word.StrongNumber is not null)
            .Select(word => word.StrongNumber!).ToHashSet(StringComparer.Ordinal);

        // The four with no corroborated target. διατί, ἱνατί and ἔσθησις the other editions write
        // as two words or as a different word, so a redirect would be a guess and 31 unpaired words
        // are cheaper than one invented one; καίγε needs none, because Nestle 1904 writes G2534 too
        // and Nestle is the edition this one is laid against.
        string[] unmapped = ["G1302", "G2444", "G2067", "G2534"];

        mine.Except(theirs).Except(unmapped).Should()
            .OnlyContain(number => GreekLemmaNumbers.Lemmatised.ContainsKey(number));

        GreekLemmaNumbers.Lemmatised.Values.Should().OnlyContain(number => theirs.Contains(number));
    }

    /// <summary>
    /// The shared fingerprint that says whose tagging this is. The digital edition states in its
    /// own README that the analysis was ported from Robinson's Westcott-Hort by a program, and the
    /// corpus can see the join: σου, σε, σοί and σύ all carry G4771 here as they do in every
    /// Robinson-tagged text, where Strong's own dictionary numbers them G4675, G4571 and G4671.
    /// A public-domain grant over a tagging layer is worth reading precisely because a tagging layer
    /// has an author, and this is what says who this one's is.
    /// </summary>
    [Fact]
    public void TheTaggingCarriesRobinsonsOwnPronounNumbering()
    {
        var singular = Loaded.Value.Books.SelectMany(book => book.Chapters)
            .SelectMany(chapter => chapter.Verses).SelectMany(verse => verse.Words)
            .Where(word => word.Lemma == "σύ")
            .ToList();

        singular.Should().NotBeEmpty()
            .And.OnlyContain(word => word.StrongNumber == "G4771" || word.StrongNumber == "G5210");
        singular.Should().Contain(word => word.StrongNumber == "G4771");
    }
}
