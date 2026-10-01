using System.Globalization;
using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Publishing;
using Essenthos.Core.Verification;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Verbs;

internal static partial class ForgeVerbs
{
    /// <summary>
    /// The load, which the API used to run in the background while it served. It is a command here
    /// because that is what it always was: hours of parsing that ends, against a database nobody is
    /// reading yet. Each source checks whether it is already there and does nothing if it is, so running
    /// it twice is how a new witness is added. <c>--from &lt;step&gt;</c> starts at a step, the one a
    /// failed load names; <c>--steps</c> lists them.
    /// </summary>
    private static async Task<int> Load(ForgeRun forge, string[] args)
    {
        using var loadScope = forge.Scope();
        var loader = loadScope.ServiceProvider.GetRequiredService<DatasetLoader>();
        if (args.Contains("--steps"))
        {
            forge.Logger.LogInformation("\n{Steps}", string.Join('\n', loader.StepNames()));
            return 0;
        }

        var loaded = await loader.Run(LoadFrom(args), CancellationToken.None);
        await forge.Tidy();
        return loaded ? 0 : 1;
    }

    /// <summary><c>--from &lt;step&gt;</c> or <c>--from=&lt;step&gt;</c>; an empty one is the first step, so an action can always pass it.</summary>
    private static string? LoadFrom(string[] args) =>
        args.FirstOrDefault(argument => argument.StartsWith("--from=", StringComparison.Ordinal))?["--from=".Length..]
        ?? Option(args, "--from");

    /// <summary>
    /// VACUUM (ANALYZE) of the tables that need it, or of the ones named. The steps that write millions of
    /// rows end with this themselves; on its own it is for after a step that does not.
    /// </summary>
    private static async Task<int> Maintain(ForgeRun forge, string[] args)
    {
        forge.Logger.LogInformation("{Outcome}", await Maintenance.Tidy(forge.DatabaseConnection, args[1..]));
        return 0;
    }

    /// <summary>
    /// The measures as a command, so a build can fail on them. The floor is set below where the corpus
    /// already stands: its job is to catch a load that lost something, not to be an aspiration.
    /// </summary>
    private static async Task<int> Verify(ForgeRun forge, string[] args)
    {
        using var verifyScope = forge.Scope();
        var check = verifyScope.ServiceProvider.GetRequiredService<CorpusCheck>();
        var measures = await check.Measure();
        var floor = Array.IndexOf(args, "--floor") is var at and >= 0 && at + 1 < args.Length
            ? double.Parse(args[at + 1], CultureInfo.InvariantCulture)
            : CorpusCheck.RenderedFloor;

        forge.Logger.LogInformation("\n{Report}", measures.Describe());
        return CorpusGate.Pass(measures, floor, forge.Logger) ? 0 : 1;
    }

    // A corpus release, and moving one to a server. See Publishing/Publisher.cs for the whole design;
    // in short: `release` verifies and dumps this machine's corpus into .releases/, `publish` restores
    // that file into a new database on a target, verifies it there, and only then swaps it in. Each of
    // release, publish and rollback takes --dry-run, which says what it would do and changes nothing.
    private static async Task<int> Release(ForgeRun forge, string[] args)
    {
        using var releaseScope = forge.Scope();
        return await releaseScope.ServiceProvider.GetRequiredService<Publisher>()
            .Release(args.Contains("--allow-dirty"), CancellationToken.None, dryRun: args.Contains("--dry-run"),
                allowUnrecorded: args.Contains("--allow-unrecorded"));
    }

    private static async Task<int> Publish(ForgeRun forge, string[] args)
    {
        using var publishScope = forge.Scope();
        return await publishScope.ServiceProvider.GetRequiredService<Publisher>().Publish(
            Option(args, "--to") ?? throw new InvalidOperationException("forge publish --to <target> [--release <name>]"),
            Option(args, "--release"),
            args.Contains("--without-rehearsal"),
            CancellationToken.None,
            dryRun: args.Contains("--dry-run"));
    }

    private static async Task<int> Rollback(ForgeRun forge, string[] args)
    {
        using var rollbackScope = forge.Scope();
        return await rollbackScope.ServiceProvider.GetRequiredService<Publisher>().Rollback(
            Option(args, "--to") ?? throw new InvalidOperationException("forge rollback --to <target>"),
            CancellationToken.None,
            dryRun: args.Contains("--dry-run"));
    }

