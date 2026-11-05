using Essenthos.Core.Verbs;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>The one list of Forge verbs the dispatch and <c>forge help</c> read.</summary>
public sealed class ForgeVerbTests
{
    [Fact]
    public void EveryVerbHasANameOfItsOwnAndALineOfHelp()
    {
        ForgeVerbs.All.Should().OnlyHaveUniqueItems(verb => verb.Name);
        ForgeVerbs.All.Should().OnlyContain(verb => verb.Help.Length > 0 && !verb.Name.Contains(' '));
    }

    [Theory]
    [InlineData(new[] { "build" }, true)]
    [InlineData(new[] { "lexicon-translations" }, true)]
    [InlineData(new[] { "lexicon-translations", "--from", "lexicon" }, false)]
    [InlineData(new[] { "compose", "RUSV", "KJV" }, false)]
    [InlineData(new[] { "compose", "RUSV", "KJV", "BHSA", "--daughter" }, true)]
    [InlineData(new[] { "texts", "ids" }, false)]
    [InlineData(new[] { "texts", "ids", "all" }, true)]
    [InlineData(new[] { "evidentia-preview", "BSB", "BHSA", "1" }, false)]
    [InlineData(new[] { "evidentia-preview", "BSB", "BHSA", "1", "1" }, true)]
    public void AVerbTakesTheArgumentsItNeeds(string[] args, bool accepted) =>
        ForgeVerbs.Find(args[0])!.Accepts(args).Should().Be(accepted);

    [Fact]
    public void AVerbNobodyDeclaredIsNotFound() => ForgeVerbs.Find("no-such-verb").Should().BeNull();
}
