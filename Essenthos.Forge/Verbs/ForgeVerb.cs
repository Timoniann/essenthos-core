using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Links.Evidentia;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;

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
internal sealed record ForgeVerb(
    string Name,
    string Arguments,
    string Help,
    Func<ForgeRun, string[], Task<int>> Run,
    int Least = 0,
    int? Most = null)
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

    /// <summary>
    /// A verdict recorded is written to the ledger at once, so no verdict lives only in this database, and the run's
    /// pair file under <c>mapping/</c> is made again from the ledger where the store holds both texts' words and frames.
    /// </summary>
    public async Task Record(IServiceScope scope, int runId)
    {
        var exported = await scope.ServiceProvider.GetRequiredService<EvidentiaLedger>().Export(runId, resources);
        logger.LogInformation("{Outcome}", exported);
        var run = EvidentiaLedger.LedgerRun.Read(Path.Combine(exported.Folder, EvidentiaLedger.RunFile));
        var pair = MappingProducts.Pair(run.From, run.To);
        var store = new ProductStore(resources);
        if (!EvidentiaFiles.CanWrite(store, pair))
        {
            logger.LogInformation("{Pair}'s evidentia file waits for both texts' words and frames under texts/", pair);
            return;
        }

        logger.LogInformation("{Outcome}", EvidentiaFiles.Write(store, resources, pair));
        store.Save();
    }

    /// <summary>
    /// The ledger's verdicts on the runs a command's texts reach, put back after it rewrote their links
    /// and deleted, with a link it replaced, the claims a verdict had put on it.
    /// </summary>
    public async Task Replay(IServiceScope scope, Func<string, string, bool> runs) =>
        logger.LogInformation("\n{Outcome}", await scope.ServiceProvider.GetRequiredService<EvidentiaLedger>().Replay(resources, runs));
}
