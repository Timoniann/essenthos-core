using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// The table of §3 of the plan (DOC-0224): which hash moves for which kind of change. Each case
/// is a verse before and after, and the test says which products the manifest would mark stale.
/// </summary>
public class VerseHashesTests
{
    private static readonly VerseWord[] Verse =
    [
        new("In", " ", Lemma: "in", Strong: "H7225", Morphology: """{"pos":"prep"}""", Gloss: "in"),
        new("the", " ", Lemma: "the", Strong: null, Morphology: """{"pos":"det"}""", Gloss: null),
        new("beginning", " ", Lemma: "beginning", Strong: "H7225", Morphology: """{"pos":"noun"}""", Gloss: "beginning"),
        new("God", " ", Lemma: "God", Strong: "H430", Morphology: """{"pos":"noun"}""", Gloss: "God"),
    ];

    private static VerseHashes Before => VerseHashes.Of(Verse);

    [Fact]
    public void TheSameWordsHashTheSameEveryTime()
    {
        VerseHashes.Of(Verse).Should().Be(Before);
        Before.Text.Should().HaveLength(VerseHashes.Length).And.MatchRegex("^[0-9a-f]+$");
        Before.Annotation.Should().HaveLength(VerseHashes.Length).And.MatchRegex("^[0-9a-f]+$");
    }

    [Fact]
    public void FirstWordRemovedAndANewOneAppendedKeepsTheCountAndMovesBothHashes()
    {
        var after = VerseHashes.Of(Verse.Skip(1).Append(new VerseWord("created", ".", "create", "H1254", """{"pos":"verb"}""", "create")));

        VerseManifests.Compare(Before, after).Should().Be(VerseChange.Text, "every product citing the verse is stale");
        after.Annotation.Should().NotBe(Before.Annotation);
    }

    [Fact]
    public void TwoWordsSwappedMovesBothHashes()
    {
        var swapped = (VerseWord[])Verse.Clone();
        (swapped[1], swapped[2]) = (swapped[2], swapped[1]);

        VerseManifests.Compare(Before, VerseHashes.Of(swapped)).Should().Be(VerseChange.Text);
        VerseHashes.Of(swapped).Annotation.Should().NotBe(Before.Annotation);
    }

    [Fact]
    public void ALetterFixedWithPositionsKeptMovesTheTextHashOnly()
    {
        var fixedLetter = (VerseWord[])Verse.Clone();
        fixedLetter[2] = fixedLetter[2] with { Surface = "beginnyng" };

        var after = VerseHashes.Of(fixedLetter);
        VerseManifests.Compare(Before, after).Should().Be(VerseChange.Text, "mapping rows of the verse are checked against their surfaces");
        after.Annotation.Should().Be(Before.Annotation, "nothing said about the words changed");
    }

    [Fact]
    public void PunctuationInTheTrailerIsText()
    {
        var trailer = (VerseWord[])Verse.Clone();
        trailer[3] = trailer[3] with { Trailer = ", " };

        VerseManifests.Compare(Before, VerseHashes.Of(trailer)).Should().Be(VerseChange.Text);
    }

    [Fact]
    public void ALemmaStrongNumberParsingOrGlossCorrectedMovesTheAnnotationHashOnly()
    {
        var corrections = new Func<VerseWord, VerseWord>[]
        {
            word => word with { Lemma = "begin" },
            word => word with { Strong = "H7226" },
            word => word with { Morphology = """{"pos":"noun","number":"sg"}""" },
            word => word with { Gloss = "a beginning" },
        };

        foreach (var correct in corrections)
        {
            var corrected = (VerseWord[])Verse.Clone();
            corrected[2] = correct(corrected[2]);

            var after = VerseHashes.Of(corrected);
            VerseManifests.Compare(Before, after).Should().Be(VerseChange.Annotation, "links pair tokens and stay; the Strong index and the names projection of the verse are redone");
            after.Text.Should().Be(Before.Text);
        }
    }

