using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Links;

internal sealed record AlignmentOutcome(
    string From,
    string To,
    int Verses,
    int Proposed,
    int Collapsed,
    int BelowThreshold,
    int Written,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        $"{From} to {To}: {Written} links from {Proposed} proposed over {Verses} verses in {Elapsed} — " +
        $"{BelowThreshold} below the threshold, {Collapsed} in collapsed clusters";
}

/// <summary>
/// Aligns two texts statistically and writes what survives as links.
///
/// The model is trained and run by SIL's <c>machine</c> tool rather than in this process, because
/// the same library crashes the process above a few hundred verses through its own API and does all
/// twenty-three thousand without complaint through the tool. Alignment is computed once per pair of
/// texts, so a batch step that writes a file is the right shape anyway.
///
/// Nothing it produces is stated by anyone. Every link carries <c>aligner</c> and the model's own
/// confidence, and the schema makes it impossible to store one as though a source had said it.
/// </summary>
internal sealed class AlignmentPipeline(AppDbContext db, ILogger<AlignmentPipeline> logger)
{
    /// <summary>
    /// Measured against the correspondences a source states for the King James and BHSA: at this
    /// threshold roughly four in five links on content words are ones the source agrees with, and
    /// about half of what the model proposes survives. Below it precision falls away without
    /// buying much coverage.
    /// </summary>
    public const double DefaultMinimumConfidence = 0.5;

    /// <summary>
    /// When this many source words all point at one target word and none of them confidently, the
    /// model has run out of signal and is dumping the verse onto whatever it can reach. It is not
    /// an alignment and it looks like one.
    /// </summary>
    private const int CollapsedCluster = 4;

    private const string LinkImport =
        """
        COPY link (id, from_text_id, to_text_id, relation, method, confidence, source)
        FROM STDIN (FORMAT BINARY)
        """;

    private const string LinkWordImport =
        "COPY link_word (link_id, word_id, side) FROM STDIN (FORMAT BINARY)";

