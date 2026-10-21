using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>The descriptor clauses the own records' table of nations is read into.</summary>
public sealed class OwnRecordDescriptorTests
{
    /// <summary>
    /// The clauses the table of nations is read into: each son is his father's, and the people of his
    /// name descends from him and no longer from the father. The owner's own clause on Caphtorim moves
    /// to the son with his credit.
    /// </summary>
    [Fact]
    public void ThePeopleOfTheTableOfNationsDescendFromTheSonsTheTextNames()
    {
        var folder = Path.Combine(Solution(), "Resources", "Essenthos", "descriptors");
        var records = DescriptorFiles.Read(folder).Records.ToDictionary(r => r.Entity);

        foreach (var (people, son, father) in new[]
                 {
                     ("jebusites", "jebusite-son-of-canaan", "canaan"), ("hamathites", "hamathite-son-of-canaan", "canaan"),
                     ("anamim", "anamim-son-of-mizraim", "mizraim"), ("naphtuhim", "naphtuh", "mizraim"),
                     ("lydians", "lud", "mizraim"), ("caphthorim", "caphtorim-son-of-mizraim", "mizraim"),
                     ("kittim-h3794", "kittim-son-of-javan", "javan"), ("dodanim-h1721", "dodanim", "javan"),
                 })
        {
            records[people].Claims.Should().ContainSingle(c => c.Relation == "descendants-of" && c.Target == son);
            records[people].Claims.Should().NotContain(c => c.Target == father);
            records[son].Claims.Should().ContainSingle(c => c.Relation == "son-of" && c.Target == father);
        }

        records["caphtorim-son-of-mizraim"].Claims!.Single().DecidedBy.Should().StartWith("the project owner");
        records["jebusite-son-of-canaan"].Names!["rus"]["nominative"].Should().Be("Иевусей");
    }

    private static string Solution()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Essenthos.Core.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException("Essenthos.Core.sln is not above the test output.");
    }
}
