using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Loading.Marks;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// What a step's code digest is taken over: the code it can run, and not code that merely shares an
/// assembly, a model or a numbering with it.
/// </summary>
public sealed partial class CodeDigestTests
{
    private static readonly CodeDigest Code = CodeDigest.ForTheForge();

    /// <summary>
    /// A record's constructor names the types of its collections and the model builder names every
    /// mapped type; neither runs their code. A step reaches a mapped type, and the lambda configuring it,
    /// only through code of its own that uses it.
    /// </summary>
    [Fact]
    public void AStepReachesTheMappedTypesItUsesAndTheirConfigurationOnly()
    {
        var lines = Code.Of(new Func<AppDbContext, int>(db => db.Entities.Count(entity => entity.Id > 0))).Lines;

        lines.Should().Contain(line => line.StartsWith("Essenthos.Core.Database.Entities.Entity::get_Id()"));
        lines.Should().Contain(line => line.Contains("EntityTypeBuilder`1<Essenthos.Core.Database.Entities.Entity>)"));
        lines.Should().NotContain(line => line.Contains("Entities.PlaceCandidate::") || line.Contains("EntityTypeBuilder`1<Essenthos.Core.Database.Entities.PlaceCandidate>"));
        lines.Should().NotContain(line => line.Contains("Essenthos.Core.Database.Entities.Word::"));

        var words = Code.Of(new Func<AppDbContext, int>(db => db.Words.Count(word => word.Id > 0))).Lines;
        words.Should().Contain(line => line.Contains("EntityTypeBuilder`1<Essenthos.Core.Database.Entities.Word>)"));
        words.Should().NotContain(line => line.Contains("EntityTypeBuilder`1<Essenthos.Core.Database.Entities.Entity>)"));
    }

    /// <summary>Using a mapped type outside the model reaches its own code and none of its relations'.</summary>
    [Fact]
    public void ARecordsCollectionsDoNotReachTheTypesInThem()
    {
        var lines = Code.Of(new Func<Type>(() => typeof(Entity))).Lines;

        lines.Should().Contain(line => line.StartsWith("Essenthos.Core.Database.Entities.Entity::.ctor()"));
        lines.Should().NotContain(line => line.Contains("Entities.PlaceCandidate::"));
        lines.Should().NotContain(line => line.StartsWith("type Essenthos.Core.Database.Entities.PlaceCandidate "));
    }

    /// <summary>
    /// The compiler numbers anonymous types across the assembly and names the regex generator's helpers by
    /// the hash of the folder built from; neither is part of what a step does.
    /// </summary>
    [Fact]
    public void NumbersAndPathsTheCompilerChoosesAreNotPartOfTheCode()
    {
        var lines = Code.Of(new Func<int>(() => new { Letters = Letters().Matches("abc x").Count }.Letters)).Lines;

        lines.Should().Contain(line => line.Contains("<>f__AnonymousType#`1::get_Letters()"));
        lines.Should().NotContain(line => FileHash().IsMatch(line));
        lines.Should().Contain(line => line.Contains("<RegexGenerator_g>F#__"));
        lines.Should().NotContain(line => line.Contains("__Utilities::.cctor()"));
    }

    [GeneratedRegex("[a-z]+")]
    private static partial Regex Letters();

    [GeneratedRegex(">F[0-9A-F]{64}__")]
    private static partial Regex FileHash();
}
