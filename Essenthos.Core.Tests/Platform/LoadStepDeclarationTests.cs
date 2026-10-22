using System.Diagnostics;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Marks;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using Xunit.Abstractions;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// What each step of the load says it reads, against what its code shows it reads. A step is skipped
/// when its code, its files and its tables are where they were, so a file it reads and does not declare
/// is a change the load would not see.
/// </summary>
public sealed class LoadStepDeclarationTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("load-declarations-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void EveryStepsCodeHasADigestAndTwoTextsReadByDifferentReadersHaveDifferentOnes()
    {
        var code = CodeDigest.ForTheForge();
        var timer = Stopwatch.StartNew();
        var digests = Steps().ToDictionary(step => step.Name, step => code.Of(step.Run).Digest);
        output.WriteLine($"{digests.Count} steps' code digested in {timer.Elapsed.TotalSeconds:F1} s");

        digests.Values.Should().OnlyContain(digest => digest.Length == 64);
        digests["GRCBRENT"].Should().NotBe(digests["SWETE"]);
        digests["TR1894"].Should().NotBe(digests["SBLGNT"]);
        digests["lexicon"].Should().NotBe(digests["syntax"]);
        CodeDigest.ForTheForge().Of(Steps().Single(step => step.Name == "SWETE").Run).Digest.Should().Be(digests["SWETE"]);

        var verify = code.Of(Steps().Single(step => step.Name == "verify").Run).Lines;
        output.WriteLine($"verify reaches {verify.Count} lines");
        File.WriteAllLines(Path.Combine(Path.GetTempPath(), "essenthos-verify-reach.txt"), verify);
    }

    /// <summary>The files compiled into the Forge that no step of the load reads, each with who does.</summary>
    private static readonly Dictionary<string, string> ReadOutsideTheLoad = new(StringComparer.Ordinal)
    {
        ["Essenthos.Core.Loading.Links.Evidentia.EvidentiaCombiner.json"] = "EVIDENTIA's runs and benchmarks",
        ["Essenthos.Core.Loading.Links.Evidentia.EvidentiaKeyCorrections.json"] = "EVIDENTIA's benchmark key",
    };

    /// <summary>
    /// A record file compiled into the Forge is read by name, and that name is in the code of the step
    /// that reads it, so a changed file runs that step. One no step's code names is either read some other
    /// way, which the digest cannot see, or not read by the load at all, and then is listed above.
    /// </summary>
    [Fact]
    public void EveryFileCompiledIntoTheForgeIsNamedByTheStepThatReadsIt()
    {
        var code = CodeDigest.ForTheForge();
        var named = Steps()
            .SelectMany(step => code.Of(step.Run).Resources)
            .ToHashSet(StringComparer.Ordinal);

        var unnamed = typeof(LoadMarks).Assembly.GetManifestResourceNames()
            .Where(resource => !named.Contains(resource) && !ReadOutsideTheLoad.ContainsKey(resource))
            .Order(StringComparer.Ordinal)
            .ToList();
        output.WriteLine(string.Join('\n', unnamed));
        unnamed.Should().BeEmpty();
    }

    /// <summary>
    /// A file a step declares and the corpus does not hold digests as missing, and stays missing whatever
    /// is changed under the name meant: a misspelled declaration is a change the load never sees.
    /// </summary>
    [Fact]
    [Trait(TestCategory.Name, TestCategory.Corpus)]
    public void EveryFileAStepDeclaresIsInTheCorpus()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var resources = TestResources.Folder("");
        var missing = Steps()
            .SelectMany(step => step.Files.Select(file => (step.Name, File: file)))
            .Where(declared => declared.File.ConfigurationKey is null || configuration[declared.File.ConfigurationKey] is null)
            .Where(declared =>
            {
                var path = declared.File.Built
                    ? Path.Combine(AppContext.BaseDirectory, declared.File.Path)
                    : Path.Combine(resources, declared.File.Path);
                return !File.Exists(path) && !Directory.Exists(path);
            })
            .Select(declared => $"{declared.Name}: {declared.File}")
            .Distinct()
            .ToList();

        output.WriteLine(string.Join('\n', missing));
        missing.Should().BeEmpty();
    }

    private IReadOnlyList<LoadStep> Steps()
    {
        var resources = Directory.CreateDirectory(Path.Combine(_root, "Resources")).FullName;
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Dataset:ResourcesPath"] = resources })
            .Build();
        return new DatasetLoader(
            new ServiceCollection().BuildServiceProvider(),
            new HostingEnvironment { ContentRootPath = _root },
            configuration,
            new DatasetStatus(),
            null!,
            NullLogger<DatasetLoader>.Instance).StepList();
    }
}