    public async Task<AlignmentOutcome> Run(
        string fromSlug,
        string toSlug,
        string workspace,
        double minimumConfidence,
        string modelType,
        CancellationToken cancellationToken = default)
    {
        var from = await Text(fromSlug, cancellationToken);
        var to = await Text(toSlug, cancellationToken);

        if (await db.Links.AnyAsync(
                l => l.FromTextId == from.Id && l.ToTextId == to.Id && l.Method == LinkMethod.Aligner,
                cancellationToken))
        {
            logger.LogInformation("{From} and {To} are already aligned; nothing to do", fromSlug, toSlug);
            return new AlignmentOutcome(fromSlug, toSlug, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        Directory.CreateDirectory(workspace);

        var source = await Words(fromSlug, Surface, cancellationToken);
        var target = await Words(toSlug, Comparable, cancellationToken);
        var addresses = source.Keys.Intersect(target.Keys).OrderBy(a => a).ToList();
        if (addresses.Count == 0)
        {
            throw new InvalidOperationException(
                $"\"{fromSlug}\" and \"{toSlug}\" share no verse in the canonical frame. Both texts have to be " +
                "loaded and placed before they can be aligned.");
        }

        var sourceFile = Path.Combine(workspace, "source.txt");
        var targetFile = Path.Combine(workspace, "target.txt");
        var alignmentFile = Path.Combine(workspace, "alignment", "pharaoh.txt");
        var modelPrefix = Path.Combine(workspace, "model", $"{fromSlug}-{toSlug}");

        Write(sourceFile, addresses, source);
        Write(targetFile, addresses, target);
        Directory.CreateDirectory(Path.GetDirectoryName(alignmentFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(modelPrefix)!);

        logger.LogInformation("Training {Model} over {Verses} verse pairs", modelType, addresses.Count);
        await Machine(["train", "alignment-model", "-mt", modelType, modelPrefix, sourceFile, targetFile],
            cancellationToken);
        await Machine(
            ["align", "-mt", modelType, "-sh", "och", "-s", modelPrefix, sourceFile, targetFile, alignmentFile],
            cancellationToken);

        var (drafts, proposed, collapsed, below) =
            Read(alignmentFile, addresses, source, target, minimumConfidence);

        await Store(from.Id, to.Id, modelType, drafts, cancellationToken);

        var outcome = new AlignmentOutcome(
            fromSlug, toSlug, addresses.Count, proposed, collapsed, below, drafts.Count, started.Elapsed);
        logger.LogInformation("Aligned {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Reads the tool's Pharaoh output — <c>source-target:translation:alignment</c> per pair — and
    /// keeps what is worth keeping.
    /// </summary>
    private static (List<AlignedDraft> Drafts, int Proposed, int Collapsed, int Below) Read(
        string path,
        List<(int, int, int)> addresses,
        Dictionary<(int, int, int), List<Word>> source,
        Dictionary<(int, int, int), List<Word>> target,
        double minimumConfidence)
    {
        var drafts = new List<AlignedDraft>(300_000);
        var proposed = 0;
        var collapsed = 0;
        var below = 0;
        var line = 0;

        foreach (var text in File.ReadLines(path))
        {
            if (line >= addresses.Count)
            {
                break;
            }

            var address = addresses[line++];
            var sourceWords = source[address];
            var targetWords = target[address];
            var verse = new List<(int Source, int Target, double Translation, double Alignment)>(24);

            foreach (var token in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = token.Split(':');
                var indices = parts[0].Split('-');
                if (indices.Length != 2 ||
                    !int.TryParse(indices[0], out var s) || !int.TryParse(indices[1], out var t) ||
                    s >= sourceWords.Count || t >= targetWords.Count)
                {
                    continue;
                }

                proposed++;
                verse.Add((s, t, Score(parts, 1), Score(parts, 2)));
            }

            var crowded = verse
                .GroupBy(pair => pair.Target)
                .Where(group => group.Count() >= CollapsedCluster && group.All(p => p.Translation < minimumConfidence))
                .Select(group => group.Key)
                .ToHashSet();

            foreach (var pair in verse)
            {
                if (crowded.Contains(pair.Target))
                {
                    collapsed++;
                    continue;
                }

                if (pair.Translation < minimumConfidence)
                {
                    below++;
                    continue;
                }

                drafts.Add(new AlignedDraft(
                    sourceWords[pair.Source].Id, targetWords[pair.Target].Id, pair.Translation, pair.Alignment));
            }
        }

        return (drafts, proposed, collapsed, below);
    }

    private static double Score(string[] parts, int index) =>
        parts.Length > index && double.TryParse(parts[index], NumberStyles.Float, CultureInfo.InvariantCulture,
            out var value)
            ? value
            : 0;

    private async Task Store(
        int fromTextId,
        int toTextId,
        string modelType,
        List<AlignedDraft> drafts,
        CancellationToken cancellationToken)
    {
        if (drafts.Count == 0)
        {
            return;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        var firstId = await ReserveLinkIds(connection, drafts.Count, cancellationToken);
        var renders = EnumSpelling.Of(LinkRelation.Renders);
        var method = EnumSpelling.Of(LinkMethod.Aligner);
        var fromSide = EnumSpelling.Of(LinkSide.From);
        var toSide = EnumSpelling.Of(LinkSide.To);

        // The model reports how likely the word pairing is and how likely the position is. The
        // schema has one confidence, so the pairing is what it holds and the position rides along
        // in the source, where it stays readable rather than being averaged away.
        await using (var writer = await connection.BeginBinaryImportAsync(LinkImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                await writer.StartRowAsync(cancellationToken);
                await writer.WriteAsync(firstId + i, NpgsqlDbType.Bigint, cancellationToken);
                await writer.WriteAsync(fromTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(toTextId, NpgsqlDbType.Integer, cancellationToken);
                await writer.WriteAsync(renders, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(method, NpgsqlDbType.Text, cancellationToken);
                await writer.WriteAsync(drafts[i].Translation, NpgsqlDbType.Double, cancellationToken);
                await writer.WriteAsync(
                    $"SIL.Machine {modelType}, symmetrised och, position {drafts[i].Position:F4}",
                    NpgsqlDbType.Text, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await using (var writer = await connection.BeginBinaryImportAsync(LinkWordImport, cancellationToken))
        {
            for (var i = 0; i < drafts.Count; i++)
            {
                await Row(writer, firstId + i, drafts[i].SourceWordId, fromSide, cancellationToken);
                await Row(writer, firstId + i, drafts[i].TargetWordId, toSide, cancellationToken);
            }

            await writer.CompleteAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task Row(
        NpgsqlBinaryImporter writer,
        long linkId,
        long wordId,
        string side,
        CancellationToken cancellationToken)
    {
        await writer.StartRowAsync(cancellationToken);
        await writer.WriteAsync(linkId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
        await writer.WriteAsync(side, NpgsqlDbType.Text, cancellationToken);
    }

    private static async Task<long> ReserveLinkIds(
        NpgsqlConnection connection,
        int count,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT setval(pg_get_serial_sequence('link', 'id'), " +
            "coalesce((SELECT max(id) FROM link), 0) + @count) - @count + 1", connection);
        command.Parameters.AddWithValue("count", count);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static async Task Machine(string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("machine") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("machine did not start.");

        // Both pipes are drained at once. The tool writes a progress bar to standard output, and
        // reading one stream to the end before the other lets that fill its buffer and block the
        // child for ever — which looks exactly like the tool hanging.
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        await Task.WhenAll(errorTask, outputTask);
        await process.WaitForExitAsync(cancellationToken);
        var error = await errorTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"`machine {string.Join(' ', arguments)}` failed with exit code {process.ExitCode}. " +
                $"It is SIL's alignment tool; install it with `dotnet tool install -g SIL.Machine.Tool`. {error}");
        }
    }

    private static void Write(
        string path,
        List<(int, int, int)> addresses,
        Dictionary<(int, int, int), List<Word>> words) =>
        File.WriteAllLines(path, addresses.Select(a => string.Join(' ', words[a].Select(w => w.Text))));

    private async Task<Database.Entities.Text> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
        ?? throw new InvalidOperationException($"There is no text \"{slug}\".");

    private async Task<Dictionary<(int, int, int), List<Word>>> Words(
        string slug,
        Func<string, string?, string?, string> form,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.Text!.Slug == slug)
            .SelectMany(r => r.Verse!.Words.Select(w => new
            {
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                w.Position,
                w.Id,
                w.Surface,
                w.Lemma,
                Consonantal = w.Morphology == null ? null : w.Morphology.RootElement.GetProperty("consonantal")
                    .GetString(),
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position)
                    .Select(r => new Word(r.Id, form(r.Surface, r.Lemma, r.Consonantal)))
                    .ToList());
    }

    private static string Surface(string surface, string? lemma, string? consonantal) => surface.ToLowerInvariant();

    /// <summary>
    /// The form a model can learn from. BHSA writes full vowel pointing, so the same word appears as
    /// many different strings and a lexicon built on twenty-three thousand verses never sees any of
    /// them often enough; using the consonants instead raised precision by a quarter.
    /// </summary>
    private static string Comparable(string surface, string? lemma, string? consonantal) =>
        !string.IsNullOrEmpty(consonantal) ? consonantal
        : !string.IsNullOrEmpty(lemma) ? lemma
        : surface.ToLowerInvariant();

    private sealed record Word(long Id, string Text);

    private sealed record AlignedDraft(long SourceWordId, long TargetWordId, double Translation, double Position);
}