    private static async Task<int> Releases(ForgeRun forge, string[] args)
    {
        using var releasesScope = forge.Scope();
        return await releasesScope.ServiceProvider.GetRequiredService<Publisher>().List(
            Option(args, "--on"), CancellationToken.None);
    }

    /// <summary>
    /// The recorded runs alone, on a corpus already loaded: <c>recipe</c> lists them, <c>recipe --run</c>
    /// runs what the corpus does not already hold, as the load does after its own linking steps.
    /// </summary>
    private static async Task<int> RecipeVerb(ForgeRun forge, string[] args)
    {
        using var recipeScope = forge.Scope();
        if (!args.Contains("--run"))
        {
            var recipeDb = recipeScope.ServiceProvider.GetRequiredService<AppDbContext>();
            await recipeDb.Database.OpenConnectionAsync();
            var listed = new StringBuilder();
            foreach (var step in Recipe.Read(forge.Resources))
            {
                var already = await Recipe.AlreadyThere(
                    (Npgsql.NpgsqlConnection)recipeDb.Database.GetDbConnection(), step, CancellationToken.None);
                listed.AppendLine(already is null ? $"  would run  {step}" : $"  skipped    {step}: {already}");
            }

            forge.Logger.LogInformation("\n{Steps}", listed);
            return 0;
        }

        await recipeScope.ServiceProvider.GetRequiredService<DatasetLoader>().FollowTheRecipe(forge.Resources, CancellationToken.None);
        forge.Logger.LogInformation(
            "{Outcome}", await recipeScope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
        await recipeScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
        return 0;
    }

    /// <summary>
    /// One text read again from its source after a change to how it is read, without the whole load.
    /// Its word links are deleted with it; align and compose it again afterwards.
    /// </summary>
    private static async Task<int> Reload(ForgeRun forge, string[] args)
    {
        using var reloadScope = forge.Scope();
        await reloadScope.ServiceProvider.GetRequiredService<DatasetLoader>()
            .Reload(Identifier(args[1]), CancellationToken.None);
        return 0;
    }

    /// <summary>
    /// The words bible4u's King James, Synodal and Ohienko print wrong, corrected in a corpus that loaded
    /// them before the reader did it: word rows kept wherever the word is the same, the corrected verses
    /// linked again by the sources that state what their words render, then the verse links and the carry.
    /// Brenton's Greek verses are begun where his English begins them the same way, their words keeping
    /// their rows and losing their links until the pairs are aligned again.
    /// BHSA's headwords and the Berean's Strong numbers are put in place first, the same way.
    /// </summary>
    private static async Task<int> Correct(ForgeRun forge, string[] args)
    {
        using var correctScope = forge.Scope();
        await correctScope.ServiceProvider.GetRequiredService<DatasetLoader>().Correct(CancellationToken.None);
        return 0;
    }

    /// <summary>
    /// What the editions print about their own words — supplied, doubtful, a subscription — written as
    /// word groups in place, over a corpus whose words and links are already there.
    /// </summary>
    private static async Task<int> Marks(ForgeRun forge, string[] args)
    {
        using var marksScope = forge.Scope();
        await marksScope.ServiceProvider.GetRequiredService<DatasetLoader>().Marks(CancellationToken.None);
        return 0;
    }

    /// <summary>
    /// What each text was translated from, revised from or shares a tradition with, made to match the
    /// list kept by hand. The load does this once the texts are in; this is that step alone.
    /// </summary>
    private static async Task<int> Relations(ForgeRun forge, string[] args)
    {
        using var relationScope = forge.Scope();
        Console.WriteLine(await relationScope.ServiceProvider.GetRequiredService<TextRelationLoader>().Load());
        return 0;
    }

    /// <summary>
    /// Every annotation a pass carried into another text, carried again over the links as they stand.
    /// For a corpus whose links were changed by something that did not carry them itself.
    /// </summary>
    private static async Task<int> Carry(ForgeRun forge, string[] args)
    {
        using var carryScope = forge.Scope();
        await carryScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
        await forge.Tidy();
        return 0;
    }
}
