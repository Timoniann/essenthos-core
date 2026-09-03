using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Essenthos.Core.TextusReceptus;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The other identifiers a text answers to.
///
/// One translation is spelled differently by everyone who serves it, and a reader pasting a
/// reference from other Bible software should reach the text rather than a 404. What has to hold
/// is narrow and absolute: an identifier reaches one text, the text answers with its own slug, and
/// nothing a client stores drifts away from that spelling.
/// </summary>
public sealed class TextAliasTests
{
    /// <summary>
    /// Every text the corpus loads, so the alias declarations are checked against the real slugs
    /// rather than against a list written beside them.
    /// </summary>
    private static TextDefinition[] Corpus =>
    [
        BhsaTextSource.Definition,
        NestleTextSource.Definition,
        SeptuagintTextSource.Definition(),
        TextusReceptusTextSource.Definition(Edition.Scrivener1894),
        TextusReceptusTextSource.Definition(Edition.Stephanus1550),
        ByzantineTextSource.Definition,
        BereanTextSource.Definition,
        .. Bible4uTextSource.Definitions.Values,
    ];

    private static IReadOnlyList<TextEntry> Loaded =>
        [.. Corpus.Select((definition, at) => new TextEntry(at + 1, definition.Slug, [1], false))];

    [Fact]
    public void TheSynodalAnswersToTheSpellingsOtherSoftwareUses()
    {
        TextAliases.Canonical("syno").Should().Be("rusv");
    }

    /// <summary>Identifiers are matched the way every other lookup here matches them.</summary>
    [Theory]
    [InlineData("SYNO")]
    [InlineData("Syno")]
    [InlineData("syno")]
    public void CaseDoesNotDecideWhichTextIsReached(string spelling) =>
        CanonIndex.Resolve(Loaded, spelling)!.Slug.Should().Be("rusv");

    /// <summary>
    /// The whole point of resolving rather than redirecting: two spellings of one identifier are
    /// one text, and it is named once, by its own slug.
    /// </summary>
    [Fact]
    public void TwoSpellingsOfOneTextAreOneText()
    {
        var texts = Loaded;

        var byAlias = CanonIndex.Resolve(texts, "SYNO");
        var bySlug = CanonIndex.Resolve(texts, "rusv");

        byAlias.Should().BeSameAs(bySlug);
        byAlias!.Slug.Should().Be("rusv");
    }

    [Fact]
    public void AnIdentifierNobodyPublishesReachesNothing() =>
        CanonIndex.Resolve(Loaded, "synodal").Should().BeNull();

    /// <summary>
    /// A text's own slug wins over any alias, so declaring one can never take a request away from
    /// the text that owns the identifier. Checked with an alias deliberately pointed at the wrong
    /// text, because the ordering is what rules the failure out and nothing else does.
    /// </summary>
    [Fact]
    public void ATextOwnSlugIsNeverShadowedByAnAlias()
    {
        IReadOnlyList<TextEntry> texts = [new TextEntry(1, "syno", [1], false), new TextEntry(2, "rusv", [1], false)];

        CanonIndex.Resolve(texts, "syno")!.Id.Should().Be(1);
    }

    /// <summary>
    /// No alias is an identifier some text already answers to. The invariant spans the aliases and
    /// the canonical slugs together, which is why it is checked here over the loaded corpus rather
    /// than left to a unique index on one column.
    /// </summary>
    [Fact]
    public void NoAliasIsAlreadySomeTextOwnSlug()
    {
        var slugs = Corpus.Select(definition => definition.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        TextAliases.All.SelectMany(declaration => declaration.Value)
            .Should().OnlyContain(alias => !slugs.Contains(alias));
    }

    [Fact]
    public void EveryAliasIsDeclaredForATextTheCorpusHolds()
    {
        var slugs = Corpus.Select(definition => definition.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase);

        TextAliases.All.Keys.Should().OnlyContain(slug => slugs.Contains(slug));
    }

    /// <summary>
    /// A text with no other name says nothing rather than sending an empty list, and one with other
    /// names lists them, so a client can offer them without knowing which texts have any.
    /// </summary>
    [Fact]
    public void OnlyATextWithOtherNamesCarriesThem()
    {
        TextAliases.Of("rusv").Should().Equal("syno");
        TextAliases.Of("kjv").Should().BeEmpty();
    }

    /// <summary>
    /// The corpus row says what else the text is called, because a client cannot offer a spelling
    /// it has never been told about — and it is answered with the text's own slug either way, so
    /// nothing a client stores back drifts onto an alias.
    /// </summary>
    [Fact]
    public void TheCorpusRowNamesTheOtherSpellingsAndItsOwnSlug()
    {
        var synodal = Corpora("rusv");

        synodal.Id.Should().Be("rusv");
        synodal.Aliases.Should().Equal("syno");
        Corpora("kjv").Aliases.Should().BeNull();
    }

    private static CorpusResponse Corpora(string slug) => Endpoints.Texts.Corpus(
        new Database.Entities.Text { Slug = slug, Name = slug, Language = "rus" },
        new CoverageResponse(1, 1, [1]),
        hasWordMapping: false);
}
