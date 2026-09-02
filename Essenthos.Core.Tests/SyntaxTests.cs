using System.Text.Json;
using Essenthos.Core.Bhsa;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.TextFabric;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// BHSA parsed and drafted once. A draft is a row of <c>word_group</c> before the database hands
/// out its id, so the nesting, the ordering, the features and the mother edges can all be asked of
/// it — and every one of them is a claim about the source, which is here to be read back.
/// </summary>
public sealed class DraftedSyntax
{
    private readonly Dictionary<WordGroupKind, List<SyntaxLoader.GroupDraft>> _byKind;

    public DraftedSyntax()
    {
        Bhsa = BhsaProject.Load(TestResources.Etcbc);

        // The loader maps each BHSA slot to the corpus word written for it. Nothing here is about
        // that mapping, so a slot stands for itself.
        Drafts = SyntaxLoader.Drafts(Bhsa, Bhsa.Words.ToDictionary(word => word.SlotId, word => (long)word.SlotId));
        _byKind = Drafts.GroupBy(draft => draft.Kind).ToDictionary(kind => kind.Key, kind => kind.ToList());
    }

    internal BhsaProject Bhsa { get; }

    internal List<SyntaxLoader.GroupDraft> Drafts { get; }

    internal List<SyntaxLoader.GroupDraft> Of(WordGroupKind kind) => _byKind[kind];

    /// <summary>One Text-Fabric feature file, read the way the loader reads it.</summary>
    internal static IDocument<T> Feature<T>(string name) =>
        (IDocument<T>)new DocumentParser().Parse(Path.Combine(TestResources.Etcbc, name + ".tf"));
}

/// <summary>
/// The syntax layer against the files it came from. Every number here was measured over
/// <c>otype.tf</c>, <c>oslots.tf</c> and <c>mother.tf</c> first, so one of these failing means the
/// loader stopped agreeing with BHSA rather than that BHSA moved.
/// </summary>
public class SyntaxTests(DraftedSyntax syntax) : IClassFixture<DraftedSyntax>
{
    /// <summary>
    /// The parser accepts several spellings of a subphrase relation so that an older release still
    /// reads, and that tolerance used to be written out whole: all 56,925 read <c>REG, rec</c>
    /// rather than <c>rec</c>, so the code BHSA documents matched nothing at all.
    /// </summary>
    [Fact]
    public void SubphraseRelationsAreSpelledTheWayTheSourceSpellsThem()
    {
        Spread(WordGroupKind.Subphrase, "relation").Should().Equal(new Dictionary<string, int>
        {
            ["rec"] = 34989,
            ["par"] = 11946,
            ["adj"] = 4138,
            ["atr"] = 3064,
            ["dem"] = 1847,
            ["mod"] = 941,
        });
    }

    /// <summary>
    /// Every feature value, against the file it was read from, row by row. BHSA's "not applicable"
    /// and "unknown" are absence rather than value, and are the only thing dropped.
    /// </summary>
    [Theory]
    [InlineData(WordGroupKind.Clause, "type", "typ")]
    [InlineData(WordGroupKind.Clause, "relation", "rela")]
    [InlineData(WordGroupKind.Clause, "domain", "domain")]
    [InlineData(WordGroupKind.ClauseAtom, "type", "typ")]
    [InlineData(WordGroupKind.ClauseAtom, "paragraph", "pargr")]
    [InlineData(WordGroupKind.Phrase, "type", "typ")]
    [InlineData(WordGroupKind.Phrase, "function", "function")]
    [InlineData(WordGroupKind.Phrase, "determination", "det")]
    [InlineData(WordGroupKind.PhraseAtom, "type", "typ")]
    [InlineData(WordGroupKind.PhraseAtom, "relation", "rela")]
    [InlineData(WordGroupKind.PhraseAtom, "determination", "det")]
    [InlineData(WordGroupKind.Subphrase, "relation", "rela")]
    [InlineData(WordGroupKind.HalfVerse, "label", "label")]
    public void AFeatureReadsBackAsTheSourceWroteIt(WordGroupKind kind, string key, string file)
    {
        var source = DraftedSyntax.Feature<string>(file);

        var disagreeing = syntax.Of(kind)
            .Where(draft => Value(draft, key) != Stated(source[draft.Node]))
            .Select(draft => $"{kind} {draft.Node}: {Value(draft, key) ?? "nothing"} for {source[draft.Node]}")
            .Take(5)
            .ToList();

        disagreeing.Should().BeEmpty();
        return;

        static string? Stated(string wrote) => wrote is "NA" or "?" or "none" or "unknown" ? null : wrote;
    }

