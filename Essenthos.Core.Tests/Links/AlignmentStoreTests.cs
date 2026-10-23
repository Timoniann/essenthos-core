using Essenthos.Core.Configuration;
using Essenthos.Core.Loading.Links;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Essenthos.Core.Tests.Links;

/// <summary>
/// The aligner's workspaces are the stored answers a rebuild reads instead of training. A temporary-file
/// cleanup empties a workspace and leaves its folders, and a run stopped half way leaves a file cut short;
/// neither may be read as an answer.
/// </summary>
public sealed class AlignmentStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "essenthos-align-test", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void TheStoreIsUnderResourcesUnlessConfiguredElsewhere()
    {
        var resources = Path.Combine(_folder, "Resources");
        Directory.CreateDirectory(resources);
        File.WriteAllText(Path.Combine(resources, "MANIFEST.json"), "{}");

        AlignmentStore.Read(Settings(("Dataset:ResourcesPath", resources)), _folder)
            .Should().Be(Path.Combine(resources, "Alignments"));
        AlignmentStore.Read(Settings(("Dataset:ResourcesPath", resources), ("Dataset:AlignmentsPath", "kept")), _folder)
            .Should().Be(Path.Combine(_folder, "kept"));
    }

    [Fact]
    public void AWorkspaceHoldingAnAnswerIsCopiedInWhole()
    {
        var former = Workspace("former", ("source.txt", "in the beginning"), ("alignment/pharaoh.txt", "0-0:0.9:0.9"));
        var store = Path.Combine(_folder, "store", "KJV-BHSA");

        AlignmentStore.Import(former, store).Should().BeTrue();

        File.ReadAllText(Path.Combine(store, "alignment", "pharaoh.txt")).Should().Be("0-0:0.9:0.9");
        File.ReadAllText(Path.Combine(former, "source.txt")).Should().Be("in the beginning");
        Directory.GetDirectories(Path.Combine(_folder, "store")).Should().ContainSingle();
    }

    [Fact]
    public void AnEmptiedWorkspaceIsNotCopiedInAndSaysItWasEmptied()
    {
        var former = Path.Combine(_folder, "former");
        Directory.CreateDirectory(Path.Combine(former, "alignment"));
        Directory.CreateDirectory(Path.Combine(former, "model"));
        var store = Path.Combine(_folder, "store", "ALM1911-BSB");

        AlignmentStore.Import(former, store).Should().BeFalse();

        Directory.Exists(store).Should().BeFalse();
        AlignmentPipeline.Refusal(former, AlignmentPipeline.Stored(Path.Combine(former, "alignment", "pharaoh.txt")))
            .Should().Contain("holds no file").And.Contain("emptied");
    }

    [Fact]
    public void AWorkspaceTheStoreHoldsIsNeverReplacedByTheFormerOne()
    {
        var former = Workspace("former", ("alignment/pharaoh.txt", "old"));
        var store = Workspace("store", ("alignment/pharaoh.txt", "retrained"));

        AlignmentStore.Import(former, store).Should().BeFalse();

        File.ReadAllText(Path.Combine(store, "alignment", "pharaoh.txt")).Should().Be("retrained");
    }

    [Fact]
    public void AnEmptyAnswerIsNotAnAnswer()
    {
        var workspace = Workspace("ws", ("alignment/pharaoh.txt", ""));
        var answer = Path.Combine(workspace, "alignment", "pharaoh.txt");

        AlignmentPipeline.Stored(answer).Should().Be(AlignmentPipeline.StoredAlignment.Empty);
        AlignmentPipeline.Stored(Path.Combine(workspace, "absent.txt")).Should().Be(AlignmentPipeline.StoredAlignment.None);
        AlignmentPipeline.Refusal(workspace, AlignmentPipeline.StoredAlignment.Empty)
            .Should().Contain(workspace).And.Contain("is empty");

        File.WriteAllText(answer, "0-0:0.9:0.9");
        AlignmentPipeline.Stored(answer).Should().Be(AlignmentPipeline.StoredAlignment.Whole);
    }

    [Fact]
    public void AFileIsReplacedWholeAndNothingIsLeftBesideIt()
    {
        var workspace = Workspace("ws", ("source.txt", "earlier"));
        var path = Path.Combine(workspace, "source.txt");

        AlignmentStore.WriteWhole(path, ["in the beginning", "and the earth"]);

        File.ReadAllLines(path).Should().Equal("in the beginning", "and the earth");
        Directory.GetFiles(workspace).Should().ContainSingle();
    }

    private string Workspace(string name, params (string Path, string Text)[] files)
    {
        var folder = Path.Combine(_folder, name);
        foreach (var (path, text) in files)
        {
            var file = Path.Combine(folder, path);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, text);
        }

        return folder;
    }

    private static IConfiguration Settings(params (string Key, string Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(setting => new KeyValuePair<string, string?>(setting.Key, setting.Value)))
            .Build();
}
