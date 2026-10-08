using System.Text.Json;
using System.Text.RegularExpressions;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The lines a reader sees under a register bearer's name say who he is, not what this corpus
/// could not find of him and not which other bearer of the name he is not.
/// </summary>
public sealed class RegisterLinesTests
{
    private static readonly Regex Process = new(
        @"(attest|supplied|given (verses|occurrences)|in this corpus|this (verse )?set|these verses|occurrence set|no verses? (here|given|cited)|no surviving|name-spelling flag|\bitem \d|\bbearer \d|\blexicon\b|enumeration)",
        RegexOptions.IgnoreCase);

    [Fact]
    public void NoLineThatReplacesTheRegistersSaysAnythingAboutOurOwnProcess()
    {
        RegisterLines.All.Should().NotBeEmpty();
        RegisterLines.All.Should().OnlyContain(line => !Process.IsMatch(line.Line));
        RegisterLines.All.Should().OnlyContain(line => line.Line.Length > 0 && line.Line != line.Was);
    }

    [Fact]
    public void ABearerIsCorrectedOnce()
    {
        RegisterLines.All.Select(line => line.SourceId).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void ADescriptionNoLineReplacesIsReturnedAsItIs()
    {
        RegisterLines.Of("essenthos:zimri1", "Fifth king of Israel").Should().Be("Fifth king of Israel");
        RegisterLines.Of("essenthos:zimri5", "a description the file was not written for").Should().Be("a description the file was not written for");
    }

    /// <summary>
    /// Every correction was written for the register's sentence as it stands in the register files,
    /// so a register read again with other words is not given a line made for the old ones.
    /// </summary>
    [Fact]
    [Trait(TestCategory.Name, TestCategory.Corpus)]
    public void EveryCorrectionIsForADescriptionTheRegisterFilesHold()
    {
        var held = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(TestResources.Folder(System.IO.Path.Combine("Essenthos", "persons")), "register-*.jsonl"))
        {
            foreach (var row in File.ReadLines(file).Where(row => row.Length > 0))
            {
                using var json = JsonDocument.Parse(row);
                var key = json.RootElement.GetProperty("key").GetString()!;
                held[PersonRegisterLoader.SourceIdOf(key)] = json.RootElement.GetProperty("description").GetString();
            }
        }

        foreach (var line in RegisterLines.All)
        {
            held.Should().ContainKey(line.SourceId);
            held[line.SourceId].Should().Be(line.Was);
        }
    }
}
