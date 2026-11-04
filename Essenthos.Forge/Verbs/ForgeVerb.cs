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
/// <param name="Records">
/// Whether a finished run with these arguments writes what the load does not, and so goes into the
/// recipe the load replays (<see cref="Recipe"/>). Null for a verb that never does.
/// </param>
/// <param name="Relinks">
/// Which texts a finished run with these arguments changed the links or the words of, so their Strong
/// counts are counted again when it ends (<see cref="StrongRenderingLoader"/>). Null for a verb that
/// never does.
/// </param>
internal sealed record ForgeVerb(
    string Name,
    string Arguments,
    string Help,
    Func<ForgeRun, string[], Task<int>> Run,
    int Least = 0,
    int? Most = null,
    Func<string[], bool>? Records = null,
    Func<string[], Relinked>? Relinks = null)
{
    public bool Accepts(string[] args) => args.Length - 1 >= Least && (Most is not { } most || args.Length - 1 <= most);

    /// <summary>
    /// The texts to count again after a finished run of these arguments. Nothing for a trial, and
    /// nothing for a step the load replays from its recipe: the load counts every text after it.
    /// </summary>
    public Relinked Changed(string[] args) =>
        Relinks is null || args.Contains("--dry-run") || Environment.GetEnvironmentVariable(Recipe.Replaying) is not null
            ? Relinked.None
            : Relinks(args);

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
    /// The Strong counts of the texts a run changed, counted again, so the Strong pages do not keep the
    /// numbers of links that are gone until the next load.
    /// </summary>
    public async Task Recount(Relinked relinked)
    {
        if (relinked.Nothing)
        {
            return;
        }

        using var scope = Scope();
        logger.LogInformation("Counting the Strong pages again for {Texts}", relinked);
        logger.LogInformation(
            "{Outcome}", await scope.ServiceProvider.GetRequiredService<StrongRenderingLoader>().Load(relinked));
    }

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
