using Essenthos.Core.Build;
using Essenthos.Core.Configuration;
using Essenthos.Core.Products;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Products;

/// <summary>
/// A served database cloned for <c>build --apply</c>: the copy holds the original's rows and the store holds its served state
/// under the new name; a template somebody is connected to, one never served, or the live corpus is refused.
/// </summary>
public sealed class DatabaseCloneTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "essenthos-products", Guid.NewGuid().ToString("N"));

    private readonly string _from = $"essenthos_clone_{Guid.NewGuid():N}"[..40];

    private readonly string _to = $"essenthos_clone_{Guid.NewGuid():N}"[..40];

    /// <summary>The test server as the suite reaches it, its password from the user secrets.</summary>
    private static readonly string Server = DatabaseConnection.Read(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { [DatabaseConnection.ConnectionStringKey] = $"{WitnessDatabase.TestServer};Database=postgres" })
        .AddUserSecrets(typeof(DatabaseCloneTests).Assembly)
        .AddEnvironmentVariables()
        .Build());

    private static string Connection(string database) => new NpgsqlConnectionStringBuilder(Server) { Database = database, Pooling = false }.ConnectionString;

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_root, "bible4u"));
        File.WriteAllText(Path.Combine(_root, "bible4u", "kjv.xml"), "<kjv/>");
        await Execute("postgres", $"CREATE DATABASE \"{_from}\"");
        await Execute(_from, "CREATE TABLE pane (id int PRIMARY KEY, hash text); INSERT INTO pane VALUES (1, 'a'), (2, 'b')");
    }

    public async Task DisposeAsync()
    {
        foreach (var database in new[] { _from, _to })
        {
            await Execute("postgres", $"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)");
        }

        Directory.Delete(_root, true);
    }

    private void Serve(ProductStore store, string database)
    {
        using var writer = store.Create(ServedState.Product(database), ProductKind.Hand, ServedTable.Schema, "forge build --into", store.Inputs("bible4u/kjv.xml"),
            header => header.Notes.Add(new(ServedState.KeysNote, "135")));
        writer.Write(new ServedTable("pane", 2));
        writer.Complete();
        store.Save();
    }

    [Fact]
    public async Task The_copy_holds_the_rows_and_the_store_its_served_state()
    {
        var store = new ProductStore(_root);
        Serve(store, _from);

        await DatabaseClone.Run(Connection("postgres"), store, _from, _to, false, NullLogger.Instance, CancellationToken.None);

        (await Scalar(_to, "SELECT string_agg(hash, ',' ORDER BY id) FROM pane")).Should().Be("a,b");
        var (original, rows) = ServedState.Read(new ProductStore(_root), _from)!.Value;
        var (copy, copied) = ServedState.Read(new ProductStore(_root), _to)!.Value;
        copy.Inputs.Should().Equal(original.Inputs);
        copied.Should().Equal(rows);
        copy.Notes.Should().Contain(new KeyValuePair<string, string>(ServedState.KeysNote, "135"))
            .And.Contain(new KeyValuePair<string, string>(ServedState.ClonedNote, _from));
    }

    [Fact]
    public async Task An_existing_copy_is_replaced_only_when_asked()
    {
        var store = new ProductStore(_root);
        Serve(store, _from);
        await DatabaseClone.Run(Connection("postgres"), store, _from, _to, false, NullLogger.Instance, CancellationToken.None);
        await Execute(_to, "DELETE FROM pane");

        var again = () => DatabaseClone.Run(Connection("postgres"), store, _from, _to, false, NullLogger.Instance, CancellationToken.None);
        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already exists*");
        await DatabaseClone.Run(Connection("postgres"), store, _from, _to, true, NullLogger.Instance, CancellationToken.None);

        (await Scalar(_to, "SELECT count(*)::text FROM pane")).Should().Be("2");
    }

    [Fact]
    public async Task A_template_somebody_is_connected_to_is_refused()
    {
        var store = new ProductStore(_root);
        Serve(store, _from);
        await using var held = new NpgsqlConnection(Connection(_from));
        await held.OpenAsync();

        var clone = () => DatabaseClone.Run(Connection("postgres"), store, _from, _to, false, NullLogger.Instance, CancellationToken.None);

        await clone.Should().ThrowAsync<InvalidOperationException>().WithMessage("*connection*");
        (await Scalar("postgres", $"SELECT count(*)::text FROM pg_database WHERE datname = '{_to}'")).Should().Be("0");
    }

    [Fact]
    public async Task A_database_never_served_from_this_store_or_the_live_corpus_is_refused()
    {
        var store = new ProductStore(_root);

        var unserved = () => DatabaseClone.Run(Connection("postgres"), store, _from, _to, false, NullLogger.Instance, CancellationToken.None);
        var live = () => DatabaseClone.Run(Connection("postgres"), store, "essenthos_core", _to, false, NullLogger.Instance, CancellationToken.None);
        var intoLive = () => DatabaseClone.Run(Connection("postgres"), store, _from, "essenthos_core", false, NullLogger.Instance, CancellationToken.None);

        await unserved.Should().ThrowAsync<InvalidOperationException>().WithMessage("*No build/served*");
        await live.Should().ThrowAsync<InvalidOperationException>().WithMessage("*essenthos_core*");
        await intoLive.Should().ThrowAsync<InvalidOperationException>().WithMessage("*essenthos_core*");
    }

    private static async Task Execute(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(Connection(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string?> Scalar(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(Connection(database));
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (string?)await command.ExecuteScalarAsync();
    }
}