    /// <summary>
    /// The three kinds the loader wrote featureless. BHSA has a value on every row of all of them,
    /// and two of the clause atom's — its embedding depth and its paragraph number — are the only
    /// features in the corpus nothing else can reconstruct.
    /// </summary>
    [Theory]
    [InlineData(WordGroupKind.ClauseAtom, 90704)]
    [InlineData(WordGroupKind.PhraseAtom, 267532)]
    [InlineData(WordGroupKind.HalfVerse, 45179)]
    public void TheKindsBhsaGivesFeaturesToCarryThem(WordGroupKind kind, int expected)
    {
        var drafts = syntax.Of(kind);

        drafts.Should().HaveCount(expected);
        drafts.Count(draft => draft.Features is null).Should().Be(0);
    }

    /// <summary>A half verse without its label is a span; with it, it is <em>Gen 1:1a</em>.</summary>
    [Fact]
    public void AHalfVerseIsLabelled()
    {
        Spread(WordGroupKind.HalfVerse, "label")
            .Should().Equal(new Dictionary<string, int> { ["A"] = 23213, ["B"] = 21610, ["C"] = 356 });
    }

    /// <summary>
    /// The embedding depth, which is an integer in the source and is stored as the source writes
    /// it. Zero is a depth like any other, so it must survive a filter written for absent values.
    /// </summary>
    [Fact]
    public void AClauseAtomCarriesItsDepth()
    {
        var tab = DraftedSyntax.Feature<int>("tab");

        var disagreeing = syntax.Of(WordGroupKind.ClauseAtom)
            .Where(draft => Value(draft, "tab") != tab[draft.Node].ToString())
            .Take(5)
            .ToList();

        disagreeing.Should().BeEmpty();
        Spread(WordGroupKind.ClauseAtom, "tab").Should().ContainKey("0");
    }

    /// <summary>
    /// The chain used to run through the atom levels, which are contiguous by construction, and so
    /// asked BHSA's split spans to fit inside them: 158 clauses and 404 phrases had a parent
    /// holding only part of them. A clause belongs to a sentence and a phrase to a clause, and then
    /// every group in the corpus is inside its parent.
    /// </summary>
    [Fact]
    public void EveryGroupIsInsideItsParent()
    {
        var escaping = syntax.Drafts
            .Where(draft => draft.Parent is { } parent && !Inside(draft, parent))
            .Select(draft => $"{draft.Kind} {draft.Node} in {draft.Parent!.Kind} {draft.Parent.Node}")
            .Take(5)
            .ToList();

        escaping.Should().BeEmpty();
    }

    /// <summary>
    /// Nothing in BHSA sits outside the kind above it, so an orphan means the nesting was derived
    /// wrongly rather than that the analysis left something out.
    /// </summary>
    [Theory]
    [InlineData(WordGroupKind.SentenceAtom)]
    [InlineData(WordGroupKind.Clause)]
    [InlineData(WordGroupKind.ClauseAtom)]
    [InlineData(WordGroupKind.Phrase)]
    [InlineData(WordGroupKind.PhraseAtom)]
    [InlineData(WordGroupKind.Subphrase)]
    public void NoGroupIsLeftWithoutAParent(WordGroupKind kind)
    {
        syntax.Of(kind).Count(draft => draft.Parent is null).Should().Be(0);
    }

    [Fact]
    public void TheTreeIsTheOneBhsaDescribes()
    {
        Parents(WordGroupKind.SentenceAtom).Should().Equal(WordGroupKind.Sentence);
        Parents(WordGroupKind.Clause).Should().Equal(WordGroupKind.Sentence);
        Parents(WordGroupKind.ClauseAtom).Should().Equal(WordGroupKind.Clause);
        Parents(WordGroupKind.Phrase).Should().Equal(WordGroupKind.Clause);
        Parents(WordGroupKind.PhraseAtom).Should().Equal(WordGroupKind.Phrase);
        Parents(WordGroupKind.Sentence).Should().BeEmpty();
        Parents(WordGroupKind.HalfVerse).Should().BeEmpty();

        return;

        List<WordGroupKind> Parents(WordGroupKind kind) => syntax.Of(kind)
            .Select(draft => draft.Parent?.Kind)
            .Distinct()
            .OfType<WordGroupKind>()
            .OrderBy(parent => parent)
            .ToList();
    }

