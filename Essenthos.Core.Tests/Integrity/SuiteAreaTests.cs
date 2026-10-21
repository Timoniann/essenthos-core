using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Integrity;

/// <summary>
/// The suite is run an area at a time by namespace (scripts/test-affected.ps1), so a test class
/// outside an area's namespace is a test no area run ever reaches, and a file whose folder and
/// namespace disagree is run for changes it has nothing to do with. The areas and which paths reach
/// them are written in scripts/test-areas.json.
/// </summary>
public sealed partial class SuiteAreaTests
{
    private const string Suite = "Essenthos.Core.Tests";

    private static readonly string[] NotAreas = ["Support", "Resources", "bin", "obj"];

    private static readonly Lazy<JsonElement> Map = new(() =>
        JsonDocument.Parse(File.ReadAllText(TestResources.Checkout("scripts", "test-areas.json"))).RootElement);

    private static IReadOnlyList<string> Areas =>
        [.. Map.Value.GetProperty("areas").EnumerateObject().Select(area => area.Name)];

    private static IEnumerable<Type> TestClasses =>
        typeof(SuiteAreaTests).Assembly.GetTypes().Where(type => type
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Any(method => method.IsDefined(typeof(FactAttribute), inherit: true)));

    [Fact]
    public void EveryTestClassIsInAnArea()
    {
        var areas = Areas.Select(area => $"{Suite}.{area}").ToHashSet(StringComparer.Ordinal);
        TestClasses.Where(type => !areas.Contains(type.Namespace!))
            .Select(type => type.FullName)
            .Should().BeEmpty("a test class goes in Essenthos.Core.Tests/<Area>/ under namespace {0}.<Area>", Suite);
    }

    [Fact]
    public void EveryFolderIsAnAreaAndEveryAreaAFolder()
    {
        var folders = Directory.GetDirectories(TestResources.Checkout(Suite))
            .Select(Path.GetFileName)
            .Where(name => !NotAreas.Contains(name) && !name!.StartsWith('.'));
        folders.Should().BeEquivalentTo(Areas);
    }

    [Fact]
    public void AFilesNamespaceIsItsFolder()
    {
        var wrong = new List<string>();
        foreach (var folder in Areas.Append("Support"))
        {
            var expected = folder == "Support" ? Suite : $"{Suite}.{folder}";
            foreach (var file in Directory.GetFiles(TestResources.Checkout(Suite, folder), "*.cs"))
            {
                var declared = NamespaceLine().Match(File.ReadAllText(file)).Groups[1].Value;
                if (declared != expected)
                {
                    wrong.Add($"{folder}/{Path.GetFileName(file)} declares '{declared}'");
                }
            }
        }

        wrong.Should().BeEmpty();
    }

    [Fact]
    public void NoTestFileSitsOutsideAFolder() =>
        Directory.GetFiles(TestResources.Checkout(Suite), "*.cs").Should().BeEmpty();

    [Fact]
    public void EveryRuleNamesAreasThatExist()
    {
        var known = Areas.Append("all").ToHashSet(StringComparer.Ordinal);
        Map.Value.GetProperty("rules").EnumerateArray()
            .SelectMany(rule => rule.GetProperty("areas").EnumerateArray()
                .Select(area => (Path: rule.GetProperty("path").GetString(), Area: area.GetString()!)))
            .Where(rule => !known.Contains(rule.Area))
            .Should().BeEmpty();
    }

    [GeneratedRegex(@"^namespace ([\w.]+);", RegexOptions.Multiline)]
    private static partial Regex NamespaceLine();
}
