using Essenthos.Core.Corpus;
﻿using Essenthos.Core.Database.Entities.Enums;
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
    /// rather than against a list written beside them. This one had fallen a text behind — the
    /// Samaritan Pentateuch was loading and nothing here covered it — which is why the list is no
    /// longer written twice.
    /// </summary>
    private static IReadOnlyList<TextDefinition> Corpus => TextCorpus.Definitions;

    private static IReadOnlyList<TextEntry> Loaded =>
        [.. Corpus.Select((definition, at) => new TextEntry(at + 1, definition.Slug, [1], false))];

    [Fact]
    public void TheSynodalAnswersToTheSpellingsOtherSoftwareUses()
    {
        TextAliases.Canonical("SYNO").Should().Be(Bible4uTextSource.Synodal);
    }

    /// <summary>Identifiers are matched the way every other lookup here matches them.</summary>
    [Theory]
    [InlineData("SYNO")]
    [InlineData("Syno")]
    [InlineData("syno")]
    public void CaseDoesNotDecideWhichTextIsReached(string spelling) =>
        CanonIndex.Resolve(Loaded, spelling)!.Slug.Should().Be(Bible4uTextSource.Synodal);

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
        byAlias!.Slug.Should().Be(Bible4uTextSource.Synodal);
    }

    /// <summary>
    /// The identifier this project itself used to publish the Ohienko Bible under. UKR is the ISO
    /// 639-2 code for the Ukrainian language and names no edition, so the text moved to the one the
    /// field uses — and the old spelling has to keep resolving, because it is in every URL and every
    /// saved reading position written before the move.
    /// </summary>
    [Theory]
    [InlineData("UKR")]
    [InlineData("ukr")]
    [InlineData("UKR1962")]
    [InlineData("ubio")]
    public void TheUkrainianAnswersToWhatItWasCalledBefore(string spelling) =>
        CanonIndex.Resolve(Loaded, spelling)!.Slug.Should().Be(Bible4uTextSource.Ohienko);

    /// <summary>
    /// Every identifier is spelled the way Bible software spells a version code, which is in
    /// capitals. It is checked rather than left to whoever adds the next text, because one lower
    /// case slug among eleven is the kind of thing nobody notices until it is in a published URL.
    /// </summary>
    [Fact]
    public void EveryIdentifierIsUpperCase()
    {
        Corpus.Select(definition => definition.Slug)
            .Should().OnlyContain(slug => slug == slug.ToUpperInvariant());

        TextAliases.All.SelectMany(declaration => declaration.Value)
            .Should().OnlyContain(alias => alias == alias.ToUpperInvariant());
    }

    /// <summary>
    /// Two texts may not have identifiers that differ only in case. The unique index on the column
    /// cannot say this — it compares byte for byte, so <c>KJV</c> and <c>kjv</c> would both be
    /// allowed to exist — and everything that resolves an identifier ignores case, so a pair like
    /// that would be one request reaching whichever of the two happened to be found first.
    /// </summary>
    [Fact]
    public void NoTwoTextsShareAnIdentifierBarItsCase() =>
        Corpus.Select(definition => definition.Slug).ToHashSet(StringComparer.OrdinalIgnoreCase)
            .Should().HaveSameCount(Corpus);

    [Fact]
    public void AnIdentifierNobodyPublishesReachesNothing() =>
        CanonIndex.Resolve(Loaded, "synodal").Should().BeNull();

    /// <summary>
    /// The corpus holds two Ukrainian Bibles now, and this is where confusing them would happen.
    /// UKRK is YouVersion's and bolls.life's code for the Kulish text, and UkrKulish is CrossWire's
    /// SWORD module for its New Testament; UKR is what Bible Gateway serves the Ohienko under, and
    /// it is one of the Ohienko's aliases here. An alias that crossed them would answer a reader who
    /// typed the right code with the wrong Ukrainian Bible, which is the one failure aliases can
    /// cause and the reason each one has to name a publisher.
    /// </summary>
    [Theory]
    [InlineData("UKRK", KulishTextSource.Slug)]
    [InlineData("ukrkulish", KulishTextSource.Slug)]
    [InlineData("UKR", Bible4uTextSource.Ohienko)]
    [InlineData("ubio", Bible4uTextSource.Ohienko)]
    public void EachUkrainianIdentifierReachesTheUkrainianBibleThatPublishesIt(
        string spelling, string slug) =>
        CanonIndex.Resolve(Loaded, spelling)!.Slug.Should().Be(slug);

    /// <summary>
    /// A text's own slug wins over any alias, so declaring one can never take a request away from
    /// the text that owns the identifier. Checked with an alias deliberately pointed at the wrong
    /// text, because the ordering is what rules the failure out and nothing else does.
    /// </summary>
    [Fact]
    public void ATextOwnSlugIsNeverShadowedByAnAlias()
    {
        IReadOnlyList<TextEntry> texts =
            [new TextEntry(1, "SYNO", [1], false), new TextEntry(2, Bible4uTextSource.Synodal, [1], false)];

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
        TextAliases.Of(Bible4uTextSource.Synodal).Should().Equal("SYNO");
        TextAliases.Of(Bible4uTextSource.KingJames).Should().BeEmpty();
    }

    /// <summary>
    /// The corpus row says what else the text is called, because a client cannot offer a spelling
    /// it has never been told about — and it is answered with the text's own slug either way, so
    /// nothing a client stores back drifts onto an alias.
    /// </summary>
    [Fact]
    public void TheCorpusRowNamesTheOtherSpellingsAndItsOwnSlug()
    {
        var synodal = Corpora(Bible4uTextSource.Synodal);

        synodal.Id.Should().Be(Bible4uTextSource.Synodal);
        synodal.Aliases.Should().Equal("SYNO");
        Corpora(Bible4uTextSource.KingJames).Aliases.Should().BeNull();
    }

    private static CorpusResponse Corpora(string slug) => Endpoints.Texts.Corpus(
        new Database.Entities.Text { Slug = slug, Name = slug, Language = "rus" },
        new CoverageResponse(1, 1, [1]),
        hasWordMapping: false);
}
