using Essenthos.Core.Loading.Links.Evidentia;

namespace Essenthos.Core.Verbs;

internal static partial class ForgeVerbs
{
    /// <summary>
    /// Read one real chapter through the deterministic evidence graph. Unlike <c>align</c>, this command
    /// never writes links: its result tells us whether the current language packs have enough evidence
    /// to justify a future mapping pass and exactly where IBM fallback would be required.
    /// </summary>
    private static async Task<int> EvidentiaPreview(ForgeRun forge, string[] args)
    {
        if (!int.TryParse(args[3], out var book) || !int.TryParse(args[4], out var chapter))
        {
            throw new ArgumentException("evidentia-preview needs numeric canonical book and chapter.");
        }

        var verseIndex = Array.IndexOf(args, "--verse");
        int? verse = verseIndex >= 0 && verseIndex + 1 < args.Length
            ? int.Parse(args[verseIndex + 1])
            : null;
        using var previewScope = forge.Scope();
        var preview = await previewScope.ServiceProvider.GetRequiredService<EvidentiaCorpusPreviewLoader>().Preview(
            Identifier(args[1]), Identifier(args[2]), book, chapter, verse,
            allowSourceStrongEvidence: !args.Contains("--without-source-strong"),
            allowKnownRenderingEvidence: !args.Contains("--without-known-renderings"));
        forge.Logger.LogInformation("\n{Preview}", preview);
        return 0;
    }

    private static async Task<int> EvidentiaMeasure(ForgeRun forge, string[] args)
    {
        if (!int.TryParse(args[3], out var book) || !int.TryParse(args[4], out var chapter))
        {
            throw new ArgumentException("evidentia-measure needs numeric canonical book and chapter.");
        }

        using var measureScope = forge.Scope();
        var measureOptions = await measureScope.ServiceProvider.GetRequiredService<EvidentiaRunner>()
            .WithConfirmed(EvidentiaOptions(args, forge.Resources));
        var measurement = await measureScope.ServiceProvider.GetRequiredService<EvidentiaCorpusPreviewLoader>().MeasureChapter(
            Identifier(args[1]), Identifier(args[2]), book, chapter, measureOptions);
        await WriteRows(args, "--disagreements", measurement.Disagreements);
        await WriteRows(args, "--words", measurement.Words);
        await WriteRows(args, "--absences", measurement.Absences);
        await WriteRows(args, "--key-doubts", measurement.KeyDoubts);
        measureOptions.Learns?.Write(OptionalText(args, "--confirmed-out")!);
        forge.Logger.LogInformation("\n{Measurement}", measurement);
        return 0;
    }

    private static async Task<int> EvidentiaMeasureBook(ForgeRun forge, string[] args)
    {
        if (!int.TryParse(args[3], out var book))
        {
            throw new ArgumentException("evidentia-measure-book needs a numeric canonical book.");
        }

        using var measureBookScope = forge.Scope();
        var fromChapter = OptionalInt(args, "--from-chapter");
        var toChapter = OptionalInt(args, "--to-chapter");
        if (fromChapter.HasValue && toChapter.HasValue && fromChapter > toChapter)
        {
            throw new ArgumentException("evidentia-measure-book needs --from-chapter less than or equal to --to-chapter.");
        }

        var measureBookOptions = await measureBookScope.ServiceProvider.GetRequiredService<EvidentiaRunner>()
            .WithConfirmed(EvidentiaOptions(args, forge.Resources));
        var measurement = await measureBookScope.ServiceProvider.GetRequiredService<EvidentiaCorpusPreviewLoader>().MeasureBook(
            Identifier(args[1]), Identifier(args[2]), book, measureBookOptions,
            firstChapter: fromChapter,
            lastChapter: toChapter);
        await WriteRows(args, "--disagreements", measurement.Disagreements);
        await WriteRows(args, "--words", measurement.Words);
        await WriteRows(args, "--absences", measurement.Absences);
        await WriteRows(args, "--key-doubts", measurement.KeyDoubts);
        measureBookOptions.Learns?.Write(OptionalText(args, "--confirmed-out")!);
        forge.Logger.LogInformation("\n{Measurement}", measurement);
        return 0;
    }

