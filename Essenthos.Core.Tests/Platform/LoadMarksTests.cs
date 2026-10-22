using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Marks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// A load step skipped when nothing it reads has moved: its code, its files and the tables its own
/// statements named, as they stood when it last ran to the end. Three steps over a scratch database:
/// one writes a provenance from a file, one copies the provenances into Strong entries, one only reads
/// the provenances.
///
/// <para>
/// A step that changed a table it reads finds it changed the next time, and runs once more, as a whole
/// load would: that run writes nothing, and the load after it is skipped. <see cref="Settle"/> loads
/// until nothing runs.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LoadMarksTests : IDisposable
{
    private const string Source = "source";
    private const string Copier = "copier";
    private const string Reader = "reader";

    private readonly string _connectionString;
    private readonly string _root = Directory.CreateTempSubdirectory("load-marks-").FullName;
    private readonly string _resources;
    private readonly AppDbContext _db;
    private bool _failOnce;
    private int _readerVersion = 1;

    public LoadMarksTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _connectionString = _db.Database.GetConnectionString()!;
        _resources = Directory.CreateDirectory(Path.Combine(_root, "Resources")).FullName;
        File.WriteAllText(Path.Combine(_resources, "input.txt"), "first\n");

        // The class's tests share its database, one after another.
        Execute("TRUNCATE load_step_mark, table_fingerprint, provenance, strong_entry CASCADE").GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AStepIsSkippedOnceNothingItReadsMoves()
    {
        Ran(await Load()).Should().Equal(Source, Copier, Reader);

        var again = await Load();
        Ran(again).Should().Equal(Source, Copier);
        again.Where(outcome => outcome.Ran).Should().OnlyContain(outcome => outcome.Moved.Count == 0);

        Ran(await Load()).Should().BeEmpty();
        (await Scalar("SELECT count(*) FROM strong_entry")).Should().Be(1);
    }

    [Fact]
    public async Task AChangedFileRunsItsStepAndTheStepsThatReadWhatItChanged()
    {
        await Settle();
        File.WriteAllText(Path.Combine(_resources, "input.txt"), "second\n");

        var outcomes = await Load();

        Ran(outcomes).Should().Equal(Source, Copier, Reader);
        outcomes.Single(outcome => outcome.Step == Source).Why.Should().Contain("file input.txt");
        outcomes.Single(outcome => outcome.Step == Copier).Why.Should().Contain("table provenance");
        (await Settle()).Should().BeLessThanOrEqualTo(2);
    }

    /// <summary>
    /// The source reads only the file's first line, so a second line runs it and it writes what is
    /// already there: the steps after it find the provenances as they were and are skipped.
    /// </summary>
    [Fact]
    public async Task AStepThatWritesWhatWasAlreadyThereStopsTheStepsAfterItFromRunning()
    {
        await Settle();
        File.AppendAllText(Path.Combine(_resources, "input.txt"), "a line nothing reads\n");

        var outcomes = await Load();

        Ran(outcomes).Should().Equal(Source);
        outcomes.Single(outcome => outcome.Step == Source).Moved.Should().BeEmpty();
    }

    [Fact]
    public async Task ATableWrittenOutsideTheLoadRunsTheStepsThatReadItAndNoOther()
    {
        await Settle();
        await Execute("INSERT INTO strong_entry (strong_number) VALUES ('H9999')");

        var outcomes = await Load();

        Ran(outcomes).Should().Equal(Copier);
        outcomes.Single(outcome => outcome.Ran).Why.Should().Contain("table strong_entry");
        (await Scalar("SELECT count(*) FROM strong_entry WHERE strong_number = 'H9999'")).Should().Be(0);
    }

    [Fact]
    public async Task AWriteThatWasRolledBackIsNotAWrite()
    {
        await Settle();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using var insert = new NpgsqlCommand("INSERT INTO provenance (source) VALUES ('never')", connection, transaction);
            await insert.ExecuteNonQueryAsync();
            await transaction.RollbackAsync();
        }

        Ran(await Load()).Should().BeEmpty();
    }

    [Fact]
    public async Task ARaisedVersionRunsItsStep()
    {
        await Settle();
        _readerVersion = 2;

        var outcomes = await Load();

        Ran(outcomes).Should().Equal(Reader);
        outcomes.Single(outcome => outcome.Ran).Why.Should().Contain("its version (1 → 2)");
    }

    [Fact]
    public async Task EveryStepRunsWhenEveryStepIsAskedFor()
    {
        await Settle();

        Ran(await Load(everything: true)).Should().Equal(Source, Copier, Reader);
        Ran(await Load()).Should().BeEmpty();
    }

    [Fact]
    public async Task AStepThatFailedRunsAgainEvenWithNothingMoved()
    {
        await Settle();
        var failing = () => Load(everything: true, failCopier: true);
        await failing.Should().ThrowAsync<InvalidOperationException>();

        Ran(await Load()).Should().Equal(Copier);
    }

    [Fact]
    public async Task ATableEmptiedRunsTheStepsThatReadIt()
    {
        await Settle();
        await Execute("TRUNCATE strong_entry CASCADE");

        Ran(await Load()).Should().Equal(Copier);
        (await Scalar("SELECT count(*) FROM strong_entry")).Should().Be(1);
    }

    [Fact]
    public async Task AFailedStepOfTheLoadIsNamedAndTheStepsBeforeTheOneNamedAreNotRun()
    {
        var status = new DatasetStatus();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Dataset:ResourcesPath"] = _resources,
                [Essenthos.Core.Configuration.DatabaseConnection.ConnectionStringKey] = _connectionString,
            })
            .Build();

        // No loader is registered, so the first step that asks for one fails where it stands.
        var loader = new DatasetLoader(
            new ServiceCollection().BuildServiceProvider(),
            new Microsoft.Extensions.Hosting.Internal.HostingEnvironment { ContentRootPath = _root },
            configuration,
            status,
            null!,
            NullLogger<DatasetLoader>.Instance);

        (await loader.Run(new LoadRequest(From: "Relations"), CancellationToken.None)).Should().BeFalse();

        status.State.Should().Be(DatasetState.Failed);
        status.Detail.Should().StartWith("relations: ").And.Contain(nameof(TextRelationLoader));
        status.Texts.Should().BeEmpty();
        (await Scalar("SELECT count(*) FROM load_step_mark WHERE step = 'relations'")).Should().Be(0);
    }

    /// <summary>
    /// A step that runs Forge processes of its own hears what their statements named through the file
    /// they append to; the parent stands in for the child here, writing what a child would.
    /// </summary>
    [Fact]
    public async Task WhatAStepsOwnProcessesReadIsWhatTheStepReads()
    {
        var parent = new LoadStep("parent", async _ =>
        {
            var log = Environment.GetEnvironmentVariable(StatementTables.ChildLog);
            log.Should().NotBeNull();
            await File.AppendAllLinesAsync(log!, ["strong_entry", "not_a_table"]);
        }) { Children = () => [new Func<int>(() => 1)] };

        (await Load(extra: parent)).Should().Contain(outcome => outcome.Step == "parent" && outcome.Ran);
        await Settle(extra: parent);
        Environment.GetEnvironmentVariable(StatementTables.ChildLog).Should().BeNull();

        await Execute("INSERT INTO strong_entry (strong_number) VALUES ('H9998')");
        var outcomes = await Load(extra: parent);

        Ran(outcomes).Should().Contain("parent");
        outcomes.Single(outcome => outcome.Step == "parent").Why.Should().Contain("table strong_entry");
    }

    [Fact]
    public void TheTablesAStatementReadsAreTheOnesItNames()
    {
        var tables = new HashSet<string>(StringComparer.Ordinal) { "word", "text", "link", "link_word", "verse" };

        StatementTables.Named("SELECT w.text, x::text FROM word w JOIN \"link_word\" lw ON lw.word_id = w.id", tables)
            .Should().BeEquivalentTo("word", "link_word");
        StatementTables.Named("UPDATE public.link SET fingerprint = NULL; COPY verse (id) FROM STDIN", tables)
            .Should().BeEquivalentTo("link", "verse");
    }

    [Fact]
    public async Task EveryKindOfStatementIsHeard()
    {
        var tables = new HashSet<string>(StringComparer.Ordinal) { "provenance", "strong_entry", "text", "verse" };
        using var heard = new StatementTables(tables);
        heard.Start();

        await _db.Provenances.CountAsync();
        await using (var connection = new NpgsqlConnection(_connectionString))
        {
            await connection.OpenAsync();
            await using (var batch = new NpgsqlBatch(connection)
                         {
                             BatchCommands = { new NpgsqlBatchCommand("SELECT count(*) FROM strong_entry"), new NpgsqlBatchCommand("SELECT 1") },
                         })
            {
                await batch.ExecuteNonQueryAsync();
            }

            await using (var copy = await connection.BeginTextExportAsync("COPY (SELECT id FROM verse) TO STDOUT"))
            {
                await copy.ReadToEndAsync();
            }
        }

        heard.Stop().Tables.Should().BeEquivalentTo("provenance", "strong_entry", "verse");
    }

    /// <summary>Loads until a load runs nothing, and says how many loads that took.</summary>
    private async Task<int> Settle(LoadStep? extra = null)
    {
        for (var loads = 1; loads <= 4; loads++)
        {
            if (!Ran(await Load(extra: extra)).Any())
            {
                return loads;
            }
        }

        throw new InvalidOperationException("Four loads in a row each ran something.");
    }

    private async Task<IReadOnlyList<LoadMarks.StepOutcome>> Load(bool everything = false, bool failCopier = false, LoadStep? extra = null)
    {
        _failOnce = failCopier;
        var configuration = new ConfigurationBuilder().Build();
        using var marks = new LoadMarks(
            _connectionString,
            new TableFingerprints(_connectionString, 2, NullLogger.Instance),
            new FileDigests(_resources, _root, configuration),
            CodeDigest.ForTheForge(),
            NullLogger.Instance);
        await marks.Open(CancellationToken.None);
        return await marks.Run(extra is null ? Steps() : [extra, .. Steps()], everything, _ => { }, CancellationToken.None);
    }

    private List<LoadStep> Steps() =>
    [
        new(Source, async _ =>
        {
            var line = File.ReadLines(Path.Combine(_resources, "input.txt")).First();
            await Execute($"DELETE FROM provenance WHERE source LIKE 'from %' AND source <> 'from {line}'");
            await Execute($"INSERT INTO provenance (source) SELECT 'from {line}' WHERE NOT EXISTS (SELECT 1 FROM provenance WHERE source = 'from {line}')");
        }) { Files = ["input.txt"] },
        new(Copier, async _ =>
        {
            await Execute(
                """
                DELETE FROM strong_entry e WHERE NOT EXISTS (SELECT 1 FROM provenance p WHERE 'X' || p.id = e.strong_number);
                INSERT INTO strong_entry (strong_number)
                SELECT 'X' || p.id FROM provenance p
                WHERE p.source LIKE 'from %' AND NOT EXISTS (SELECT 1 FROM strong_entry e WHERE e.strong_number = 'X' || p.id)
                """);
            if (_failOnce)
            {
                throw new InvalidOperationException("The copier failed after writing.");
            }
        }),
        new(Reader, async _ => await Scalar("SELECT count(*) FROM provenance")) { Version = _readerVersion },
    ];

    private static IEnumerable<string> Ran(IEnumerable<LoadMarks.StepOutcome> outcomes) =>
        outcomes.Where(outcome => outcome.Ran).Select(outcome => outcome.Step);

    private async Task Execute(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
