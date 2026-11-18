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

    [Fact]
    public void The_prefixes_file_gives_every_prefix_number_bhsa_carries_an_entry_and_its_three_other_languages()
    {
        var store = new ProductStore(Path.Combine(Checkout(), "Resources"));

        var rows = TableProducts.PrefixRows(store, new HashSet<string>());

        rows.Where(r => r.IsEntry).Select(r => (r.Number, r.Lemma)).Should().Equal(
            ("H9000", "וְ"), ("H9003", "בְּ"), ("H9004", "כְּ"), ("H9005", "לְ"), ("H9008", "הֲ"), ("H9009", "הַ"));
        rows.GroupBy(r => r.Number).Should().AllSatisfy(number =>
            number.Select(r => r.Language).Should().BeEquivalentTo("eng", "ukr", "deu", "spa"));
        rows.Should().AllSatisfy(r => r.DetailedDefinition.Should().Contain("Open Hebrew Bible"));
    }

    [Fact]
    public void A_prefix_row_standing_in_for_an_entry_of_strongs_dictionary_is_refused()
    {
        var store = new ProductStore(_root);
        using (var writer = store.Create(TableProducts.Prefixes, ProductKind.Hand, TableProducts.PrefixRow.Schema, ProductHeader.ByHand, []))
        {
            writer.Write(new TableProducts.PrefixRow("H9005", "eng", "לְ", "lə", "prep", "to, for", "1) to", "test"));
            writer.Complete();
        }

        var read = () => TableProducts.PrefixRows(store, new HashSet<string> { "H9005" });

        read.Should().Throw<InvalidDataException>().WithMessage("*H9005 eng is an entry of Strong's dictionary*");
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
