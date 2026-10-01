using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links.Evidentia;

namespace Essenthos.Core.Verbs;

/// <summary>
/// One thing the Forge can be asked to do: <c>forge &lt;name&gt; &lt;arguments&gt;</c>.
/// </summary>
/// <param name="Name">What is typed first.</param>
/// <param name="Arguments">What follows it, as <c>forge help</c> prints it.</param>
/// <param name="Help">One line saying what it does.</param>
/// <param name="Run">The verb itself, given every argument with the name first; what it returns is the exit code.</param>
/// <param name="Least">How many arguments it needs after its name.</param>
/// <param name="Most">How many it takes after its name, or null for as many as are given.</param>
/// <param name="Records">
/// Whether a finished run with these arguments writes what the load does not, and so goes into the
/// recipe the load replays (<see cref="Recipe"/>). Null for a verb that never does.
/// </param>
internal sealed record ForgeVerb(
    string Name,
    string Arguments,
    string Help,
    Func<ForgeRun, string[], Task<int>> Run,
    int Least = 0,
    int? Most = null,
    Func<string[], bool>? Records = null)
{
    public bool Accepts(string[] args) => args.Length - 1 >= Least && (Most is not { } most || args.Length - 1 <= most);

    public string Usage => Arguments.Length > 0 ? $"{Name} {Arguments}" : Name;
}

/// <summary>What every verb works with: the services, the log, the corpus sources and the database.</summary>
internal sealed class ForgeRun(IServiceProvider services, ILogger logger, string resources, string databaseConnection)
{
    public ILogger Logger => logger;

    public string Resources => resources;

    public string DatabaseConnection => databaseConnection;

    public IServiceScope Scope() => services.CreateScope();

    /// <summary>
    /// The vacuum a step that wrote millions of rows ends with, so the next step plans against the tables as
    /// they now are rather than as autovacuum last saw them.
    /// </summary>
    public async Task Tidy() => logger.LogInformation("{Outcome}", await Maintenance.Tidy(databaseConnection, []));

    /// <summary>A verdict recorded is written to the ledger at once, so no verdict lives only in this database.</summary>
    public async Task Record(IServiceScope scope, int runId) =>
        logger.LogInformation("{Outcome}", await scope.ServiceProvider.GetRequiredService<EvidentiaLedger>().Export(runId, resources));

    /// <summary>
    /// The ledger's verdicts on the runs a command's texts reach, put back after it rewrote their links
    /// and deleted, with a link it replaced, the claims a verdict had put on it.
    /// </summary>
    public async Task Replay(IServiceScope scope, Func<string, string, bool> runs) =>
        logger.LogInformation("\n{Outcome}", await scope.ServiceProvider.GetRequiredService<EvidentiaLedger>().Replay(resources, runs));
}
