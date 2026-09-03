using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests;

/// <summary>
/// STEPBible's morpheme layer, and what it changes about the prefixes.
///
/// The rows are TAHOT's own, copied from the file, because the point of the join is that two
/// independent sources describe the same words and an invented row cannot disagree with anything.
/// </summary>
public class TahotSegmentationTests(ITestOutputHelper output)
{
    /// <summary>
    /// Genesis 1:1 as TAHOT writes it: seven words, eleven morphemes, which is the same eleven the
    /// mapping file lists.
    /// </summary>
    private const string InTheBeginning =
        """
        Eng (Heb) Ref & Type	Hebrew	Transliteration	Translation	dStrongs	Grammar
        Gen.1.1#01=L	בְּ/רֵאשִׁ֖ית	be./re.Shit	in/ beginning	H9003/{H7225G}	HR/Ncfsa
        Gen.1.1#02=L	בָּרָ֣א	ba.Ra'	he created	{H1254A}	HVqp3ms
        Gen.1.1#03=L	אֱלֹהִ֑ים	'E.lo.Him	God	{H0430G}	HNcmpa
        Gen.1.1#04=L	אֵ֥ת	'et	<obj.>	{H0853}	HTo
        Gen.1.1#05=L	הַ/שָּׁמַ֖יִם	ha./sha.Ma.yim	the/ heavens	H9009/{H8064}	HTd/Ncmpa
        Gen.1.1#06=L	וְ/אֵ֥ת	ve./'Et	and/ <obj.>	H9002/{H0853}	HC/To
        Gen.1.1#07=L	הָ/אָֽרֶץ\׃	ha./'A.retz	the/ earth	H9009/{H0776G}\H9016	HTd/Ncfsa
        """;

    /// <summary>
    /// Genesis 1:7, where the mem the King James renders "from" is a prefix. The mapping file
    /// numbers it H4480, which is also the free-standing preposition, so nothing in the corpus could
    /// tell it from a word until this said so.
    /// </summary>
    private const string FromUnderTheFirmament =
        """
        Gen.1.7#09=L	מִ/תַּ֣חַת	mi./Ta.chat	[were] from/ under	H9006/{H8478G}	HR/Ncmsc
        Gen.1.7#10=L	לָ/רָקִ֔יעַ	la./ra.Ki.a'	to the/ firmament	H9005/{H7549}	HRd/Ncmsa
        """;

    /// <summary>Genesis 1:1's morphemes as the mapping file lists them, with its own numbering.</summary>
    private static readonly HebrewEntry[] MappedInTheBeginning =
    [
        new("H9003", "c1", 1, "in"),
        new("H7225", "c1", 2, "beginning"),
        new("H1254", "c1", 3, "create"),
        new("H430", "c1", 4, "god(s)"),
        new("H853", "c1", 5, "[object marker]"),
        new("H9009", "c1", 6, "the"),
        new("H8064", "c1", 7, "heavens"),
        new("H9000", "c1", 8, "and"),
        new("H853", "c1", 9, "[object marker]"),
        new("H9009", "c1", 10, "the"),
        new("H776", "c1", 11, "earth"),
    ];

    [Fact]
    public void AWordIsReadAsTheMorphemesItIsMadeOf()
    {
        var aligned = Align(InTheBeginning, MappedInTheBeginning);

        aligned.Should().NotBeNull();
        aligned!.Should().HaveCount(MappedInTheBeginning.Length);
        aligned[1].Gloss.Should().Be("in");
        aligned[2].Gloss.Should().Be("beginning");
        aligned[11].Gloss.Should().Be("earth");
    }

    /// <summary>
    /// The waw is H9002 here and H9000 in the mapping file, and the article inside a preposition is
    /// H9010 against H9009. Reading those as different morphemes would report a disagreement at
    /// every conjunction in the Bible.
    /// </summary>
    [Fact]
    public void TheTwoSourcesNumberingOfTheSameMorphemeIsTreatedAsAgreement()
    {
        var aligned = Align(InTheBeginning, MappedInTheBeginning);

        aligned!.Should().ContainKey(8);
        aligned[8].IsConjunction.Should().BeTrue();
    }

