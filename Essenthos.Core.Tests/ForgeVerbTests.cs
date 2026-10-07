using Essenthos.Core.Verbs;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>The one list of Forge verbs the dispatch, <c>forge help</c> and the recipe read.</summary>
public sealed class ForgeVerbTests
{
    [Fact]
    public void EveryVerbHasANameOfItsOwnAndALineOfHelp()
    {
        ForgeVerbs.All.Should().OnlyHaveUniqueItems(verb => verb.Name);
        ForgeVerbs.All.Should().OnlyContain(verb => verb.Help.Length > 0 && !verb.Name.Contains(' '));
    }

    [Fact]
    public void TheVerbsThatWriteWhatTheLoadDoesNotAreTheOnesTheRecipeRecords() =>
        ForgeVerbs.All.Where(verb => verb.Records is not null).Select(verb => verb.Name).Should().BeEquivalentTo(
            "align", "compose", "names", "possessives", "unshare", "strong", "synodal-strong", "union-strong", "reina-valera-strong",
            "crosswire-strong", "ohb-cuv", "interlinear-join", "correct", "reload", "edition-boundaries");

    [Theory]
    [InlineData(new[] { "load" }, true)]
    [InlineData(new[] { "load", "--from", "encyclopedia" }, true)]
    [InlineData(new[] { "compose", "RUSV", "KJV" }, false)]
    [InlineData(new[] { "compose", "RUSV", "KJV", "BHSA", "--daughter" }, true)]
    [InlineData(new[] { "redraw", "berean" }, false)]
    [InlineData(new[] { "redraw", "berean", "BHSA" }, true)]
    [InlineData(new[] { "redraw", "berean", "BHSA", "NESTLE1904" }, false)]
    [InlineData(new[] { "reload", "GEEZ81" }, true)]
    [InlineData(new[] { "reload" }, false)]
    [InlineData(new[] { "kjv-greek", "TISCH" }, true)]
    [InlineData(new[] { "kjv-greek" }, false)]
    [InlineData(new[] { "kjv-greek", "TISCH", "WH1881" }, false)]
    [InlineData(new[] { "evidentia-preview", "BSB", "BHSA", "1" }, false)]
    [InlineData(new[] { "evidentia-preview", "BSB", "BHSA", "1", "1" }, true)]
    public void AVerbTakesTheArgumentsItNeeds(string[] args, bool accepted) =>
        ForgeVerbs.Find(args[0])!.Accepts(args).Should().Be(accepted);

    [Fact]
    public void AVerbNobodyDeclaredIsNotFound() => ForgeVerbs.Find("no-such-verb").Should().BeNull();

    /// <summary>
    /// Everything the recipe records changes links or words outside a load, and so says which texts the
    /// Strong pages count again after it; so do the verbs that write links the recipe does not replay.
    /// </summary>
    [Fact]
    public void EveryVerbThatChangesLinksSaysWhichTextsToCountAgain()
    {
        ForgeVerbs.All.Where(verb => verb.Records is not null).Should().OnlyContain(verb => verb.Relinks != null);
        ForgeVerbs.All.Where(verb => verb.Relinks is not null).Select(verb => verb.Name).Should().Contain(
            "redraw", "clearbible", "object-marker", "evidentia-apply", "evidentia-replay", "recipe");
        ForgeVerbs.All.Where(verb => verb.Relinks is not null).Select(verb => verb.Name).Should().NotContain(
            "load", "cards", "carry", "verify", "evidentia-approve");
    }

    [Theory]
    [InlineData(new[] { "align", "kjv", "bhsa" }, "KJV,BHSA")]
    [InlineData(new[] { "compose", "RUSV", "KJV,BSB", "BHSA" }, "RUSV,BHSA")]
    [InlineData(new[] { "compose", "RUSV", "KJV", "BHSA", "--dry-run" }, "")]
    [InlineData(new[] { "redraw", "berean", "BHSA" }, "BSB,BHSA")]
    [InlineData(new[] { "redraw", "clearbible", "BSB" }, "BSB")]
    [InlineData(new[] { "interlinear-join", "RUSV-IL" }, "")]
    [InlineData(new[] { "interlinear-join", "RUSV-IL", "--replace" }, "RUSV-IL")]
    [InlineData(new[] { "names", "KJV", "BHSA" }, "")]
    [InlineData(new[] { "names", "KJV", "BHSA", "--apply" }, "KJV,BHSA")]
    [InlineData(new[] { "cards" }, "")]
    [InlineData(new[] { "kjv-greek", "tisch" }, "KJV,TISCH")]
    public void ARunCountsAgainTheTextsWhoseLinksItChanged(string[] args, string texts) =>
        string.Join(',', ForgeVerbs.Find(args[0])!.Changed(args).Texts!).Should().Be(texts);

    [Fact]
    public void ARunThatCannotNameItsTextsCountsThemAll()
    {
        ForgeVerbs.Find("crosswire-strong")!.Changed(["crosswire-strong"]).Texts.Should().BeNull();
        ForgeVerbs.Find("evidentia-apply")!.Changed(["evidentia-apply", "7", "--write"]).EvidentiaRun.Should().Be(7);
        ForgeVerbs.Find("evidentia-apply")!.Changed(["evidentia-apply", "7"]).Nothing.Should().BeTrue();
    }

    [Fact]
    public async Task TheKingJamesVerbRefusesOtherTextsBeforeResolvingAnyLoader()
    {
        var verb = ForgeVerbs.Find("kjv-greek")!;
        verb.Records.Should().BeNull();
        var run = new ForgeRun(null!, Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance, "", "");
        var act = () => verb.Run(run, ["kjv-greek", "BHSA"]);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not a King James Greek witness*");
    }
}
