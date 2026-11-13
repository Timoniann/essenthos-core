using System.Text;
using Essenthos.Core.Publishing;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// A release published as its difference from the one a server's copy holds. What these hold: a row as a
/// dump writes it hashes as the server hashes the row in its table, for every kind of value the corpus
/// stores; the script turns a copy of the base into exactly the release, foreign keys and triggers aside;
/// and it commits nothing when the copy is not the base, or when any table it changed, or with
/// <c>verify_all</c> any table at all, does not come out as the release holds it.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class ReleaseDifferenceTests : IAsyncLifetime
{
    private static readonly string[] Tables = ["corpus_release", "t_word", "t_pair", "t_same"];

    private readonly WitnessDatabase _database;
    private readonly string _folder = Directory.CreateTempSubdirectory("release-difference-").FullName;

    public ReleaseDifferenceTests(WitnessDatabase database) => _database = database;

    public async Task InitializeAsync()
    {
        await Execute(
            "DROP SCHEMA IF EXISTS r_b CASCADE; DROP SCHEMA IF EXISTS r_n CASCADE; " +
            "DROP TABLE IF EXISTS t_word, t_pair, t_same; DROP SEQUENCE IF EXISTS t_word_seq; DELETE FROM corpus_release; " +
            "CREATE TABLE t_word (id bigint PRIMARY KEY, surface text, tags text[], data jsonb, at timestamptz, weight double precision, " +
            "flag boolean, amount numeric, day date, raw bytea, \"position\" int); " +
            "CREATE TABLE t_pair (link_id bigint, word_id bigint, side text, PRIMARY KEY (link_id, word_id, side)); " +
            "CREATE TABLE t_same (id int PRIMARY KEY, note text); " +
            "CREATE SEQUENCE t_word_seq; " +
            "CREATE SCHEMA r_b; CREATE SCHEMA r_n;");
        foreach (var release in new[] { "r_b", "r_n" })
        {
            foreach (var table in Tables)
            {
                await Execute($"CREATE TABLE {release}.{table} (LIKE public.{table} INCLUDING ALL)");
            }
        }

        await Execute(
            Label("r_b", "b") + Label("r_n", "n") +
            "INSERT INTO r_b.t_word VALUES " +
            "(1, 'plain', '{a,b}', '{\"a\": [1, \"b\"]}', '2026-11-13 10:00:00+02', 0.1, true, 1.50, '2026-11-13', '\\x00ff', 1), " +
            "(2, '', NULL, NULL, NULL, 1e-300, false, NULL, NULL, NULL, 2), " +
            "(3, 'a,b \"quoted\" (x) back\\slash', '{\"y z\",NULL}', '\"text\"', '0001-01-01 00:00:00+00', 'NaN', NULL, -0.0001, '1999-12-31', '', 3), " +
            "(4, E'tab\\there\\nnewline\\rreturn', '{}', '[]', '2026-01-01 00:00:00.123456+00', 'Infinity', true, 12345678901234567890.5, NULL, '\\x5c', 4), " +
            "(5, ' лідер (рос.) ', '{\"паралель\"}', '{\"ключ\": \"значення, з комою\"}', NULL, -2.5, NULL, 0, NULL, NULL, NULL); " +
            "INSERT INTO r_n.t_word SELECT * FROM r_b.t_word WHERE id NOT IN (2, 3); " +
            "INSERT INTO r_n.t_word SELECT id, surface || ' changed', tags, data, at, weight, flag, amount, day, raw, \"position\" FROM r_b.t_word WHERE id = 3; " +
            "INSERT INTO r_n.t_word VALUES (6, 'new', NULL, NULL, NULL, NULL, NULL, NULL, NULL, NULL, 6); " +
            "INSERT INTO r_b.t_pair VALUES (1, 1, 'from'), (1, 2, 'to'), (2, 3, 'from'); " +
            "INSERT INTO r_n.t_pair VALUES (1, 1, 'from'), (2, 3, 'from'), (2, 6, 'to'); " +
            "INSERT INTO r_b.t_same VALUES (1, 'kept'), (2, NULL); INSERT INTO r_n.t_same SELECT * FROM r_b.t_same;");
        foreach (var table in Tables)
        {
            await Execute($"INSERT INTO public.{table} SELECT * FROM r_b.{table}");
        }
    }

    public async Task DisposeAsync()
    {
        await Execute("DROP SCHEMA IF EXISTS r_b CASCADE; DROP SCHEMA IF EXISTS r_n CASCADE; DROP TABLE IF EXISTS t_word, t_pair, t_same; " +
                      "DROP SEQUENCE IF EXISTS t_word_seq; DELETE FROM corpus_release");
        Directory.Delete(_folder, recursive: true);
    }

    [Theory]
    [InlineData("1\tplain\t\\N", "(1,plain,)")]
    [InlineData("1\t\t\\N", "(1,\"\",)")]
    [InlineData("a,b\t(x)\ta\"b", "(\"a,b\",\"(x)\",\"a\"\"b\")")]
    [InlineData("back\\\\slash\ttab\\there\tnew\\nline", "(\"back\\\\slash\",\"tab\there\",\"new\nline\")")]
    [InlineData("\\101\\x42\\q", "(ABq)")]
    [InlineData("{a,\"b c\"}", "(\"{a,\"\"b c\"\"}\")")]
    public void ACopyLineReadsAsTheRecordPostgresWouldWrite(string line, string record) =>
        new CopyRows().Text(Encoding.UTF8.GetBytes(line)).Should().Be(record);

    [Fact]
    public async Task ARowHashesHereAsTheServerHashesItForEveryKindOfValue()
    {
        foreach (var table in Tables)
        {
            var here = default(TableSum);
            var rows = new CopyRows();
            var columns = await Columns(table);
            await foreach (var line in Export($"COPY r_b.{table} ({columns}) TO STDOUT"))
            {
                here = here.Add(rows.Hash(Encoding.UTF8.GetBytes(line)));
            }

            (await ServerSum($"r_b.{table}", columns)).Should().Be(here, $"{table} is summed alike on both sides");
        }
    }

    [Fact]
    public async Task TheScriptTurnsACopyOfTheBaseIntoTheReleaseRowForRow()
    {
        var record = await Make();

        record.Changed.Select(table => (table.Table, table.Gone, table.Added)).Should().BeEquivalentTo(
            [("corpus_release", 1L, 1L), ("t_word", 2L, 2L), ("t_pair", 1L, 1L)]);
        record.Unchanged.Should().Be(1);

        var printed = await Apply(record, everyTable: false);

        printed.Should().Contain($"{ReleaseDifference.AppliedMark} n");
        foreach (var table in Tables)
        {
            (await Scalar($"SELECT count(*) FROM ((TABLE public.{table} EXCEPT ALL TABLE r_n.{table}) UNION ALL (TABLE r_n.{table} EXCEPT ALL TABLE public.{table})) d"))
                .Should().Be(0L, $"{table} holds what the release holds");
        }

        (await Scalar("SELECT last_value FROM t_word_seq")).Should().Be(42L);
    }

    [Fact]
    public async Task ACopyThatIsNotTheBaseIsRefusedAndLeftAsItWas()
    {
        var record = await Make();
        await Execute("UPDATE public.corpus_release SET name = 'x'");

        var apply = () => Apply(record, everyTable: false);

        (await apply.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("holds release x").And.Contain("release b");
        (await Scalar("SELECT count(*) FROM public.t_word WHERE id = 2")).Should().Be(1L);
    }

    [Fact]
    public async Task AChangedTableThatDoesNotComeOutAsTheReleaseRollsEverythingBack()
    {
        var record = await Make();
        await Execute("UPDATE public.t_word SET surface = 'tampered' WHERE id = 4");

        var apply = () => Apply(record, everyTable: false);

        (await apply.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("public.t_word holds");
        (await Scalar("SELECT count(*) FROM public.corpus_release WHERE name = 'b'")).Should().Be(1L);
        (await Scalar("SELECT count(*) FROM public.t_word WHERE id = 6")).Should().Be(0L);
    }

    [Fact]
    public async Task AnUnchangedTableIsSummedOnlyWhenEveryTableIsAskedFor()
    {
        var record = await Make();
        await Execute("UPDATE public.t_same SET note = 'drifted' WHERE id = 1");

        var everyTable = () => Apply(record, everyTable: true);
        (await everyTable.Should().ThrowAsync<PostgresException>()).Which.MessageText.Should().Contain("public.t_same holds");
        (await Scalar("SELECT count(*) FROM public.corpus_release WHERE name = 'b'")).Should().Be(1L);

        await Apply(record, everyTable: false);
        (await Scalar("SELECT count(*) FROM public.corpus_release WHERE name = 'n'")).Should().Be(1L);
    }

    [Fact]
    public async Task DifferencesInTheFolderAreTakenAwayOnceTheReleaseIsLive()
    {
        var plan = ReleaseUploads.Choose(
            ["20261110a.dump", "20261112a.dump", "20261113a.from-20261112a.sql.gz", "20261114a.from-20261113a.sql.gz", "20261112a.from-20261110a.sql.gz"],
            published: "20261113a", serving: "20261113a", previous: "20261112a", uploaded: "20261113a.from-20261112a.sql.gz");

        plan.Refusal.Should().BeNull();
        plan.Remove.Should().Equal("20261110a.dump", "20261112a.from-20261110a.sql.gz", "20261113a.from-20261112a.sql.gz");
        plan.Keep.Select(kept => kept.File).Should().Equal("20261112a.dump", "20261114a.from-20261113a.sql.gz");
    }

    [Fact]
    public void ADifferenceNeedsACopyBesideTheLiveCorpus()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Publish:Targets:t:Local"] = "true",
            ["Publish:Targets:t:DataRoot"] = _folder,
            ["Publish:Targets:t:Container"] = "c",
            ["Publish:Targets:t:Database"] = "corpus",
            ["Publish:Targets:t:Reader"] = "reader",
            ["Publish:Targets:t:DatabasePort"] = "5432",
            ["Publish:Targets:t:ApiContainer"] = "api",
            ["Publish:Targets:t:Copies"] = "1",
            ["Publish:Targets:t:ByDifference"] = "true",
        }).Build();

        var read = () => ReleaseTarget.Read(configuration, "t", _folder);

        read.Should().Throw<InvalidOperationException>().WithMessage("*ByDifference*Copies*");
    }

    private Task<DifferenceRecord> Make() =>
        ReleaseDifference.Write(
            new SchemaRows(this), _folder, "b", "n", Tables,
            new Dictionary<string, IReadOnlyList<string>>
            {
                ["corpus_release"] = ["id"], ["t_word"] = ["id"], ["t_pair"] = ["link_id", "word_id", "side"], ["t_same"] = ["id"],
            },
            ["SELECT pg_catalog.setval('public.t_word_seq', 42, true);"], parallel: 2, CancellationToken.None);

    /// <summary>
    /// Runs the script as psql would, over one connection: statements to the line ending in a semicolon,
    /// COPY blocks to <c>\.</c>, <c>\if :{?verify_all}</c> by whether every table was asked for. What it
    /// printed comes back; a failing statement throws, and the connection's end rolls the transaction back.
    /// </summary>
    private async Task<string> Apply(DifferenceRecord record, bool everyTable)
    {
        var printed = new StringBuilder();
        await using var connection = new NpgsqlConnection(_database.NewConnection().ConnectionString + ";Pooling=false");
        await connection.OpenAsync();
        await using var file = File.OpenRead(Path.Combine(_folder, record.File));
        await using var zip = new System.IO.Compression.GZipStream(file, System.IO.Compression.CompressionMode.Decompress);
        using var script = new StreamReader(zip, Encoding.UTF8);
        var statement = new StringBuilder();
        var skipping = false;
        while (await script.ReadLineAsync() is { } line)
        {
            if (line == "\\endif")
            {
                skipping = false;
            }
            else if (skipping || line.StartsWith("--", StringComparison.Ordinal) || line.StartsWith("\\set ", StringComparison.Ordinal))
            {
            }
            else if (line == "\\if :{?verify_all}")
            {
                skipping = !everyTable;
            }
            else if (line.StartsWith("\\echo ", StringComparison.Ordinal))
            {
                printed.AppendLine(line[6..]);
            }
            else if (line.StartsWith("COPY ", StringComparison.Ordinal) && line.EndsWith(" FROM stdin;", StringComparison.Ordinal))
            {
                await using var writer = await connection.BeginTextImportAsync(line[..^1]);
                while (await script.ReadLineAsync() is { } row && row != "\\.")
                {
                    await writer.WriteAsync(row + "\n");
                }
            }
            else
            {
                statement.AppendLine(line);
                if (line.EndsWith(';'))
                {
                    await using var command = new NpgsqlCommand(statement.ToString(), connection);
                    await command.ExecuteNonQueryAsync();
                    statement.Clear();
                }
            }
        }

        return printed.ToString();
    }

    private async Task<TableSum> ServerSum(string table, string columns)
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var setting in ReleaseDifference.Settings)
        {
            await using var set = new NpgsqlCommand(setting, connection, transaction);
            await set.ExecuteNonQueryAsync();
        }

        await using var command = new NpgsqlCommand(TableSum.Sql(table, columns), connection, transaction);
        await using var reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();
        return new TableSum(reader.GetInt64(0), (Int128)reader.GetDecimal(1), (Int128)reader.GetDecimal(2));
    }

    /// <summary>A table's columns as pg_dump's COPY header names them.</summary>
    private async Task<string> Columns(string table)
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT string_agg(quote_ident(attname), ', ' ORDER BY attnum) FROM pg_attribute WHERE attrelid = @t::regclass AND attnum > 0 AND NOT attisdropped",
            connection);
        command.Parameters.AddWithValue("t", $"public.{table}");
        return (string)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>COPY text as pg_dump's session writes it: ISO dates, Postgres intervals, exact floats, UTC.</summary>
    private async IAsyncEnumerable<string> Export(string copy)
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using (var set = new NpgsqlCommand(
                         "SET DateStyle = 'ISO'; SET IntervalStyle = 'postgres'; SET extra_float_digits = 3; SET TimeZone = 'UTC'", connection))
        {
            await set.ExecuteNonQueryAsync();
        }

        using var reader = await connection.BeginTextExportAsync(copy);
        while (await reader.ReadLineAsync() is { } line)
        {
            yield return line;
        }
    }

    private static string Label(string schema, string name) =>
        $"INSERT INTO {schema}.corpus_release (id, name, built_at, manifest_sha, migration_head, forge_version, original_words, translations, " +
        $"strong_entries, people, places, peoples) VALUES (1, '{name}', '2026-11-13 12:00:00+00', 'sha', 'head', 'forge', 0, 0, 0, 0, 0, 0); ";

    private async Task Execute(string sql)
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> Scalar(string sql)
    {
        await using var connection = _database.NewConnection();
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    /// <summary>The releases <c>b</c> and <c>n</c> as the schemas <c>r_b</c> and <c>r_n</c>, read as a dump reads them.</summary>
    private sealed class SchemaRows(ReleaseDifferenceTests tests) : IReleaseRows
    {
        public async Task Rows(string release, string table, Action<string, string> header, RowAction row, CancellationToken cancellationToken)
        {
            var columns = await tests.Columns(table);
            header($"public.{table}", columns);
            await foreach (var line in tests.Export($"COPY r_{release}.{table} ({columns}) TO STDOUT").WithCancellation(cancellationToken))
            {
                row(Encoding.UTF8.GetBytes(line));
            }
        }
    }
}
