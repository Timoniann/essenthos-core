using Essenthos.Core.Build;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// The served tables made from files: the relations between texts people edit as <c>texts/relations</c> say what the Forge's
/// list says until that list is deleted, and a table is written once and then left as it is until its sources move.
/// </summary>
public sealed class TableProductsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    [Fact]
    public void The_relations_file_in_the_repository_states_what_the_forges_list_states()
    {
        var store = new ProductStore(Path.Combine(Checkout(), "Resources"));

        using var reader = store.Open(TableProducts.Relations, TableProducts.RelationRow.Schema);

        reader.ReadAll().Should().Equal(TextRelationLoader.All.Select(e => new TableProducts.RelationRow(e.From, e.To, e.Relation, e.Scope, e.Note, e.Source)));
    }

    [Fact]
    public void A_table_is_written_from_its_hand_file_and_left_alone_until_the_file_moves()
    {
        var store = new ProductStore(_root);

        var first = TableProducts.Write(store, new HashSet<string> { "text_relation" }, force: false).Single();
        var second = TableProducts.Write(store, new HashSet<string> { "text_relation" }, force: false).Single();

        first.Should().Match<TableProducts.Made>(m => !m.Current && m.Rows == TextRelationLoader.All.Count);
        second.Current.Should().BeTrue();
        store.Exists(TableProducts.Relations).Should().BeTrue("the hand file is seeded from the Forge's list where it is missing");
    }

    private static string Checkout()
    {
        for (var folder = new DirectoryInfo(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            if (File.Exists(Path.Combine(folder.FullName, "Essenthos.Core.sln")))
            {
                return folder.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No Essenthos.Core.sln above {AppContext.BaseDirectory}.");
    }
}
