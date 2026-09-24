using Essenthos.Core.Loading.Links.Evidentia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// A lexeme standing twice in a verse gives the learned index the same evidence twice, so which
/// occurrence a word takes is decided by order alone.
/// </summary>
public class EvidentiaOccurrenceChoiceTests
{
    private static readonly EvidentiaAddress Genesis16 = new(1, 1, 6);
    private static readonly EvidentiaAddress Jonah115 = new(32, 1, 15);
    private static readonly EvidentiaAddress Luke1314 = new(42, 13, 14);
    private static readonly EvidentiaAddress Luke138 = new(42, 13, 8);

    [Fact]
    public void TwoOccurrencesOfOneLexemeAreNotCrossed()
    {
        var proposals = Propose(
            globally: true,
            [
                English(1, Genesis16, 1, "God"),
                English(2, Genesis16, 2, "said"),
                English(3, Genesis16, 3, "expanse"),
                English(4, Genesis16, 4, "waters"),
                English(5, Genesis16, 5, "waters"),
            ],
            [
                Hebrew(11, Genesis16, 1, "יֹּאמֶר", "H559"),
                Hebrew(12, Genesis16, 2, "אֱלֹהִים", "H430"),
                Hebrew(13, Genesis16, 3, "מַיִם", "H4325"),
                Hebrew(14, Genesis16, 4, "מָיִם", "H4325"),
                Hebrew(15, Genesis16, 5, "רָקִיעַ", "H7549"),
            ],
            ("God", "H430"), ("said", "H559"), ("expanse", "H7549"), ("waters", "H4325"));

        proposals.Should().Contain([(4L, 13L), (5L, 14L)], "the first waters is the first מַיִם");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnOccurrenceBetweenPlacedNeighboursIsPreferredToOneAtTheSameRelativePlace(bool globally)
    {
        var proposals = Propose(
            globally,
            [
                English(1, Jonah115, 1, "sailors"),
                English(2, Jonah115, 2, "threw"),
                English(3, Jonah115, 3, "sea"),
                English(4, Jonah115, 4, "raging"),
                English(5, Jonah115, 5, "calm"),
            ],
            [
                Hebrew(11, Jonah115, 1, "מַּלָּחִים", "H4419"),
                Hebrew(13, Jonah115, 3, "יָּם", "H3220"),
                Hebrew(15, Jonah115, 5, "יָּטִלוּ", "H2904"),
                Hebrew(16, Jonah115, 6, "יָּם", "H3220"),
                Hebrew(17, Jonah115, 7, "זַעְפֹּו", "H2197"),
                Hebrew(18, Jonah115, 8, "יַּעֲמֹד", "H5975"),
            ],
            ("sailors", "H4419"), ("threw", "H2904"), ("sea", "H3220"), ("raging", "H2197"), ("calm", "H5975"));

        proposals.Should().Contain((3L, 16L), "the sea stands between what threw and raging were placed on");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnAnchorOutOfOrderDoesNotPullItsNeighbours(bool globally)
    {
        var proposals = Propose(
            globally,
            [
                English(1, Luke1314, 2, "being"),
                English(2, Luke1314, 3, "because"),
                English(3, Luke1314, 4, "Jesus"),
                English(4, Luke1314, 8, "said"),
                English(5, Luke1314, 9, "multitude"),
            ],
            [
                Greek(12, Luke1314, 2, "ὅτι", "G3754"),
                Greek(14, Luke1314, 4, "Ἰησοῦς", "G2424"),
                Greek(17, Luke1314, 7, "ὅτι", "G3754"),
                Greek(18, Luke1314, 8, "ἔλεγεν", "G3004"),
                Greek(19, Luke1314, 9, "εἰσίν", "G1510"),
                Greek(20, Luke1314, 10, "ὄχλῳ", "G3793"),
            ],
            ("being", "G1510"), ("because", "G3754"), ("Jesus", "G2424"), ("said", "G3004"), ("multitude", "G3793"));

        proposals.Should().Contain((2L, 12L), "being, whose only rendering stands at the far end, is no guide to its neighbour");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheDiagonalRunsOverTheVerseNotOverTheWordsThatHaveARendering(bool globally)
    {
        var proposals = Propose(
            globally,
            [
                English(1, Luke138, 2, "answered"),
                English(2, Luke138, 9, "also"),
                English(3, Luke138, 16, "fertilize"),
            ],
            [
                Greek(11, Luke138, 3, "ἀποκριθεὶς", "G611"),
                Greek(12, Luke138, 9, "καὶ", "G2532"),
                Greek(13, Luke138, 10, "τοῦτο", "G3778"),
                Greek(14, Luke138, 12, "ἔτος", "G2094"),
                Greek(15, Luke138, 18, "καὶ", "G2532"),
                Greek(16, Luke138, 20, "κόπρια", "G2874"),
            ],
            ("answered", "G611"), ("also", "G2532"), ("fertilize", "G2874"), ("fertilize", "G3778"), ("fertilize", "G2094"));

        proposals.Should().Contain((2L, 12L), "also stands mid-verse, and so does the first καί");
    }

    private static IReadOnlyList<(long Source, long Target)> Propose(
        bool globally,
        IReadOnlyList<EvidentiaToken> source,
        IReadOnlyList<EvidentiaToken> target,
        params (string Form, string Strong)[] renderings)
    {
        var packs = new LanguagePackRegistry([new EnglishLanguagePack(), new OriginalLanguagePack()]);
        var preview = new EvidentiaPipeline(packs, [new Renderings(renderings)])
            .Preview(new EvidentiaRequest(source, target, AllowSourceStrongEvidence: false));
        var resolver = new EvidentiaKnownRenderingProposalResolver();
        var resolution = globally
            ? resolver.ResolveGlobally(preview.Candidates, EvidentiaKnownRenderingProposalResolver.Review)
            : resolver.Resolve(preview.Candidates, EvidentiaKnownRenderingProposalResolver.Review);
        return resolution.Proposals
            .Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id))
            .ToList();
    }

    private static EvidentiaToken English(long id, EvidentiaAddress address, int position, string surface) =>
        new(id, address, position, surface, "eng", PartOfSpeech: "NOUN");

    private static EvidentiaToken Hebrew(long id, EvidentiaAddress address, int position, string surface, string strong) =>
        new(id, address, position, surface, "hbo", StrongNumber: strong, PartOfSpeech: "subs");

    private static EvidentiaToken Greek(long id, EvidentiaAddress address, int position, string surface, string strong) =>
        new(id, address, position, surface, "grc", StrongNumber: strong, PartOfSpeech: "noun");

    private sealed class Renderings((string Form, string Strong)[] renderings) : IEvidentiaEvidenceSource
    {
        private const double Score = 0.65;

        public IEnumerable<EvidentiaEvidence> Find(EvidentiaAnalysis source, EvidentiaAnalysis target) =>
            renderings
                .Where(rendering => source.IsContentWord
                    && rendering.Form.Equals(source.Token.Surface, StringComparison.OrdinalIgnoreCase)
                    && rendering.Strong == target.Token.StrongNumber)
                .Select(_ => new EvidentiaEvidence(
                    EvidentiaEvidenceKind.KnownRendering, Score, "test",
                    new EvidentiaEvidenceSupport(20, 1, 0)));
    }
}