    [Fact]
    public void ThePrefixesAreTheOnesTheSourceCallsPrefixes()
    {
        var aligned = Align(InTheBeginning, MappedInTheBeginning);

        aligned![1].IsPrefix.Should().BeTrue();
        aligned[6].IsPrefix.Should().BeTrue();
        aligned[2].IsPrefix.Should().BeFalse();
        aligned[5].IsPrefix.Should().BeFalse();
    }

    /// <summary>
    /// The verse end and the maqqef are written after a backslash and are not morphemes. Reading
    /// them as such would leave every verse one morpheme longer than the corpus's and line up
    /// nothing.
    /// </summary>
    [Fact]
    public void PunctuationIsNotAMorpheme()
    {
        var aligned = Align(InTheBeginning, MappedInTheBeginning);

        aligned!.Values.Should().NotContain(morpheme => morpheme.Gloss == "verseEnd");
    }

    /// <summary>
    /// A morpheme the two number differently is dropped rather than paired with whatever stands in
    /// its place, and the morphemes on either side of it still line up.
    /// </summary>
    [Fact]
    public void AMorphemeTheTwoSourcesDisagreeOnIsLeftUnpaired()
    {
        HebrewEntry[] disputed =
        [
            new("H9003", "c1", 1, "in"),
            new("H7225", "c1", 2, "beginning"),
            new("H1254", "c1", 3, "create"),
            new("H3068", "c1", 4, "YHWH"),
            new("H853", "c1", 5, "[object marker]"),
            new("H9009", "c1", 6, "the"),
            new("H8064", "c1", 7, "heavens"),
            new("H9000", "c1", 8, "and"),
            new("H853", "c1", 9, "[object marker]"),
            new("H9009", "c1", 10, "the"),
            new("H776", "c1", 11, "earth"),
        ];

        var aligned = Align(InTheBeginning, disputed);

        aligned!.Should().NotContainKey(4);
        aligned.Should().ContainKey(1);
        aligned.Should().ContainKey(11);
    }

    /// <summary>
    /// The gloss is kept as the source prints it and the words are pulled out of it separately:
    /// square brackets mark what the translator supplied and are part of no word.
    /// </summary>
    [Fact]
    public void TheBracketsThatMarkASuppliedWordAreNotPartOfIt()
    {
        var mapped = new HebrewEntry[]
        {
            new("H4480", "c1", 1, "from"),
            new("H8478", "c1", 2, "under"),
            new("H9005", "c1", 3, "to"),
            new("H7549", "c1", 4, "expanse"),
        };

        var aligned = Align(FromUnderTheFirmament, mapped, book: 1, chapter: 1, verse: 7);

        aligned![1].Gloss.Should().Be("[were] from");
        aligned[1].GlossWords.Should().Equal("were", "from");
        aligned[1].Renders("from").Should().BeTrue();
    }

    /// <summary>
    /// The case the whole join is for. The King James writes "from under the firmament"; the mapping
    /// file numbers the מ H4480, the same number as the free-standing מִן, so this project's own list
    /// of prefixes cannot contain it without claiming every preposition in the Bible is a prefix.
    /// TAHOT says which it is.
    /// </summary>
    [Fact]
    public void AnEnglishFromReachesThePrefixOnlyWhenTheSourceSaysItIsOne()
    {
        HebrewEntry[] hebrew =
        [
            new("H4480", "c1", 1, "from"),
            new("H8478", "c1", 2, "under"),
            new("H9005", "c1", 3, "to"),
            new("H7549", "c1", 4, "expanse"),
        ];
        EnglishSegment[] english =
        [
            Segment(hebrew, 2, "from", "under"),
            Segment(hebrew, 4, "the", "firmament"),
        ];

        HebrewPrefixes.Match(hebrew, english).Should().NotContain(match => match.EnglishWord == 0);

        var aligned = Align(FromUnderTheFirmament, hebrew, book: 1, chapter: 1, verse: 7);
        var matched = HebrewPrefixes.Match(hebrew, english, aligned);

        matched.Should().ContainSingle(match => match.EnglishWord == 0 && match.HebrewPosition == 1);
        matched.Single(match => match.EnglishWord == 0).Stated.Should().BeTrue();
        matched.Single(match => match.EnglishWord == 0).Confidence.Should().Be(HebrewPrefixes.Stated);
    }

