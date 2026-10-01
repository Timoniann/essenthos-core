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
            "align", "compose", "names", "possessives", "unshare", "strong", "synodal-strong", "union-strong",
            "crosswire-strong", "ohb-cuv", "interlinear-join", "correct", "reload");

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
    [InlineData(new[] { "evidentia-preview", "BSB", "BHSA", "1" }, false)]
    [InlineData(new[] { "evidentia-preview", "BSB", "BHSA", "1", "1" }, true)]
    public void AVerbTakesTheArgumentsItNeeds(string[] args, bool accepted) =>
        ForgeVerbs.Find(args[0])!.Accepts(args).Should().Be(accepted);

    [Fact]
    public void AVerbNobodyDeclaredIsNotFound() => ForgeVerbs.Find("no-such-verb").Should().BeNull();
}