    [Fact]
    public void AWordInsertedMovesBothHashes()
    {
        var title = Verse.Prepend(new VerseWord("A", " ", "a", null, null, null));

        var after = VerseHashes.Of(title);
        VerseManifests.Compare(Before, after).Should().Be(VerseChange.Text);
        after.Annotation.Should().NotBe(Before.Annotation);
    }

    [Fact]
    public void AVerseSplitOrMergedShowsAsKeysGoneAndKeysNew()
    {
        var one = VerseKey.Parse("1.1:1");
        var oneA = VerseKey.Parse("1.1:1a");
        var oneB = VerseKey.Parse("1.1:1b");
        var before = new Dictionary<VerseKey, VerseHashes> { [one] = Before };
        var after = new Dictionary<VerseKey, VerseHashes>
        {
            [oneA] = VerseHashes.Of(Verse.Take(2)),
            [oneB] = VerseHashes.Of(Verse.Skip(2)),
        };

        VerseManifests.Diff(before, after).Should().Equal((one, VerseChange.Gone), (oneA, VerseChange.New), (oneB, VerseChange.New));
        VerseManifests.Diff(after, before).Should().Equal((one, VerseChange.New), (oneA, VerseChange.Gone), (oneB, VerseChange.Gone));
    }

    [Fact]
    public void AnUnchangedVerseIsNotInTheDiff()
    {
        var key = VerseKey.Parse("1.1:1");
        var before = new Dictionary<VerseKey, VerseHashes> { [key] = Before };
        var after = new Dictionary<VerseKey, VerseHashes> { [key] = VerseHashes.Of(Verse) };

        VerseManifests.Diff(before, after).Should().BeEmpty();
    }

    [Fact]
    public void NullAndEmptyAreDifferentValues()
    {
        var empty = (VerseWord[])Verse.Clone();
        empty[1] = empty[1] with { Strong = "" };

        VerseManifests.Compare(Before, VerseHashes.Of(empty)).Should().Be(VerseChange.Annotation);
    }

    [Fact]
    public void AFieldBoundaryMovingIsAChangeEvenWhenTheLettersAreTheSame()
    {
        var moved = (VerseWord[])Verse.Clone();
        moved[0] = moved[0] with { Surface = "In ", Trailer = "" };

        VerseManifests.Compare(Before, VerseHashes.Of(moved)).Should().Be(VerseChange.Text);

        var joined = new[] { new VerseWord("In the", " ") };
        var apart = new[] { new VerseWord("In", " "), new VerseWord("the", " ") };
        VerseHashes.Of(joined).Text.Should().NotBe(VerseHashes.Of(apart).Text, "a word boundary is part of the text");
    }

    [Fact]
    public void TheHasherStartsAFreshVerseAfterEachFinish()
    {
        using var hasher = new VerseHasher();
        foreach (var word in Verse)
        {
            hasher.Add(word);
        }

        var first = hasher.Finish();
        hasher.Add(Verse[0]);
        var second = hasher.Finish();

        first.Should().Be(Before);
        second.Should().Be(VerseHashes.Of(Verse.Take(1)));
    }

    [Fact]
    public void TheManifestRowRoundTripsThroughItsSchema()
    {
        var folder = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));
        var store = new ProductStore(folder);
        var rows = new[]
        {
            new VerseManifestRow(VerseKey.Parse("1.1:1"), 1, 4, Before),
            new VerseManifestRow(VerseKey.Parse("1.31:50a"), 51, 2, VerseHashes.Of(Verse.Take(2))),
        };
        try
        {
            using (var writer = store.Create(VerseManifestRow.ProductOf("KJV"), ProductKind.Machine, VerseManifestRow.Schema, "test", []))
            {
                writer.WriteAll(rows);
            }

            using var reader = store.Open(VerseManifestRow.ProductOf("KJV"), VerseManifestRow.Schema);
            reader.ReadAll().Should().Equal(rows);
            reader.Header!.Product.Should().Be("Essenthos/texts/KJV/verses.essx");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
