using Essenthos.Core.Names;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Names;

/// <summary>
/// Where an edition spells a man's name the way another man's name is spelled, and Strong numbers it so, the settled names
/// name the man the verse means: Matthew 1:10's Ἀμώς is King Amon, Luke 3:32's Σαλά is Salmon, Luke 3:33's Ἀδμίν is Admin
/// (numbered with Aminadab's G284), and the Ἠλί of Matthew 27:46 is the cry "my God", nobody's name.
/// </summary>
public sealed class NamesakeSpellingTests
{
    private const string SolutionFile = "Essenthos.Core.sln";

    [Theory]
    [InlineData("NESTLE1904", "40.1:10.10", "amon-2")]
    [InlineData("NESTLE1904", "40.1:10.11", "amon-2")]
    [InlineData("GEEZ81", "40.1:10.6", "amon-2")]
    [InlineData("GEEZ81", "40.1:10.7", "amon-2")]
    [InlineData("TR1894", "40.1:10.10", "amon-2")]
    [InlineData("TR1894", "40.1:10.11", "amon-2")]
    [InlineData("NESTLE1904", "42.3:25.4", "amos-2")]
    [InlineData("NESTLE1904", "42.3:32.8", "salmon")]
    [InlineData("NESTLE1904", "42.3:33.4", "admin")]
    [InlineData("NESTLE1904", "42.3:35.10", "shelah")]
    public void TheWordNamesTheManTheVerseMeansWhateverItsSpelling(string text, string own, string record)
    {
        PersonsOn(text, own).Should().Equal([record], $"{text} {own}");
    }

    [Theory]
    [InlineData("NESTLE1904", "40.27:46.12")]
    [InlineData("NESTLE1904", "40.27:46.13")]
    [InlineData("VULGCLEM", "40.27:46.10")]
    [InlineData("VULGCLEM", "40.27:46.11")]
    public void TheCryOfMatthewTwentySevenFortySixNamesNobody(string text, string own)
    {
        PersonsOn(text, own).Should().BeEmpty($"{text} {own}");
    }

    private static List<string> PersonsOn(string text, string own)
    {
        var store = new ProductStore(Path.Combine(Repository(), "Resources"));
        using var reader = store.Open(NameProducts.Settled(text), SettledName.Schema(text));
        return [.. reader.Rows().Where(row => row.Own.Own == own && row.Role == NameRoles.Name).Select(row => row.Record).Order(StringComparer.Ordinal)];
    }

    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, SolutionFile)))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException($"No {SolutionFile} above {AppContext.BaseDirectory}.");
    }
}
