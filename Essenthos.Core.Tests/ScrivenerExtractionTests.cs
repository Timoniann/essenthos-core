using Essenthos.Core.Loading;
using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Taking the second alternative of every variant group is claimed to yield Scrivener 1894. That is
/// a rule, and this is the answer key: <c>byztxt/greektext-scrivener</c> is the same New Testament
/// with no tags, so the extraction can be checked word for word against it.
///
/// A rule nobody can verify after the fact is how a reading nobody printed ends up in a corpus that
/// claims citability. These run over all 27 books, so they take a moment and are worth it.
/// </summary>
public class ScrivenerExtractionTests
{
    /// <summary>
    /// Every verse where the extraction and the answer key disagree, named rather than counted, so
    /// that a change anywhere shows up as itself instead of moving a number.
    ///
    /// Twenty-eight of them are fourteen adjacent pairs — the same words divided differently
    /// between two consecutive verses, which is a versification difference between the two
    /// transcriptions and not a textual one. Nine are form differences the composite does not mark
    /// as variants at all: <c>eiden</c> against <c>eide</c> at Matthew 4:16, <c>dusin</c> against
    /// <c>dusi</c> at Matthew 6:24 and Luke 16:13, and six that differ in length.
    ///
    /// None can be fixed by any rule over the pipes, because the composite does not record them.
    /// Colossians 4:10 was a thirty-seventh until the reader learnt that a variant group of two
    /// parses belongs to the word before it rather than being a word of its own; its disagreement
    /// was a phantom "{N-DSM}" in the middle of the verse, and it was the only entry here that was
    /// ever this reader's fault.
    /// </summary>
    private static readonly string[] KnownDifferences =
    [
        "MT 4:16", "MT 6:24", "MT 15:5", "MT 15:6", "MT 17:14", "MT 17:15", "MT 20:4", "MT 20:5",
        "MT 26:60", "MT 26:61", "MR 6:27", "MR 6:28", "LU 1:73", "LU 1:74", "LU 16:13", "AC 4:5",
        "AC 4:6", "AC 9:28", "AC 9:29", "AC 13:32", "AC 13:33", "AC 17:27", "EPH 3:17", "EPH 3:18",
        "EPH 3:20", "1TH 2:11", "1TH 2:12", "1TH 3:10", "1TH 5:13", "PHM 1:11",
        "PHM 1:12", "HEB 1:1", "HEB 1:2", "HEB 7:20", "HEB 7:21", "RE 14:13",
    ];

    private static IReadOnlyList<(string Book, UtrVerse Extracted, UtrVerse Answer)> Compared()
    {
        var compared = new List<(string, UtrVerse, UtrVerse)>(8_000);

        foreach (var book in TextusReceptusTextSource.Books)
        {
            var extracted = UtrReader.Read(File.ReadAllText(TestResources.TextusReceptus(book)), Edition.Scrivener1894);
            var answer = ScrivenerReader.Read(File.ReadAllText(TestResources.Scrivener(book)))
                .ToDictionary(v => (v.Chapter, v.Number));

            foreach (var verse in extracted.Where(v => answer.ContainsKey((v.Chapter, v.Number))))
            {
                compared.Add((book, verse, answer[(verse.Chapter, verse.Number)]));
            }
        }

        return compared;
    }

    [Fact]
    public void TheWholeNewTestamentIsRead()
    {
        var compared = Compared();

        compared.Should().HaveCountGreaterThan(7_900);
        compared.Select(c => c.Book).Distinct().Should().HaveCount(27);
    }

    /// <summary>
    /// The rule is right in all but the verses listed above. A number above that is a defect in the
    /// extraction; a number below it means the answer key changed and the list is stale.
    /// </summary>
    [Fact]
    public void TheSecondAlternativeIsScrivenerExceptWhereTheTranscriptionsThemselvesDisagree()
    {
        var differing = Compared()
            .Where(c => !c.Extracted.Words.Select(w => w.Surface)
                .SequenceEqual(c.Answer.Words.Select(w => w.Surface)))
            .Select(c => $"{c.Book} {c.Extracted.Chapter}:{c.Extracted.Number}")
            .ToList();

        differing.Should().BeEquivalentTo(KnownDifferences);
    }

    /// <summary>
    /// The first alternative is Stephanus, so it must agree with the Scrivener key less often than
    /// the second does. If it agreed more, the alternatives are the other way round and both texts
    /// are mislabelled — which nothing else here would catch.
    /// </summary>
    [Fact]
    public void TheFirstAlternativeIsTheOtherEditionAndAgreesLessOften()
    {
        var second = 0;
        var first = 0;

        foreach (var book in TextusReceptusTextSource.Books)
        {
            var content = File.ReadAllText(TestResources.TextusReceptus(book));
            var answer = ScrivenerReader.Read(File.ReadAllText(TestResources.Scrivener(book)))
                .ToDictionary(v => (v.Chapter, v.Number), v => v.Words.Select(w => w.Surface).ToList());

            second += Agreeing(UtrReader.Read(content, Edition.Scrivener1894), answer);
            first += Agreeing(UtrReader.Read(content, Edition.Stephanus1550), answer);
        }

        second.Should().BeGreaterThan(first);
    }

    /// <summary>
    /// Every word of the loaded edition keeps its Strong number, which is the whole reason this
    /// text is worth extracting: a Scrivener reading set nobody else publishes with tags on it.
    /// </summary>
    [Fact]
    public void EveryWordCarriesItsStrongNumber()
    {
        var untagged = UtrReader.Read(File.ReadAllText(TestResources.TextusReceptus("MT")), Edition.Scrivener1894)
            .SelectMany(v => v.Words.Select(w => (v.Chapter, v.Number, w)))
            .Where(w => w.w.Strong is null)
            .ToList();

        untagged.Should().BeEmpty(because: string.Join(
            ", ", untagged.Take(5).Select(u => $"{u.Chapter}:{u.Number} {u.w.Surface}")));
    }

    /// <summary>
    /// The two repositories spell theta and psi with each other's letters. Compared unfolded, every
    /// one of those is a false mismatch, and there are far more of them than the 36 real ones.
    /// </summary>
    [Fact]
    public void TheAnswerKeysAlphabetIsFoldedOntoTheCompositesBeforeAnythingIsCompared() =>
        ScrivenerReader.Fold("kayolikh").Should().Be("kaqolikh");

    private static int Agreeing(
        IReadOnlyList<UtrVerse> extracted,
        Dictionary<(int, int), List<string>> answer) =>
        extracted.Count(v =>
            answer.TryGetValue((v.Chapter, v.Number), out var words)
            && v.Words.Select(w => w.Surface).SequenceEqual(words));
}
