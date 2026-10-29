using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Marks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
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
/// A step that changed a table it or a step before it reads leaves a mark that no longer holds, so the
/// load goes through the steps again from there until nothing moves: the load after it runs nothing.
/// <see cref="Settle"/> loads until nothing runs.
/// </para>
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class LoadMarksTests : IDisposable
{
    private const string Source = "source";
    private const string Copier = "copier";
    private const string Reader = "reader";
    private const string Early = "early";
    private const string Late = "late";

    private readonly string _connectionString;
    private readonly string _root = Directory.CreateTempSubdirectory("load-marks-").FullName;
    private readonly string _resources;
    private readonly AppDbContext _db;
    private bool _failOnce;
    private int _readerVersion = 1;
    private readonly List<string> _earlySaw = [];

    public LoadMarksTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _connectionString = _db.Database.GetConnectionString()!;
        _resources = Directory.CreateDirectory(Path.Combine(_root, "Resources")).FullName;
        File.WriteAllText(Path.Combine(_resources, "input.txt"), "first\n");

        // The class's tests share its database, one after another.
        Execute("TRUNCATE load_step_mark, table_fingerprint, provenance, strong_entry CASCADE").GetAwaiter().GetResult();
        Execute(ForgetTheMigrations).GetAwaiter().GetResult();
    }

    private const string Migration = "99990101000000_TouchesOneTable";

    private const string ForgetTheMigrations = $"""DELETE FROM "__EFMigrationsHistory" WHERE migration_id LIKE '9999%'""";

    private MigrationTables? _migrations;

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AStepIsSkippedOnceNothingItReadsMoves()
    {
        var first = await Load();

        // The source and the copier changed tables they read, so the load went through them again.
        Ran(first).Should().Equal(Source, Copier, Reader, Source, Copier);
        first.Where(outcome => outcome.Ran).Skip(3).Should().OnlyContain(outcome => outcome.Moved.Count == 0);

        Ran(await Load()).Should().BeEmpty();
        (await Scalar("SELECT count(*) FROM strong_entry")).Should().Be(1);
    }

    [Fact]
    public async Task AChangedFileRunsItsStepAndTheStepsThatReadWhatItChanged()
    {
        await Settle();
        File.WriteAllText(Path.Combine(_resources, "input.txt"), "second\n");

        var outcomes = await Load();

        Ran(outcomes).Should().StartWith([Source, Copier, Reader]);
        outcomes.First(outcome => outcome.Step == Source).Why.Should().Contain("file input.txt");
        outcomes.First(outcome => outcome.Step == Copier).Why.Should().Contain("table provenance, which source changed");
        Ran(await Load()).Should().BeEmpty();
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

        // The copier took the row out of a table it reads, so it ran once more, and wrote nothing.
        Ran(outcomes).Should().Equal(Copier, Copier);
        outcomes.First(outcome => outcome.Ran).Why.Should().Contain("table strong_entry");
        outcomes.Last(outcome => outcome.Ran).Moved.Should().BeEmpty();
        (await Scalar("SELECT count(*) FROM strong_entry WHERE strong_number = 'H9999'")).Should().Be(0);
        Ran(await Load()).Should().BeEmpty();
    }

    /// <summary>
    /// A migration moves the marks of the steps that read or wrote a table it touches, and no other; one
    /// whose reach cannot be told, or that this build does not know, moves every mark.
    /// </summary>
    [Theory]
    [InlineData("place_candidate", new string[0])]
    [InlineData("strong_entry", new[] { Copier })]
    [InlineData("provenance", new[] { Source, Copier, Reader })]
    [InlineData(null, new[] { Source, Copier, Reader })]
    [InlineData("unknown", new[] { Source, Copier, Reader })]
    public async Task AMigrationRunsTheStepsThatReadATableItTouchesAndNoOther(string? touches, string[] run)
    {
        _migrations = new MigrationTables(new Dictionary<string, IReadOnlySet<string>?>
        {
            [Migration] = touches is null ? null : new HashSet<string> { touches },
        });
        try
        {
            await Settle();
            var migration = touches == "unknown" ? "99990101000001_NotInThisBuild" : Migration;
            await Execute($"""INSERT INTO "__EFMigrationsHistory" (migration_id, product_version) VALUES ('{migration}', '10.0.12')""");

            var outcomes = await Load();

            Ran(outcomes).Should().Equal(run);
            outcomes.Where(outcome => outcome.Ran).Where(outcome => !outcome.Why.Contains("migration 9999")).Should().BeEmpty();
            Ran(await Load()).Should().BeEmpty();
        }
        finally
        {
            await Execute(ForgetTheMigrations);
        }
    }

    /// <summary>
    /// Raw SQL in a migration touches the tables it names; a routine it defines, which any statement can
    /// reach, or SQL naming no table touches everything.
    /// </summary>
    [Fact]
    public async Task RawSqlInAMigrationTouchesTheTablesItNamesOrEverything()
    {
        MigrationTables.Tables([new SqlOperation { Sql = "UPDATE strong_entry SET lemma = NULL" }])
            .Should().Contain(MigrationTables.Raw + "strong_entry");
        MigrationTables.Tables([new SqlOperation { Sql = "CREATE OR REPLACE FUNCTION f() RETURNS int AS $$ SELECT 1 $$ LANGUAGE sql" }])
            .Should().BeNull();
        MigrationTables.Tables([new AddColumnOperation { Table = "place_candidate", Name = "x", ClrType = typeof(int) }])
            .Should().BeEquivalentTo(["place_candidate"]);
        MigrationTables.Tables([new EnsureSchemaOperation { Name = "other" }]).Should().BeNull();

        _migrations = new MigrationTables(new Dictionary<string, IReadOnlySet<string>?>
        {
            [Migration] = MigrationTables.Tables([new SqlOperation { Sql = "REINDEX INDEX ix_nothing_known" }]),
        });
        try
        {
            await Settle();
            await Execute($"""INSERT INTO "__EFMigrationsHistory" (migration_id, product_version) VALUES ('{Migration}', '10.0.12')""");
            Ran(await Load()).Should().Equal(Source, Copier, Reader);
        }
        finally
        {
            await Execute(ForgetTheMigrations);
        }
    }

    /// <summary>The migrations of the corpus are all read, and one that only changes columns touches only its tables.</summary>
    [Fact]
    public async Task EveryMigrationOfTheCorpusIsReadAndAColumnChangeTouchesItsTablesOnly()
    {
        var corpus = MigrationTables.OfTheCorpus();
        var applied = await Scalar("""SELECT count(*) FROM "__EFMigrationsHistory" """);
        applied.Should().BeGreaterThan(100);
        var read = corpus.Touches;
        read.Should().HaveCount((int)applied);
        read["20261029091002_PlacesMayHaveNoScoreAndCreditTheirCoordinates"].Should().BeEquivalentTo(["place_location", "place_candidate"]);
    }

    /// <summary>
    /// A step that keeps marks of its own (the recipe) holds what it wrote as it left it: its own writes do
    /// not send the load back to it, and a write by anything else to what it read still runs it.
    /// </summary>
    [Fact]
    public async Task AStepKeepingItsOwnMarksIsNotRunAgainByItsOwnWritesButIsByAnotherWrite()
    {
        var steps = Steps().Select(step => step.Name == Copier ? step with { KeepsItsOwnMarks = true } : step).ToList();

        Ran(await Load(steps: steps)).Should().Equal(Source, Copier, Reader, Source);
        Ran(await Load(steps: steps)).Should().BeEmpty();

        await Execute("INSERT INTO strong_entry (strong_number) VALUES ('H9999')");
        Ran(await Load(steps: steps)).Should().Equal(Copier);
        Ran(await Load(steps: steps)).Should().BeEmpty();
    }

    [Fact]
    public void AVacuumOrAnAnalyzeReadsNoTable()
    {
        StatementTables.Maintenance("VACUUM (ANALYZE) \"strong_reach\"").Should().BeTrue();
        StatementTables.Maintenance("  analyze word").Should().BeTrue();
        StatementTables.Maintenance("SELECT relname FROM pg_stat_user_tables").Should().BeFalse();
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

        Ran(await Load()).Should().Equal(Copier, Copier);
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
        outcomes.First(outcome => outcome.Step == "parent" && outcome.Ran).Why.Should().Contain("table strong_entry");
    }

    /// <summary>
    /// The early step reads a table the late step after it writes. Rewritten with the rows it already
    /// held, the table has not moved, and the early step is not run again in this load or the next.
    /// </summary>
    [Fact]
    public async Task ALaterStepRewritingWhatAnEarlierStepReadWithTheSameRowsRunsNothingAgain()
    {
        File.WriteAllText(Path.Combine(_resources, "late.txt"), "one\n");
        await Settle(steps: EarlyAndLate());
        File.AppendAllText(Path.Combine(_resources, "late.txt"), "a line nothing reads\n");

        var outcomes = await Load(steps: EarlyAndLate());

        Ran(outcomes).Should().Equal(Late);
        outcomes.Single(outcome => outcome.Ran).Wrote.Should().Contain("strong_entry");
        outcomes.Single(outcome => outcome.Ran).Moved.Should().BeEmpty();
        Ran(await Load(steps: EarlyAndLate())).Should().BeEmpty();
    }

    /// <summary>
    /// When the late step changes the rows the early step read, the load goes back and runs the early
    /// step over them, and the load after it finds nothing to run.
    /// </summary>
    [Fact]
    public async Task ALaterStepChangingWhatAnEarlierStepReadRunsTheEarlierOneAgainInTheSameLoad()
    {
        File.WriteAllText(Path.Combine(_resources, "late.txt"), "one\n");
        await Settle(steps: EarlyAndLate());
        File.WriteAllText(Path.Combine(_resources, "late.txt"), "two\n");
        _earlySaw.Clear();

        var outcomes = await Load(steps: EarlyAndLate());

        Ran(outcomes).Should().Equal(Late, Early, Late);
        outcomes.First(outcome => outcome.Step == Early && outcome.Ran).Why.Should().Contain("table strong_entry, which late changed");
        outcomes.Last(outcome => outcome.Step == Late).Moved.Should().BeEmpty();
        _earlySaw.Should().Equal("Ltwo");
        Ran(await Load(steps: EarlyAndLate())).Should().BeEmpty();
    }

    /// <summary>
    /// A late step that changes what the early one reads every time it runs never settles: the load
    /// stops going back after a few passes, and the next load runs the early step again rather than
    /// skipping it over rows it never read.
    /// </summary>
    [Fact]
    public async Task AStepThatChangesWhatAnEarlierStepReadsEveryTimeStopsTheLoadGoingBackAndIsNotSkippedNextTime()
    {
        var outcomes = await Load(steps: EarlyAndLate(everyTime: true));

        Ran(outcomes).Count(step => step == Early).Should().Be(LoadMarks.Passes);
        _earlySaw.Should().HaveCount(LoadMarks.Passes);

        var next = await Load(steps: EarlyAndLate(everyTime: true));
        Ran(next).Should().StartWith([Early]);
        next.First(outcome => outcome.Step == Early && outcome.Ran).Why.Should().Contain("table strong_entry");
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
        using var heard = new StatementTables(tables, new NpgsqlConnectionStringBuilder(_connectionString).Database);
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
    private async Task<int> Settle(LoadStep? extra = null, List<LoadStep>? steps = null)
    {
        for (var loads = 1; loads <= 4; loads++)
        {
            if (!Ran(await Load(extra: extra, steps: steps)).Any())
            {
                return loads;
            }
        }

        throw new InvalidOperationException("Four loads in a row each ran something.");
    }

    private async Task<IReadOnlyList<LoadMarks.StepOutcome>> Load(
        bool everything = false, bool failCopier = false, LoadStep? extra = null, List<LoadStep>? steps = null)
    {
        _failOnce = failCopier;
        var configuration = new ConfigurationBuilder().Build();
        using var marks = new LoadMarks(
            _connectionString,
            new TableFingerprints(_connectionString, 2, NullLogger.Instance),
            new FileDigests(_resources, _root, configuration),
            CodeDigest.ForTheForge(),
            NullLogger.Instance,
            _migrations);
        await marks.Open(CancellationToken.None);
        steps ??= extra is null ? Steps() : [extra, .. Steps()];
        return await marks.Run(steps, everything, _ => { }, CancellationToken.None);
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

    private const string EarlyReads =
        "SELECT coalesce(string_agg(strong_number, ',' ORDER BY strong_number), '') FROM strong_entry WHERE strong_number LIKE 'L%' OR strong_number LIKE 'C%'";

    /// <summary>
    /// The early step reads the rows the late step writes: the late step's from the first line of its
    /// file (rewriting the row that is there with itself), or a new row every time it runs.
    /// </summary>
    private List<LoadStep> EarlyAndLate(bool everyTime = false) =>
    [
        new(Early, async _ => _earlySaw.Add(await Text(EarlyReads))),
        everyTime
            ? new(Late, async _ => await Execute(
                "INSERT INTO strong_entry (strong_number) SELECT 'C' || (count(*) + 1) FROM strong_entry WHERE strong_number LIKE 'C%'"))
            : new(Late, async _ =>
            {
                var line = File.ReadLines(Path.Combine(_resources, "late.txt")).First();
                await Execute($"DELETE FROM strong_entry WHERE strong_number LIKE 'L%' AND strong_number <> 'L{line}'");
                await Execute($"INSERT INTO strong_entry (strong_number) SELECT 'L{line}' WHERE NOT EXISTS (SELECT 1 FROM strong_entry WHERE strong_number = 'L{line}')");
                await Execute($"UPDATE strong_entry SET strong_number = strong_number WHERE strong_number = 'L{line}'");
            }) { Files = ["late.txt"] },
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

    private async Task<string> Text(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<long> Scalar(string sql)
    {
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
