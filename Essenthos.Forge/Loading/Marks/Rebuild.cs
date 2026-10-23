using System.Diagnostics;
using System.Text.RegularExpressions;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// The corpus built from nothing into a database of its own, ignoring every mark, verified, and compared
/// table by table with a reference corpus. It is the check on the marks: a load that skips what has not
/// moved must end where a load that skips nothing ends, and this is that load, done cold.
///
/// <para>
/// It never touches the database the Forge is configured for, nor the reference beyond reading it, and it
/// swaps nothing: putting a rebuilt corpus in place of the live one is a separate step, taken by hand. No
/// model runs (<see cref="ModelRuns"/>): the recipe's alignments come from the aligner's stored
/// workspaces, and a pair whose stored alignment no longer fits its words stops the rebuild rather than
/// training again. The load writes review lists and reads the recipe under the Resources it is given,
/// so it is given a folder of its own.
/// </para>
/// </summary>
internal static partial class Rebuild
{
    public static async Task<int> Run(
        string configured,
        string database,
        string reference,
        string resources,
        int sessions,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var live = new NpgsqlConnectionStringBuilder(configured).Database;
        if (!Name().IsMatch(database) || !Name().IsMatch(reference))
        {
            logger.LogError("A database is named in lower-case letters, digits and underscores: \"{Database}\", \"{Reference}\"", database, reference);
            return 1;
        }

        if (database == live || database == reference)
        {
            logger.LogError(
                "The rebuild goes into a database of its own, never into {Live} (the one this Forge is configured for) or the reference",
                live);
            return 1;
        }

        if (!Directory.Exists(resources))
        {
            logger.LogError("No Resources folder at {Resources}", resources);
            return 1;
        }

        var maintenance = new NpgsqlConnectionStringBuilder(configured) { Database = "postgres" }.ConnectionString;
        var target = new NpgsqlConnectionStringBuilder(configured) { Database = database }.ConnectionString;
        var compared = new NpgsqlConnectionStringBuilder(configured) { Database = reference }.ConnectionString;
        await using (var connection = new NpgsqlConnection(maintenance))
        {
            await connection.OpenAsync(cancellationToken);
            await using var exists = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = ANY(@names)", connection);
            exists.Parameters.AddWithValue("names", new[] { database });
            if ((long)(await exists.ExecuteScalarAsync(cancellationToken))! > 0)
            {
                logger.LogError("{Database} already exists. A rebuild starts from nothing and drops nothing: drop it by hand first", database);
                return 1;
            }

            await using var referenceExists = new NpgsqlCommand("SELECT count(*) FROM pg_database WHERE datname = @name", connection);
            referenceExists.Parameters.AddWithValue("name", reference);
            if ((long)(await referenceExists.ExecuteScalarAsync(cancellationToken))! == 0)
            {
                logger.LogError("There is no reference database {Reference} to compare with", reference);
                return 1;
            }

            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", connection);
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        var started = Stopwatch.GetTimestamp();
        logger.LogInformation("Rebuild: {Database} created; migrating it", database);
        await using (var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                         .UseNpgsql(target, npgsql => npgsql.CommandTimeout(1800)).Options))
        {
            await db.Database.MigrateAsync(cancellationToken);
        }

        logger.LogInformation(
            "Rebuild: loading {Database} from nothing, every step, no model run, from {Resources}, with the alignments stored in {Alignments}",
            database, resources, AlignmentStore.Root);
        if (await Forge(["load", "--no-skip"], target, resources, cancellationToken) is not 0 and var loadExit)
        {
            logger.LogError("Rebuild: the load into {Database} failed ({Exit}); the database is left as it stands", database, loadExit);
            return 1;
        }

        logger.LogInformation("Rebuild: loaded in {Minutes:F0} min; verifying", Stopwatch.GetElapsedTime(started).TotalMinutes);
        var verified = await Forge(["verify"], target, resources, cancellationToken);
        logger.LogInformation("Rebuild: verify {Outcome}", verified == 0 ? "passed" : $"failed ({verified})");

        logger.LogInformation("Rebuild: comparing {Database} with {Reference}, table by table", database, reference);
        var differences = await CorpusComparison.Compare(compared, target, sessions, cancellationToken);
        logger.LogInformation("Rebuild of {Database} against {Reference}:\n{Comparison}", database, reference, CorpusComparison.Describe(differences));
        logger.LogInformation(
            "Rebuild: done in {Minutes:F0} min. Nothing was swapped; {Database} stays beside {Live} until somebody puts it in place by hand",
            Stopwatch.GetElapsedTime(started).TotalMinutes, database, live);
        return verified == 0 ? 0 : 1;
    }

    /// <summary>The Forge as a process of its own against the rebuilt database, its log on this one's console.</summary>
    private static async Task<int> Forge(string[] arguments, string connectionString, string resources, CancellationToken cancellationToken)
    {
        var host = Environment.ProcessPath
                   ?? throw new InvalidOperationException("The Forge cannot find its own executable to rebuild with.");
        var start = new ProcessStartInfo(host) { UseShellExecute = false };
        if (Path.GetFileNameWithoutExtension(host).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            start.ArgumentList.Add(typeof(Rebuild).Assembly.Location);
        }

        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["Database__ConnectionString"] = connectionString;
        start.Environment["Dataset__ResourcesPath"] = resources;
        start.Environment["Dataset__AlignmentsPath"] = AlignmentStore.Root;
        start.Environment[ModelRuns.Forbidden] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("The Forge could not be started.");
        await process.WaitForExitAsync(cancellationToken);
        return process.ExitCode;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex Name();
}
