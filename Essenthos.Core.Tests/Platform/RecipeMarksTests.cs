using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Marks;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Xunit;

namespace Essenthos.Core.Tests.Platform;

/// <summary>
/// The recipe's lines skipped one by one: each is keyed on its verb's code, its files and the texts it
/// reads as the lines before it left them. Lines of made-up verbs over four texts; a line writes one link
/// between its first and last text the first time it runs, under its own provenance, and nothing after,
/// as a replayed Forge run whose output the corpus already holds writes nothing.
/// </summary>
[Collection(WitnessDatabaseCollection.Name)]
public sealed class RecipeMarksTests : IDisposable
{
    private static readonly string[] Slugs = ["RMA", "RMB", "RMC", "RMD"];

    private readonly string _connectionString;
    private readonly string _root = Directory.CreateTempSubdirectory("recipe-marks-").FullName;
    private readonly AppDbContext _db;
    private readonly Dictionary<string, string> _code = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string[]> _heard = new(StringComparer.Ordinal);
    private readonly List<string> _ran = [];
    private readonly HashSet<string> _failing = new(StringComparer.Ordinal);

    public RecipeMarksTests(WitnessDatabase database)
    {
        _db = database.NewContext();
        _connectionString = _db.Database.GetConnectionString()!;

        // The class's tests share its database, one after another.
        Execute("TRUNCATE load_step_mark, table_fingerprint, link, provenance, strong_entry, entity CASCADE")
            .GetAwaiter().GetResult();
        Execute("DELETE FROM book WHERE text_id IN (SELECT id FROM text WHERE slug LIKE 'RM_')").GetAwaiter().GetResult();
        foreach (var slug in Slugs.Where(slug => !_db.Texts.Any(text => text.Slug == slug)))
        {
            _db.Texts.Add(new Text { Slug = slug, Name = slug, Kind = TextKind.Translation, Language = "en" });
        }

        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task EveryLineRunsOnceAndThenNoneAlthoughEachWroteWhatOthersRead()
    {
        string[] recipe = ["fake RMA RMB", "fake RMB RMC", "fake RMC RMD", "fake RMA RMD"];

        (await Ran(recipe)).Should().Equal(recipe);

        // Each line wrote a text an earlier line reads, and a later line wrote what it read: neither its own
        // write nor a later line's runs it again.
        (await Ran(recipe)).Should().BeEmpty();
        (await Scalar("SELECT count(*) FROM link")).Should().Be(4);
    }

    [Fact]
    public async Task ALineAppendedRunsAlone()
    {
        string[] recipe = ["fake RMA RMB", "fake RMB RMC", "fake RMC RMD"];
        await Ran(recipe);

        (await Ran([.. recipe, "fake RMD RMA"])).Should().Equal("fake RMD RMA");
        (await Ran([.. recipe, "fake RMD RMA"])).Should().BeEmpty();
    }

    /// <summary>
    /// The changed line writes, so the line after it that reads one of its texts runs; that one writes
    /// nothing, so the last line, which reads only what it and the first line read, does not run.
    /// </summary>
    [Fact]
    public async Task ALineChangedInTheMiddleRunsWithTheLinesAfterItThatReadItsTextsUntilOneWritesNothing()
    {
        await Ran(["fake RMA RMB", "fake RMB RMC", "fake RMC RMD", "fake RMA RMD"]);

        var ran = await Ran(["fake RMA RMB", "fake RMB RMC --other", "fake RMC RMD", "fake RMA RMD"]);

        ran.Should().Equal("fake RMB RMC --other", "fake RMC RMD");
        (await Ran(["fake RMA RMB", "fake RMB RMC --other", "fake RMC RMD", "fake RMA RMD"])).Should().BeEmpty();
    }

    [Fact]
    public async Task ALineTakenOutRunsTheLinesAfterItThatReadItsTextsAndLeavesWhatItWrote()
    {
        await Ran(["fake RMA RMB", "fake RMB RMC", "fake RMC RMD", "fake RMA RMD"]);

        (await Ran(["fake RMA RMB", "fake RMC RMD", "fake RMA RMD"])).Should().Equal("fake RMC RMD");
        (await Scalar($"SELECT count(*) FROM load_step_mark WHERE starts_with(step, '{RecipeMarks.LinePrefix}')")).Should().Be(3);
        (await Scalar("SELECT count(*) FROM link")).Should().Be(4);
    }

    [Fact]
    public async Task AChangeToOneVerbsCodeRunsOnlyThatVerbsLines()
    {
        string[] recipe = ["alpha RMA RMB", "beta RMB RMC", "alpha RMC RMD", "beta RMA RMD"];
        await Ran(recipe);

        _code["alpha"] = "alpha, changed";

        (await Ran(recipe)).Should().Equal("alpha RMA RMB", "alpha RMC RMD");
    }

    /// <summary>A link written outside the recipe between two texts runs the lines that read either, and no other.</summary>
    [Fact]
    public async Task AWriteOutsideTheRecipeRunsOnlyTheLinesReadingTheTextsItTouched()
    {
        string[] recipe = ["fake RMA RMB", "fake RMB RMC", "fake RMA RMB --again", "fake RMC RMD"];
        await Ran(recipe);

        await Link("RMC", "RMD", "written by a later step");

        (await Ran(recipe)).Should().Equal("fake RMB RMC", "fake RMC RMD");
        (await Ran(recipe)).Should().BeEmpty();
    }

    /// <summary>
    /// A line counts a table it does not read through its texts only if its statements named it: a step
    /// after the recipe writing that table runs that line alone, and a table no line reads runs nothing.
    /// </summary>
    [Fact]
    public async Task AWriteToAnotherTableRunsOnlyTheLinesWhoseStatementsNamedIt()
    {
        string[] recipe = ["fake RMA RMB", "fake RMB RMC", "fake RMC RMD"];
        _heard["fake RMB RMC"] = ["strong_entry"];
        await Ran(recipe);

        await Execute("INSERT INTO strong_entry (strong_number) VALUES ('H9999')");
        (await Ran(recipe)).Should().Equal("fake RMB RMC");

        await Execute("UPDATE text SET about = 'moved' WHERE slug = 'RMA'");
        (await Ran(recipe)).Should().BeEmpty();
    }

    /// <summary>
    /// A line run by hand and recorded at the end is given the mark the load would have left, so the next
    /// load neither runs it nor counts what it wrote as a change from outside.
    /// </summary>
    [Fact]
    public async Task ALineRunByHandIsTakenInAndTheNextLoadRunsNothing()
    {
        string[] recipe = ["fake RMA RMB", "fake RMB RMC", "fake RMC RMD"];
        await Ran(recipe);

        (await ByHand("fake RMD RMA", recipe)).Should().BeTrue();

        (await Ran([.. recipe, "fake RMD RMA"])).Should().BeEmpty();
    }

    /// <summary>When something else had moved its texts first, the line is not taken in, and the load runs what reads what moved.</summary>
    [Fact]
    public async Task ALineRunByHandAfterAnotherChangeIsNotTakenIn()
    {
        string[] recipe = ["fake RMA RMB", "fake RMB RMC", "fake RMC RMD"];
        await Ran(recipe);
        await Link("RMA", "RMB", "written by hand without the recipe");

        (await ByHand("fake RMD RMA", recipe)).Should().BeFalse();

        // Both writes count as changes from outside: every line reading RMA, RMB or RMD runs.
        (await Ran([.. recipe, "fake RMD RMA"])).Should().Equal("fake RMA RMB", "fake RMB RMC", "fake RMC RMD", "fake RMD RMA");
    }

    [Fact]
    public async Task EveryLineRunsWhenEveryLineIsAskedFor()
    {
        string[] recipe = ["fake RMA RMB", "fake RMB RMC"];
        await Ran(recipe);

        (await Ran(recipe, everything: true)).Should().Equal(recipe);
        (await Ran(recipe)).Should().BeEmpty();
    }

    /// <summary>
    /// A rebuild that stopped is resumed, not started again: the lines that finished while every line was
    /// asked for keep their marks, so a load heeding them goes on from the line that threw.
    /// </summary>
    [Fact]
    public async Task ALoadAfterALineThrewGoesOnFromThatLine()
    {
        string[] recipe = ["fake RMA RMB", "fake RMB RMC", "fake RMC RMD", "fake RMA RMD"];
        _failing.Add("fake RMC RMD");

        await FluentActions.Awaiting(() => Ran(recipe, everything: true)).Should().ThrowAsync<InvalidOperationException>();
        _ran.Should().Equal("fake RMA RMB", "fake RMB RMC", "fake RMC RMD");

        _failing.Clear();
        (await Ran(recipe)).Should().Equal("fake RMC RMD", "fake RMA RMD");
        (await Ran(recipe)).Should().BeEmpty();
    }

    /// <summary>
    /// A line that writes back the rows that were there (a replace that gives the same answer) has left its
    /// texts as it found them, so the lines after it that read them do not run.
    /// </summary>
    [Fact]
    public async Task ALineThatWritesBackTheRowsThereRunsNoLineAfterIt()
    {
        string[] recipe = ["fake RMA RMB", "rewrite RMB RMC", "fake RMC RMD"];
        await Ran(recipe);

        await Link("RMA", "RMB", "written by a later step");

        (await Ran(recipe)).Should().Equal("fake RMA RMB", "rewrite RMB RMC");
        (await Ran(recipe)).Should().BeEmpty();
    }

    /// <summary>
    /// A name written on a word moves the text's names and nothing else of it: the lines whose statements read
    /// names run, and a line that only reads the text's links does not.
    /// </summary>
    [Fact]
    public async Task ANameWrittenOnAWordRunsOnlyTheLinesThatReadNames()
    {
        string[] recipe = ["fake RMA RMB", "fake RMA RMC", "fake RMB RMD"];
        _heard["fake RMA RMB"] = ["link", "word", "word_entity"];
        _heard["fake RMA RMC"] = ["link", "link_word", "word"];
        _heard["fake RMB RMD"] = ["link"];
        var text = await _db.Texts.SingleAsync(t => t.Slug == "RMA");
        _db.AddBook(text, 1, "Genesis", (1, 1, ["Adam"]));
        await _db.SaveChangesAsync();
        await Ran(recipe);

        var adam = new Entity { Kind = EntityKind.Person, Slug = "rm-adam", Name = "Adam", SourceId = "rm-adam", Source = "a test" };
        _db.Entities.Add(adam);
        _db.WordEntities.Add(new WordEntity
        {
            WordId = (await _db.Words.SingleAsync(w => w.TextId == text.Id)).Id, Entity = adam, Method = LinkMethod.Manual, Source = "a test",
        });
        await _db.SaveChangesAsync();

        (await Ran(recipe)).Should().Equal("fake RMA RMB");
        (await Ran(recipe)).Should().BeEmpty();
    }

    /// <summary>
    /// What the recipe takes back before it ends (<c>finish</c>) is part of what it leaves: the line that wrote
    /// it does not run again at the next load, though it would write it again.
    /// </summary>
    [Fact]
    public async Task WhatTheRecipeTakesBackBeforeItEndsDoesNotRunItsLinesAgain()
    {
        string[] recipe = ["fake RMA RMB", "fake RMB RMC"];
        Task TakeBack(CancellationToken cancellationToken) =>
            Execute("DELETE FROM link WHERE provenance_id IN (SELECT id FROM provenance WHERE source = 'fake RMB RMC')");

        (await Ran(recipe, finish: TakeBack)).Should().Equal(recipe);
        (await Scalar("SELECT count(*) FROM link")).Should().Be(1);

        (await Ran(recipe, finish: TakeBack)).Should().BeEmpty();
    }

    private async Task<List<string>> Ran(
        IReadOnlyList<string> recipe, bool everything = false, Func<CancellationToken, Task>? finish = null)
    {
        _ran.Clear();
        await Marks().Run(Lines(recipe), everything, Run, CancellationToken.None, finish);
        return [.. _ran];
    }

    /// <summary>The line run as a Forge verb run by hand does: watched, run, recorded at the end, then taken in.</summary>
    private async Task<bool> ByHand(string command, IReadOnlyList<string> recipe)
    {
        var line = Lines([command])[0];
        var tables = new TableFingerprints(_connectionString, 1, NullLogger.Instance);
        await tables.Open(CancellationToken.None);
        var writes = await tables.Writes(CancellationToken.None);
        var before = await TextDigests.Of(_connectionString, line.Texts, CancellationToken.None);
        await Run(line, CancellationToken.None);
        return await Marks().Adopt(Lines([.. recipe, command]), command, before, writes, new HashSet<string>(), CancellationToken.None);
    }

    private static List<RecipeLine> Lines(IReadOnlyList<string> recipe) =>
    [
        .. recipe.Select(command =>
        {
            var parts = command.Split(' ');
            var texts = parts.Skip(1).Where(part => Slugs.Contains(part)).Order(StringComparer.Ordinal).ToList();
            return new RecipeLine(new RecipeStep(parts[0], parts[1..]), command, texts, []);
        }),
    ];

    private RecipeMarks Marks() =>
        new(
            _connectionString,
            new TableFingerprints(_connectionString, 2, NullLogger.Instance),
            new FileDigests(_root, _root, new ConfigurationBuilder().Build()),
            verb => _code.GetValueOrDefault(verb, verb),
            NullLogger.Instance);

    private async Task<RecipeRun> Run(RecipeLine line, CancellationToken cancellationToken)
    {
        _ran.Add(line.Command);
        if (_failing.Contains(line.Command))
        {
            throw new InvalidOperationException($"`{line.Command}` failed");
        }

        if (await Scalar($"SELECT count(*) FROM provenance WHERE source = '{line.Command}'") == 0)
        {
            await Link(line.Texts[0], line.Texts[^1], line.Command);
        }
        else if (line.Step.Verb == "rewrite")
        {
            await Execute($"UPDATE link SET confidence = confidence WHERE provenance_id IN (SELECT id FROM provenance WHERE source = '{line.Command}')");
        }

        return new RecipeRun(true, _heard.GetValueOrDefault(line.Command, []).ToHashSet(StringComparer.Ordinal));
    }

    private async Task Link(string from, string to, string source) =>
        await Execute(
            $"""
             WITH p AS (INSERT INTO provenance (source) VALUES ('{source}') RETURNING id)
             INSERT INTO link (from_text_id, to_text_id, relation, method, provenance_id)
             SELECT (SELECT id FROM text WHERE slug = '{from}'), (SELECT id FROM text WHERE slug = '{to}'), 'renders', 'manual', p.id FROM p
             """);

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
