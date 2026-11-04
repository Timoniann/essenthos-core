using Essenthos.Core.Conversion;
using Essenthos.Core.Products;
using FluentAssertions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// Strong numbers laid from another edition reach the served table only where the terms of that numbering allow:
/// a words product that says <c>strong-served no</c> loads without them, one of a text with its own tags with them,
/// and every laid text has a verdict, with its terms quoted, in <c>texts/strong-terms.tsv</c>.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class TextWordLoadTests(WitnessDatabase database) : IDisposable
{
    private readonly string _resources = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    private void Words(ProductStore store, string slug, string? served)
    {
        using var writer = store.Create(TextWord.ProductOf(slug), ProductKind.Machine, TextWord.Schema(slug), "test", [], header =>
        {
            if (served is not null)
            {
                header.Notes.Add(new(StrongTerms.Served, served));
            }
        });
        writer.WriteAll(Enumerable.Range(1, 3).Select(at => new TextWord(new WordKey(slug, new VerseKey(1, 1, 1), at), 100 + at,
            $"w{at}", " ", null, $"H{at}", null, null, $"w{at}", null, false, null, null, null)));
    }

    [Fact]
    public async Task ANumberingWhoseTermsDoNotAllowItIsLeftOutOfTheServedTable()
    {
        var store = new ProductStore(_resources);
        Words(store, "RUSV", "no: for the mapping only");
        Words(store, "KJV", null);
        store.Save();

        TextWordLoad.StrongServed(store, "RUSV").Should().BeFalse();
        TextWordLoad.StrongServed(store, "KJV").Should().BeTrue("a text's own tags carry no verdict and are served");

        const string table = "served_words";
        await using var connection = database.NewConnection();
        await connection.OpenAsync();
        await Execute(connection, $"DROP TABLE IF EXISTS {table}");
        await Execute(connection, $"""
            CREATE TABLE {table} (
                text text NOT NULL, own_book int NOT NULL, own_chapter int NOT NULL, own_chapter_label text NOT NULL, own_verse int NOT NULL,
                own_verse_label text NOT NULL, own_position int NOT NULL, id bigint, surface text NOT NULL, trailer text NOT NULL, lemma text,
                strong text, gloss text, morphology text, normalised text, graphical text, elided boolean NOT NULL, break text, supplied int,
                restored int, supplied_mark text, doubtful int, brackets int, subscription int)
            """);

        foreach (var slug in (string[])["RUSV", "KJV"])
        {
            (await TextWordLoad.Load(database.NewConnection().ConnectionString, store, slug, table, streams: 1)).Rows.Should().Be(3);
        }

        var loaded = new List<(string Text, string Surface, string? Strong)>();
        await using (var select = new NpgsqlCommand($"SELECT text, surface, strong FROM {table} ORDER BY text, own_position", connection))
        await using (var reader = await select.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                loaded.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
            }
        }

        loaded.Should().Equal(("KJV", "w1", "H1"), ("KJV", "w2", "H2"), ("KJV", "w3", "H3"), ("RUSV", "w1", null), ("RUSV", "w2", null), ("RUSV", "w3", null));
        await Execute(connection, $"DROP TABLE {table}");
    }

    [Fact]
    public void EveryTextWhoseNumbersAreLaidHasAVerdictWithItsTermsQuoted()
    {
        var terms = StrongTerms.Read(TestResources.Shipped("."));

        SideStrongNumbers.Texts.Should().BeEquivalentTo(["RUSV", "LSG1910", "JND2024", "SCH1951", "RLT2018", "CUV", "CUVS", "RV1909"]);
        terms.Keys.Should().BeEquivalentTo(SideStrongNumbers.Texts);
        terms.Values.Should().OnlyContain(term => term.Quote.Length > 0 && term.File.Length > 0 && term.Why.Length > 0);
        terms.Values.Where(term => term.Served).Should().BeEmpty("the owner's answer of 2026-11-04 serves none of these until their terms allow it");
    }

    private static async Task Execute(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public void Dispose()
    {
        if (Directory.Exists(_resources))
        {
            Directory.Delete(_resources, recursive: true);
        }
    }
}