    /// <summary>
    /// The King James writes what the source does not: TAHOT glosses every waw "and" and the
    /// translators wrote "But", "Now" and "Then". The project's own list still answers for those, so
    /// the segmentation only ever adds.
    /// </summary>
    [Fact]
    public void AWordTheSourceDoesNotGlossIsStillMatchedFromTheProjectsOwnList()
    {
        HebrewEntry[] hebrew = [new("H9000", "c1", 1, "and"), new("H776", "c1", 2, "earth")];
        EnglishSegment[] english = [Segment(hebrew, 2, "But", "the", "earth")];

        const string tahot =
            """
            Gen.1.2#01=L	וְ/הָ/אָ֗רֶץ	ve./ha./'A.retz	and/ the/ earth	H9002/{H0776G}	HC/Ncfsa
            """;

        var matched = HebrewPrefixes.Match(hebrew, english, Align(tahot, hebrew, 1, 1, 2));

        matched.Should().ContainSingle(match => match.EnglishWord == 0 && match.HebrewPosition == 1);
        matched.Single().Stated.Should().BeFalse();
        matched.Single().Confidence.Should().Be(HebrewPrefixes.Adjacent);
    }

    /// <summary>
    /// The measurement, over the two files themselves. It is the number the change is worth: how
    /// many function words reach a Hebrew morpheme with the segmentation and without it, and how
    /// many of them rest on a gloss a source printed rather than on a list this project wrote.
    ///
    /// Skipped where TAHOT has not been fetched, because a checkout is meant to work without it.
    /// </summary>
    [Fact]
    public void TheStatedGlossesReachMoreFunctionWordsThanTheProjectsListAlone()
    {
        var volumes = TestResources.Tahot();
        if (volumes.Count == 0)
        {
            output.WriteLine("TAHOT is not on disk; run scripts/fetch-stepbible.ps1");
            return;
        }

        var segmentation = TahotSegmentation.Read(volumes);
        var records = KjvBhsMapping.Read(TestResources.KjvBhsMapping);

        var withoutIt = 0;
        var withIt = 0;
        var stated = 0;
        var segmented = 0;

        foreach (var record in records)
        {
            withoutIt += HebrewPrefixes.Match(record.Hebrew, record.English).Count;

            var aligned = segmentation.Align(record.Book, record.Chapter, record.Verse, record.Hebrew);
            if (aligned is { Count: > 0 })
            {
                segmented++;
            }

            var matched = HebrewPrefixes.Match(record.Hebrew, record.English, aligned);
            withIt += matched.Count;
            stated += matched.Count(match => match.Stated);
        }

        output.WriteLine($"TAHOT: {segmentation.Verses} verses, {segmentation.Morphemes} morphemes");
        output.WriteLine($"mapping file: {records.Count} verses, {segmented} of them segmented");
        output.WriteLine($"prefix matches without TAHOT: {withoutIt}");
        output.WriteLine($"prefix matches with TAHOT:    {withIt}");
        output.WriteLine($"of which the source glosses:  {stated}");

        segmented.Should().BeGreaterThan(records.Count * 9 / 10);
        withIt.Should().BeGreaterThan(withoutIt);
        stated.Should().BeGreaterThan(withIt / 2);
    }

    private static IReadOnlyDictionary<int, TahotMorpheme>? Align(
        string rows,
        IReadOnlyList<HebrewEntry> hebrew,
        int book = 1,
        int chapter = 1,
        int verse = 1)
    {
        var path = Path.Combine(Path.GetTempPath(), $"tahot-{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(path, rows);
            return TahotSegmentation.Read([path]).Align(book, chapter, verse, hebrew);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static EnglishSegment Segment(IReadOnlyList<HebrewEntry> hebrew, int position, params string[] words) =>
        new([.. words.Select(word => new EnglishWord(word, false))],
            hebrew.SingleOrDefault(entry => entry.Position == position));
}