    /// <summary>
    /// A stored run: the same measurement, with every decision kept under a run id so it can be ranked,
    /// reviewed and compared. It writes the run's own tables and nothing of the corpus. --parallel N computes
    /// up to N books at once (default 4, at most 4) and stores them in book order, so the run is the one a
    /// serial run would store, ids included.
    /// </summary>
    private static async Task<int> EvidentiaRun(ForgeRun forge, string[] args)
    {
        var scopes = Positional(args, 3).Select(EvidentiaBookScope.Parse).ToList();
        if (scopes.Count == 0)
        {
            throw new ArgumentException(
                "evidentia-run needs at least one canonical book, optionally with chapters: evidentia-run BSB BHSA 1:1-10 8 32.");
        }

        using var runScope = forge.Scope();
        forge.Logger.LogInformation("\n{Outcome}", await runScope.ServiceProvider.GetRequiredService<EvidentiaRunner>()
            .Run(Identifier(args[1]), Identifier(args[2]), scopes, EvidentiaOptions(args, forge.Resources),
                OptionalInt(args, "--parallel") ?? EvidentiaRunner.DefaultParallel));
        return 0;
    }

    private static async Task<int> EvidentiaRuns(ForgeRun forge, string[] args)
    {
        using var runsScope = forge.Scope();
        forge.Logger.LogInformation("\n{Runs}", await runsScope.ServiceProvider.GetRequiredService<EvidentiaRunner>().List());
        return 0;
    }

    private static async Task<int> EvidentiaQueue(ForgeRun forge, string[] args)
    {
        using var queueScope = forge.Scope();
        forge.Logger.LogInformation("\n{Queue}", await queueScope.ServiceProvider.GetRequiredService<EvidentiaReviewQueue>()
            .Pending(int.Parse(args[1]), QueueFilter(args)));
        return 0;
    }

    /// <summary>A verdict is recorded and goes no further; evidentia-apply is the only thing that writes a link.</summary>
    private static async Task<int> EvidentiaVerdict(ForgeRun forge, string[] args)
    {
        using var verdictScope = forge.Scope();
        var queue = verdictScope.ServiceProvider.GetRequiredService<EvidentiaReviewQueue>();
        var ids = Positional(args, 1).Select(long.Parse).ToList();
        var reviewer = OptionalText(args, "--reviewer") ?? string.Empty;
        var approving = args[0] == "evidentia-approve";
        var settled = approving
            ? await queue.Approve(ids, reviewer, OptionalText(args, "--note"))
            : await queue.Reject(ids, reviewer, OptionalText(args, "--note"));
        forge.Logger.LogInformation(
            "{Settled} decisions {Verdict}; nothing reaches the corpus until evidentia-apply --write",
            settled, approving ? "approved" : "rejected");
        await forge.Record(verdictScope, await queue.RunOf(ids.First()));
        return 0;
    }

    private static async Task<int> EvidentiaCorrect(ForgeRun forge, string[] args)
    {
        var (correctDecision, correctTarget) = (args[1], args[2]);
        using var correctScope = forge.Scope();
        await correctScope.ServiceProvider.GetRequiredService<EvidentiaReviewQueue>().Correct(
            long.Parse(correctDecision), long.Parse(correctTarget),
            OptionalText(args, "--reviewer") ?? string.Empty, OptionalText(args, "--note"));
        forge.Logger.LogInformation(
            "Decision {Decision} corrected to word {Target}; nothing reaches the corpus until evidentia-apply --write",
            correctDecision, correctTarget);
        await forge.Record(correctScope,
            await correctScope.ServiceProvider.GetRequiredService<EvidentiaReviewQueue>().RunOf(long.Parse(correctDecision)));
        return 0;
    }

