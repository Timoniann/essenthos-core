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
    /// <summary>
    /// The load, which the API used to run in the background while it served. It is a command here
    /// because that is what it always was: hours of parsing that ends, against a database nobody is
    /// reading yet. Each step is skipped when nothing it reads has moved since it last ran to the end
    /// (<see cref="Loading.Marks.LoadMarks"/>). <c>--from &lt;step&gt;</c> starts at a step, the one a
    /// failed load names; <c>--only &lt;step|module,...&gt;</c> runs only those, then verify;
    /// <c>--no-skip</c> runs every step considered whatever its mark says; <c>--steps</c> lists the steps
    /// with their modules.
    /// </summary>
    private static async Task<int> Load(ForgeRun forge, string[] args)
    {
        using var loadScope = forge.Scope();
        var loader = loadScope.ServiceProvider.GetRequiredService<DatasetLoader>();
        if (args.Contains("--steps"))
        {
            Console.WriteLine(string.Join('\n', loader.StepList().Select(step => $"{step.Name}\t{step.Module}")));
            return 0;
        }

        var loaded = await loader.Run(LoadRequestOf(args), CancellationToken.None);
        await forge.Tidy();
        return loaded ? 0 : 1;
    }

    /// <summary>
    /// <c>--from</c>, <c>--only</c> and <c>--no-skip</c>, each also as <c>--name=value</c>; an empty value is
    /// the same as none, so an action can always pass all three.
    /// </summary>
    internal static LoadRequest LoadRequestOf(string[] args) =>
        new(
            Named(args, "--from"),
            Named(args, "--only")?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            args.Contains("--no-skip") || Named(args, "--no-skip") is { } force && bool.TryParse(force, out var yes) && yes);

    private static string? Named(string[] args, string name) =>
        args.FirstOrDefault(argument => argument.StartsWith(name + "=", StringComparison.Ordinal))?[(name.Length + 1)..]
        ?? (Array.IndexOf(args, name) is var at and >= 0 && at + 1 < args.Length && !args[at + 1].StartsWith("--", StringComparison.Ordinal)
            ? args[at + 1]
            : null);

    /// <summary>
    /// <c>rebuild &lt;database&gt; --compare &lt;reference&gt; --resources &lt;folder&gt; [--resume]</c>: see
    /// <see cref="Loading.Marks.Rebuild"/>. The Resources folder is asked for, not defaulted, because the
    /// load writes review lists into the one it is given.
    /// </summary>
    private static async Task<int> RebuildVerb(ForgeRun forge, string[] args)
    {
        var reference = Named(args, "--compare");
        var resources = Named(args, "--resources");
        if (reference is not { Length: > 0 } || resources is not { Length: > 0 } || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            forge.Logger.LogError("forge rebuild <database> --compare <reference> --resources <folder> [--resume]");
            return 1;
        }

        using var scope = forge.Scope();
        var configuration = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var sessions = int.TryParse(configuration["Loading:FingerprintSessions"], out var configured) && configured > 0 ? configured : 6;
        return await Loading.Marks.Rebuild.Run(
            forge.DatabaseConnection, args[1], reference, Path.GetFullPath(resources), sessions, args.Contains("--resume"), forge.Logger,
            CancellationToken.None);
    }

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

    /// <summary>
    /// The measures as a command, so a build can fail on them. The floor is set below where the corpus
    /// already stands: its job is to catch a load that lost something, not to be an aspiration. What it
    /// finds is recorded, broken or not, because /v1/health reports the latest measures and a fix made
    /// between two loads is otherwise reported as still broken until the next whole load.
    /// </summary>
    private static async Task<int> Verify(ForgeRun forge, string[] args)
    {
        using var verifyScope = forge.Scope();
        var check = verifyScope.ServiceProvider.GetRequiredService<CorpusCheck>();
        var measures = await check.Measure();
        await check.Record(measures);
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
    /// Repair the two known edition boundaries atomically, retaining real words and their evidence.
    /// </summary>
    private static async Task<int> EditionBoundaries(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        var outcome = await scope.ServiceProvider.GetRequiredService<EditionBoundaryRepairLoader>()
            .Load(forge.Resources, CancellationToken.None);
        Console.WriteLine(outcome);
        return 0;
    }

    /// <summary>
    /// The load's swete-restoration step on its own: the verses of Swete and of Ottley's Isaiah read
    /// against the printed page, written into a corpus that holds them as the transcription had them.
    /// Their words are linked again by <c>septuagint-refresh</c>.
    /// </summary>
    private static async Task<int> RestoreSwete(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<SweteRestorationLoader>()
            .Load(Path.Combine(forge.Resources, "Swete"), CancellationToken.None));
        return 0;
    }

    /// <summary>
    /// The notes on the book titles alone, which the load writes at each text's step: the one text
    /// that prints any is read again from its files and nothing else is touched.
    /// </summary>
    private static async Task<int> TitleNotes(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<BookNoteLoader>()
            .Load(KulishTextSource.Read(Path.Combine(forge.Resources, "Kulish")), CancellationToken.None));
        return 0;
    }

    /// <summary>
    /// The notes on the chapter headings alone, which the load writes at each text's step: the one text
    /// that prints any is read again from its files and nothing else is touched.
    /// </summary>
    private static async Task<int> HeadingNotes(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<ChapterNoteLoader>()
            .Load(EbibleTextSource.Read(Path.Combine(forge.Resources, "BiblicaUkrainian2022")), CancellationToken.None));
        return 0;
    }

    /// <summary>
    /// The texts named (every text by default) placed in the frame: each one's frame file written from
    /// its verses as they stand, and its rows of <c>verse_reference</c> filled from the file.
    /// </summary>
    private static async Task<int> Reframe(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        await scope.ServiceProvider.GetRequiredService<DatasetLoader>().Reframe(Slugs(args), CancellationToken.None);
        return 0;
    }

    /// <summary>
    /// The frame files of the texts named (every text by default), written from the verses of the
    /// configured corpus or of <c>--database</c>, which is only read.
    /// </summary>
    private static async Task<int> FrameFiles(ForgeRun forge, string[] args)
    {
        var rules = Loading.Frame.TvtmsReader.Read(Configuration.ResourcePaths.File(forge.Resources, "Versification", "TVTMS.txt"));
        var store = new ProductStore(forge.Resources);
        await using var db = OnDatabase(forge, Option(args, "--database"));
        var loader = new CanonicalFrameLoader(db, Microsoft.Extensions.Logging.Abstractions.NullLogger<CanonicalFrameLoader>.Instance);
        var source = db.Database.GetDbConnection().Database;
        var slugs = Slugs(args);

        foreach (var text in await db.Texts.AsNoTracking().OrderBy(t => t.Slug).ToListAsync())
        {
            if ((slugs is not null && !slugs.Contains(text.Slug)) || !rules.Covers(text.Versification))
            {
                continue;
            }

            var framed = await loader.Framed(text, rules, CancellationToken.None, store);
            CanonicalFrameLoader.Write(text, rules, store, framed, source);
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"{text.Slug}\t{framed.Verses.Count} verses\t{framed.Rows.Count} lines\t{framed.Titles} title rows\t{store.RelativePath(Loading.Frame.FrameFile.Product(text.Slug), ProductKind.Machine)}"));
        }

        store.Save();
        return 0;
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

    private static async Task<int> VerseRefresh(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<VerseLinkLoader>()
            .Refresh(args[1].Split(',').Select(Identifier).ToHashSet()));
        return 0;
    }

    private static async Task<int> SeptuagintRefresh(ForgeRun forge, string[] args)
    {
        var books = args[3].Split(',').Select(int.Parse).ToHashSet();
        if (books.Count == 0 || books.Any(b => b < 1)) throw new InvalidOperationException("Name positive canonical book ordinals.");
        using var scope = forge.Scope();
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<SeptuagintLinkLoader>()
            .Refresh(Identifier(args[1]), Identifier(args[2]), books));
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
    private static async Task<int> ForeignNamesVerb(ForgeRun forge, string[] args)
    {
        using var scope = forge.Scope();
        var db = scope.ServiceProvider.GetRequiredService<Essenthos.Core.Database.AppDbContext>();
        var write = !args.Contains("--dry-run");
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<ForeignNames>().Withdraw(write: write));
        if (!write)
        {
            return 0;
        }

        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<OwnReferenceLoader>().Load());
        Console.WriteLine($"{await Microsoft.EntityFrameworkCore.RelationalDatabaseFacadeExtensions.ExecuteSqlRawAsync(db.Database, DatasetLoader.NamingVerses)} verse naming flags refreshed");
        Console.WriteLine(await scope.ServiceProvider.GetRequiredService<EntityRenderingLoader>().Load());
        return 0;
    }

    private static async Task<int> Carry(ForgeRun forge, string[] args)
    {
        using var carryScope = forge.Scope();
        await carryScope.ServiceProvider.GetRequiredService<AnnotationCarrier>().Carry();
        await forge.Tidy();
        return 0;
    }
}
