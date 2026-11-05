using System.Globalization;
using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Products;
using Essenthos.Core.Publishing;
using Essenthos.Core.Verification;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Verbs;

internal static partial class ForgeVerbs
{
    private static async Task<int> LexiconTranslations(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        var outcome = await scope.ServiceProvider.GetRequiredService<StrongTranslationLoader>().Load(forge.Resources);
        forge.Logger.LogInformation("{Outcome}", outcome);
        return outcome.NoTranslations || outcome.Refused.Total > 0 ? 1 : 0;
    }

    /// <summary>
    /// VACUUM (ANALYZE) of the tables that need it, or of the ones named. The steps that write millions of
    /// rows end with this themselves; on its own it is for after a step that does not.
    /// </summary>
    private static async Task<int> Maintain(ForgeRun forge, string[] args)
    {
        forge.Logger.LogInformation("{Outcome}", await Maintenance.Tidy(forge.DatabaseConnection, args[1..]));
        return 0;
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

    private static async Task<int> Export(ForgeRun forge, string[] args)
    {
        using var exportScope = forge.Scope();
        return await exportScope.ServiceProvider.GetRequiredService<TextExporter>()
            .Export(Option(args, "--to"), args.Contains("--dry-run"), CancellationToken.None);
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

    private static async Task<int> PublishDownloads(ForgeRun forge, string[] args)
    {
        using var downloadsScope = forge.Scope();
        return await downloadsScope.ServiceProvider.GetRequiredService<Publisher>().PublishDownloads(
            Option(args, "--to") ?? throw new InvalidOperationException("forge publish-downloads --to <target> [--from <folder>]"),
            Option(args, "--from"),
            args.Contains("--dry-run"),
            CancellationToken.None);
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
    /// The frame files of the texts named (every text by default) whose inputs moved, made from products alone
    /// (<see cref="Loading.Frame.FrameProducts"/>): each text's verse manifest, words, titles and declaration, and the
    /// versification data. Reads no database.
    /// </summary>
    private static Task<int> FrameFiles(ForgeRun forge, string[] args)
    {
        var rules = Loading.Frame.TvtmsReader.Read(Configuration.ResourcePaths.File(forge.Resources, "Versification", "TVTMS.txt"));
        var store = new ProductStore(Option(args, "--from") ?? forge.Resources);
        var outcomes = Loading.Frame.FrameProducts.Write(store, rules, Slugs(args), args.Contains("--force"), Parallelism(args, 16));
        foreach (var outcome in outcomes)
        {
            Console.WriteLine(outcome.Written
                ? string.Create(CultureInfo.InvariantCulture,
                    $"{outcome.Slug}	{outcome.Verses} verses	{outcome.Lines} lines	{outcome.Titles} title rows	{store.RelativePath(Loading.Frame.FrameFile.Product(outcome.Slug), ProductKind.Machine)}")
                : $"{outcome.Slug}	current");
        }

        var (unlisted, unheld) = Loading.Frame.FrameProducts.CheckTitled(store, Loading.Frame.FrameProducts.Texts(store));
        if (unlisted.Count + unheld.Count > 0)
        {
            forge.Logger.LogWarning("{Product}: frames place a verse at the title of {Unlisted}, which it does not name; it names {Unheld}, where none does",
                Loading.Frame.FrameProducts.TitledPsalms, string.Join(' ', unlisted), string.Join(' ', unheld));
        }

        store.Save();
        return Task.FromResult(0);
    }

    /// <summary>Every text's rows of <c>verse_reference</c> filled from its frame file, in the configured corpus or <c>--database</c>.</summary>
    private static async Task<int> FrameLoad(ForgeRun forge, string[] args)
    {
        var store = new ProductStore(forge.Resources);
        await using var db = OnDatabase(forge, Option(args, "--database"));
        var loader = new CanonicalFrameLoader(db, forge.Logger is ILogger<CanonicalFrameLoader> typed
            ? typed
            : Microsoft.Extensions.Logging.Abstractions.NullLogger<CanonicalFrameLoader>.Instance);
        var slugs = Slugs(args);

        foreach (var text in await db.Texts.AsNoTracking().OrderBy(t => t.Slug).ToListAsync())
        {
            if ((slugs is not null && !slugs.Contains(text.Slug)) || !store.Exists(Loading.Frame.FrameFile.Product(text.Slug)))
            {
                continue;
            }

            Console.WriteLine(await loader.Fill(text, store));
        }

        return 0;
    }

    /// <summary>The texts named before the first option, or null for every text.</summary>
    private static HashSet<string>? Slugs(string[] args)
    {
        var named = args.Skip(1).TakeWhile(arg => !arg.StartsWith("--", StringComparison.Ordinal))
            .SelectMany(arg => arg.Split(',', StringSplitOptions.RemoveEmptyEntries)).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return named.Count == 0 ? null : named;
    }

    /// <summary>
    /// Every text's frame computed as <c>frame</c> would write it, set against the rows the corpus holds:
    /// a row only one side has is a difference, except a psalm title's further row, which the passes after
    /// the frame write (<see cref="CanonicalFrameLoader.Unchanged"/>). Exits 1 when a text differs.
    /// </summary>
    private static async Task<int> FrameCheck(ForgeRun forge, string[] args)
    {
        var path = Option(args, "--out") is { } file ? Path.GetFullPath(file) : null;
        var rules = Loading.Frame.TvtmsReader.Read(Configuration.ResourcePaths.File(forge.Resources, "Versification", "TVTMS.txt"));

        await using var db = OnDatabase(forge, Option(args, "--database"));
        var loader = new CanonicalFrameLoader(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<CanonicalFrameLoader>.Instance);
        Console.WriteLine($"The frame of {db.Database.GetDbConnection().Database}, by the rules under {rules.Data.Store.ResourcesPath}");
        await using var output = path is null ? null : new StreamWriter(path, false, new UTF8Encoding(false)) { NewLine = "\n" };
        output?.WriteLine("text\tbook\tchapter\tverse\tlabel\tcanonical_book\tcanonical_chapter\tcanonical_verse\tprimary");

        var differing = 0;
        foreach (var text in await db.Texts.AsNoTracking().OrderBy(t => t.Slug).ToListAsync())
        {
            if (!rules.Covers(text.Versification))
            {
                Console.WriteLine($"{text.Slug}\tnot covered");
                continue;
            }

            var (verses, existing, expected) = await loader.Compute(text, rules);
            var wanted = expected.ToHashSet();
            var held = existing.ToHashSet();
            var missing = wanted.Count(reference => !held.Contains(reference));
            var extra = held.Where(reference => !wanted.Contains(reference)).ToList();
            var titles = extra.Count(reference => !reference.IsPrimary && reference.Verse == Loading.Frame.CanonicalReference.TitleVerse);
            var unchanged = CanonicalFrameLoader.Unchanged(existing, expected);
            if (!unchanged)
            {
                differing++;
            }

            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{text.Slug}\t{verses.Count} verses\t{expected.Count} computed\t{existing.Count} held\t{missing} missing\t{extra.Count - titles} extra\t{titles} title rows of later passes\t{(unchanged ? "unchanged" : "DIFFERS")}"));

            if (output is null)
            {
                continue;
            }

            var byId = verses.ToDictionary(verse => verse.Id);
            foreach (var reference in expected
                         .OrderBy(r => byId[r.VerseId].Book).ThenBy(r => byId[r.VerseId].Chapter)
                         .ThenBy(r => byId[r.VerseId].Number).ThenBy(r => byId[r.VerseId].Label, StringComparer.Ordinal)
                         .ThenByDescending(r => r.IsPrimary).ThenBy(r => r.Book).ThenBy(r => r.Chapter).ThenBy(r => r.Verse))
            {
                var verse = byId[reference.VerseId];
                output.WriteLine(string.Create(CultureInfo.InvariantCulture,
                    $"{text.Slug}\t{verse.Book}\t{verse.Chapter}\t{verse.Number}\t{verse.Label}\t{reference.Book}\t{reference.Chapter}\t{reference.Verse}\t{(reference.IsPrimary ? 1 : 0)}"));
            }
        }

        Console.WriteLine($"{differing} texts differ from the frame the corpus holds");
        return differing == 0 ? 0 : 1;
    }

    /// <summary>
    /// The configured corpus, or another database on the same server named by <c>--database</c>, for a
    /// verb that only reads: the snapshot a rework export is taken from, a copy being proved.
    /// </summary>
    private static AppDbContext OnDatabase(ForgeRun forge, string? database)
    {
        var connection = new Npgsql.NpgsqlConnectionStringBuilder(forge.DatabaseConnection);
        if (database is not null)
        {
            connection.Database = database;
        }

        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connection.ConnectionString, npgsql => npgsql.CommandTimeout(1800)).Options);
    }

    private static async Task<int> VerseLinks(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<VerseLinkLoader>().Load());
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
}