    private static async Task<int> EvidentiaAcceptTier(ForgeRun forge, string[] args)
    {
        var acceptRun = args[1];
        using var acceptScope = forge.Scope();
        var accepted = await acceptScope.ServiceProvider.GetRequiredService<EvidentiaReviewQueue>().AcceptTier(
            int.Parse(acceptRun), QueueFilter(args), OptionalText(args, "--reviewer") ?? string.Empty,
            OptionalText(args, "--note"));
        forge.Logger.LogInformation(
            "{Accepted} proposals accepted with their tier, unread. They will be written as rule-based claims, never as a " +
            "person's, and only by evidentia-apply --write", accepted);
        await forge.Record(acceptScope, int.Parse(acceptRun));
        return 0;
    }

    /// <summary>
    /// The one command that writes EVIDENTIA's output into the corpus, and only what a reviewer approved
    /// or corrected. Without --write it reports what it would write and writes nothing.
    /// </summary>
    private static async Task<int> EvidentiaApply(ForgeRun forge, string[] args)
    {
        using var applyScope = forge.Scope();
        forge.Logger.LogInformation("\n{Outcome}", await applyScope.ServiceProvider.GetRequiredService<EvidentiaLinkWriter>()
            .Apply(int.Parse(args[1]), args.Contains("--write")));
        if (args.Contains("--write"))
        {
            await forge.Tidy();
        }

        return 0;
    }

    /// <summary>
    /// The verdicts of every run that has any, or of the runs named, written to the ledger under
    /// Resources/Essenthos/evidentia. The verbs that record a verdict do this themselves.
    /// </summary>
    private static async Task<int> EvidentiaExport(ForgeRun forge, string[] args)
    {
        using var exportScope = forge.Scope();
        var ledger = exportScope.ServiceProvider.GetRequiredService<EvidentiaLedger>();
        var named = Positional(args, 1).Select(int.Parse).ToList();
        if (named.Count == 0)
        {
            foreach (var outcome in await ledger.ExportAll(forge.Resources))
            {
                forge.Logger.LogInformation("{Outcome}", outcome);
            }
        }

        foreach (var run in named)
        {
            forge.Logger.LogInformation("{Outcome}", await ledger.Export(run, forge.Resources));
        }

        return 0;
    }

    /// <summary>
    /// The ledger's verdicts put back into the corpus: a run it does not hold written again and applied,
    /// a verdict whose link was deleted written again. What every load does as one of its steps.
    /// </summary>
    private static async Task<int> EvidentiaReplay(ForgeRun forge, string[] args)
    {
        using var replayScope = forge.Scope();
        forge.Logger.LogInformation("\n{Outcome}", await replayScope.ServiceProvider.GetRequiredService<EvidentiaLedger>()
            .Replay(forge.Resources));
        return 0;
    }

    private static async Task<int> EvidentiaProblems(ForgeRun forge, string[] args)
    {
        using var problemScope = forge.Scope();
        forge.Logger.LogInformation("\n{Problems}", await problemScope.ServiceProvider.GetRequiredService<EvidentiaProblemVerses>()
            .Worst(
                [.. args[1].Split(',').Select(int.Parse)],
                OptionalInt(args, "--take") ?? EvidentiaProblemVerses.DefaultTake,
                OptionalInt(args, "--min-words") ?? EvidentiaProblemVerses.DefaultMinimumContentWords,
                args.Contains("--flagged")));
        return 0;
    }

    private static async Task<int> EvidentiaRerun(ForgeRun forge, string[] args)
    {
        using var rerunScope = forge.Scope();
        forge.Logger.LogInformation("\n{Rerun}", await rerunScope.ServiceProvider.GetRequiredService<EvidentiaProblemVerses>()
            .Rerun(
                int.Parse(args[1]),
                OptionalInt(args, "--take") ?? EvidentiaProblemVerses.DefaultTake,
                OptionalInt(args, "--min-words") ?? EvidentiaProblemVerses.DefaultMinimumContentWords));
        return 0;
    }

    private static async Task<int> EvidentiaCompare(ForgeRun forge, string[] args)
    {
        using var compareScope = forge.Scope();
        forge.Logger.LogInformation("\n{Comparison}", await compareScope.ServiceProvider.GetRequiredService<EvidentiaProblemVerses>()
            .Compare(int.Parse(args[1]), int.Parse(args[2])));
        return 0;
    }
}