    /// <summary>
    /// A construct chain inside an apposition is a subphrase inside a subphrase, and hanging all
    /// 113,850 flat off the phrase atom made <em>what is inside this one</em> unaskable. 27,326 sit
    /// entirely inside a longer one; the rest have only the phrase atom above them.
    /// </summary>
    [Fact]
    public void ASubphraseInsideALongerSubphraseHangsOffIt()
    {
        var subphrases = syntax.Of(WordGroupKind.Subphrase);

        subphrases.Count(draft => draft.Parent!.Kind == WordGroupKind.Subphrase).Should().Be(27326);
        subphrases.Count(draft => draft.Parent!.Kind == WordGroupKind.PhraseAtom).Should().Be(86524);
    }

    /// <summary>A parent is always longer than its child, so a chain of them cannot close.</summary>
    [Fact]
    public void NestingTerminates()
    {
        syntax.Drafts
            .Where(draft => draft.Parent?.Kind == draft.Kind)
            .Count(draft => draft.Parent!.Slots.Count <= draft.Slots.Count)
            .Should().Be(0);
    }

    /// <summary>
    /// <c>position</c> is meant to order a kind through the text, and for subphrases it did not:
    /// BHSA numbers them within their phrase atom, and 14,124 of 113,850 came out of textual order.
    /// Ordering a group's children by it has to give text order, which is the whole use of it.
    /// </summary>
    [Theory]
    [InlineData(WordGroupKind.Sentence)]
    [InlineData(WordGroupKind.Clause)]
    [InlineData(WordGroupKind.Phrase)]
    [InlineData(WordGroupKind.Subphrase)]
    [InlineData(WordGroupKind.HalfVerse)]
    public void PositionRunsThroughTheTextInOrder(WordGroupKind kind)
    {
        var ordered = syntax.Of(kind).OrderBy(draft => draft.Position).ToList();

        ordered.Select(draft => draft.Position).Should().Equal(Enumerable.Range(1, ordered.Count));
        ordered.Select(draft => draft.FirstSlot).Should().BeInAscendingOrder();
    }

    /// <summary>
    /// <c>mother.tf</c> is the other half of <c>rela</c>: a subphrase whose relation is <c>rec</c>
    /// is the rectum of what the mother names. All 182,269 edges are carried, and each points at
    /// the group or the word the source points at.
    /// </summary>
    [Fact]
    public void EveryMotherEdgeIsCarried()
    {
        var carried = syntax.Drafts
            .Where(draft => draft.Mother is not null || draft.MotherWordId is not null)
            .ToList();

        syntax.Bhsa.Mothers.Should().HaveCount(182269);
        carried.Should().HaveCount(182269);

        carried
            .Count(draft => (draft.Mother?.Node ?? (int)draft.MotherWordId!.Value) != syntax.Bhsa.Mothers[draft.Node])
            .Should().Be(0);
    }

    /// <summary>
    /// A stored relation with no mother is a relation to nothing, which is what made the feature
    /// unusable. BHSA writes the two together, and so does this.
    /// </summary>
    [Theory]
    [InlineData(WordGroupKind.Subphrase, 56925)]
    [InlineData(WordGroupKind.Clause, 20791)]
    [InlineData(WordGroupKind.Phrase, 612)]
    public void ARelationComesWithTheThingItIsARelationTo(WordGroupKind kind, int expected)
    {
        var related = syntax.Of(kind).Where(draft => Value(draft, "relation") is not null).ToList();

        related.Should().HaveCount(expected);
        related.Count(draft => draft.Mother is null && draft.MotherWordId is null).Should().Be(0);
    }

    private Dictionary<string, int> Spread(WordGroupKind kind, string key) => syntax.Of(kind)
        .Select(draft => Value(draft, key))
        .OfType<string>()
        .GroupBy(value => value)
        .ToDictionary(value => value.Key, value => value.Count());

    private static bool Inside(SyntaxLoader.GroupDraft draft, SyntaxLoader.GroupDraft parent)
    {
        var at = 0;
        foreach (var slot in draft.Slots)
        {
            while (at < parent.Slots.Count && parent.Slots[at] < slot)
            {
                at++;
            }

            if (at == parent.Slots.Count || parent.Slots[at] != slot)
            {
                return false;
            }
        }

        return true;
    }

    private static string? Value(SyntaxLoader.GroupDraft draft, string key)
    {
        if (draft.Features is not { } features)
        {
            return null;
        }

        using var document = JsonDocument.Parse(features);
        return document.RootElement.TryGetProperty(key, out var value) ? value.GetString() : null;
    }
}
