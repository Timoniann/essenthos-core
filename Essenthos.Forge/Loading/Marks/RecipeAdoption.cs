using Npgsql;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// A recorded verb run by hand, watched so that once it is recorded at the end of the recipe its line can
/// be given the mark the load would have left (<see cref="RecipeMarks.Adopt"/>): its texts' digests and the
/// tables' write counts before it runs, and what its statements name while it runs.
/// </summary>
internal sealed class RecipeAdoption : IDisposable
{
    private readonly string _connectionString;
    private readonly string _resources;
    private readonly IConfiguration _configuration;
    private readonly string _contentRoot;
    private readonly ILogger _logger;
    private readonly RecipeLine _line;
    private readonly IReadOnlyDictionary<string, string> _before;
    private readonly IReadOnlyDictionary<string, long> _writes;
    private readonly StatementTables _heard;
    private IReadOnlySet<string> _named = new HashSet<string>();

    private RecipeAdoption(
        string connectionString, string resources, IConfiguration configuration, string contentRoot, ILogger logger,
        RecipeLine line, IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, long> writes)
    {
        _connectionString = connectionString;
        _resources = resources;
        _configuration = configuration;
        _contentRoot = contentRoot;
        _logger = logger;
        _line = line;
        _before = before;
        _writes = writes;
        _heard = new StatementTables(null, new NpgsqlConnectionStringBuilder(connectionString).Database);
        _heard.Start();
    }

    /// <summary>
    /// Starts watching a run of <paramref name="args"/> if it is one the recipe records; null otherwise, or
    /// on a database without the recipe's marks.
    /// </summary>
    public static async Task<RecipeAdoption?> Begin(
        string connectionString, string resources, IConfiguration configuration, string contentRoot, string[] args, ILogger logger)
    {
        if (!Recipe.Records(args))
        {
            return null;
        }

        try
        {
            var texts = await Texts(connectionString);
            var line = Recipe.Lines([Recipe.StepOf(args)], texts, resources, DatasetLoader.RecipeSources())[0];
            var tables = new TableFingerprints(connectionString, 1, logger);
            await tables.Open(CancellationToken.None);
            var writes = await tables.Writes(CancellationToken.None);
            var before = await TextDigests.Of(connectionString, line.Texts, CancellationToken.None);
            return new RecipeAdoption(connectionString, resources, configuration, contentRoot, logger, line, before, writes);
        }
        catch (PostgresException exception) when (exception.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            logger.LogDebug(exception, "This database keeps no marks of the recipe's lines");
            return null;
        }
    }

    /// <summary>The verb has ended: what its statements named is what its line read, not what comes after it.</summary>
    public void Ran() => _named = _heard.Stop().Tables;

    /// <summary>After the run was recorded: gives its line its mark, if its texts had not moved before it.</summary>
    public async Task Finish()
    {
        var heard = _named;
        var texts = await Texts(_connectionString);
        var lines = Recipe.Lines(Recipe.Read(_resources), texts, _resources, DatasetLoader.RecipeSources());
        var marks = new RecipeMarks(
            _connectionString,
            new TableFingerprints(_connectionString, 2, _logger),
            new FileDigests(_resources, _contentRoot, _configuration, _logger),
            DatasetLoader.RecipeCode(),
            _logger);
        await marks.Adopt(lines, _line.Command, _before, _writes, heard, CancellationToken.None);
    }

    public void Dispose() => _heard.Dispose();

    private static async Task<List<string>> Texts(string connectionString)
    {
        var texts = new List<string>();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT slug FROM text", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            texts.Add(reader.GetString(0));
        }

        return texts;
    }
}
