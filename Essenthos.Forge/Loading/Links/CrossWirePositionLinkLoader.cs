using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <param name="Module">The SWORD module the positions are read from, as its configuration names it.</param>
/// <param name="Slug">The corpus text loaded from it, whose words the links start from.</param>
/// <param name="Credit">What every link drawn from the positions carries, and the dataset declaration claims.</param>
internal sealed record SwordPositions(string Module, string Slug, string Credit);

/// <param name="Witness">The Received Text the positions were laid against.</param>
/// <param name="Laid">How many elements found their words there, and why the rest did not.</param>
/// <param name="Written">Links the positions drew that no link of the pair named before.</param>
/// <param name="Corroborated">Links the positions drew over words a link already named, which became a claim on it.</param>
/// <param name="Contradicted">
/// Of <paramref name="Written"/>, those whose translated words the Strong-number links of the pair take
/// to other Greek words: the places where the number match picked another occurrence, or another word.
/// </param>
internal sealed record PositionLinkOutcome(
    string Witness,
    bool AlreadyLoaded,
    LaidPositions? Laid,
    int Written,
    int Corroborated,
    int Contradicted,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"the positions are already linked to {Witness}"
            : $"{Witness}: {Laid}; {Written} links written, {Corroborated} joined a link over the same words, "
              + $"{Contradicted} of the written ones name other Greek words than the Strong numbers did, in {Elapsed}";
}

/// <summary>
/// The Revised Literal Translation's New Testament linked to the Received Text by the positions
/// CrossWire's module states (<see cref="ReceivedTextPositions"/>): each tagged element names the
/// places of the Greek words it renders, and where the witness holds the named word at the place the
/// link is <c>stated-by-source</c>, with no confidence.
///
/// <para>
/// **Both printed editions are witnesses to it.** The positions count the KJV2003 project's own
/// Received Text, which is neither of Robinson's: measured over the whole New Testament, 99.43% of the
/// elements hold their words in Scrivener and 99.40% in Stephanus, and where the two editions differ
/// 582 agree with Scrivener only and 539 with Stephanus only. So each edition is linked where it holds
/// the words, and an element neither holds — 138, most of them the subscriptions Stephanus prints after
/// the epistles, which the corpus does not load as words — links nothing.
/// </para>
///
/// <para>
/// **The Strong-number links stay.** <c>crosswire-strong</c> matched the same words by number; where
/// a position names the words a number link already names, the statement becomes a claim on it and
/// its settled answer, and where it names others both stand, which is the count of places the number
/// match picked another occurrence of the same number.
/// </para>
/// </summary>
internal sealed class CrossWirePositionLinkLoader(
    AppDbContext db,
    UnionStrongLinkLoader layer,
    ILogger<CrossWirePositionLinkLoader> logger)
{
    public static readonly IReadOnlyList<SwordPositions> All =
    [
        new("RLT", SwordTextSource.RevisedLiteral, Sources.RevisedLiteralPositionCredit),
    ];

    /// <summary>The Received Text editions the positions are laid against.</summary>
    public static readonly IReadOnlyList<string> Witnesses = [Sources.ScrivenerSlug, Sources.StephanusSlug];

    /// <summary>The modules an argument list names, by module or by text; every one where it names none.</summary>
    public static IReadOnlyList<SwordPositions> Named(IEnumerable<string> arguments)
    {
        var named = arguments.Where(argument => !argument.StartsWith("--", StringComparison.Ordinal)).ToList();
        return named.Count == 0
            ? All
            :
            [
                .. named.Select(name => All.FirstOrDefault(module =>
                        string.Equals(module.Module, name, StringComparison.OrdinalIgnoreCase)
                        || string.Equals(module.Slug, name, StringComparison.OrdinalIgnoreCase))
                    ?? throw new ArgumentException(
                        $"No CrossWire module with Received Text positions is called {name}. Name one of "
                        + $"{string.Join(", ", All.Select(module => module.Module))}, or none to run them all.")),
            ];
    }

    public async Task<IReadOnlyList<PositionLinkOutcome>> Load(
        string resources,
        IReadOnlyList<SwordPositions> modules,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<PositionLinkOutcome>();
        foreach (var module in modules)
        {
            var folder = Path.Combine(resources, SwordTextSource.Texts[module.Module].Folder);
            Dictionary<long, int>? units = null;
            Dictionary<int, StatedPositions>? stated = null;
            foreach (var witness in Witnesses)
            {
                if (await Linked(module, witness, cancellationToken))
                {
                    outcomes.Add(new PositionLinkOutcome(witness, true, null, 0, 0, 0, TimeSpan.Zero));
                    logger.LogInformation("{Module}: {Outcome}", module.Module, outcomes[^1]);
                    continue;
                }

                units ??= (await layer.Numbers(module.Slug, folder, module.Credit, cancellationToken)).Tags
                    .ToDictionary(tag => tag.Key, tag => tag.Value.Unit);
                stated ??= SwordTextSource.Positions(folder);
                outcomes.Add(await Write(module, witness, units, stated, cancellationToken));
            }
        }

        return outcomes;
    }

    /// <summary>Whether the pair already holds what the module's positions say, as links or as claims on them.</summary>
    public async Task<bool> Linked(SwordPositions module, string witnessSlug, CancellationToken cancellationToken = default)
    {
        var from = await Text(module.Slug, cancellationToken);
        var to = await Text(witnessSlug, cancellationToken);
        return await db.LinkClaims.AnyAsync(
            c => c.Link!.FromTextId == from.Id && c.Link.ToTextId == to.Id && c.Provenance!.Source == module.Credit,
            cancellationToken);
    }

    /// <summary>
    /// The positions laid onto the witness and written: what <see cref="Load"/> does once the module
    /// has been read and laid onto the translation's words.
    /// </summary>
    /// <param name="units">The element each translation word was laid onto, by word id.</param>
    /// <param name="stated">The positions each element states, by its unit.</param>
    public async Task<PositionLinkOutcome> Write(
        SwordPositions module,
        string witnessSlug,
        IReadOnlyDictionary<long, int> units,
        IReadOnlyDictionary<int, StatedPositions> stated,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var from = await Text(module.Slug, cancellationToken);
        var to = await Text(witnessSlug, cancellationToken);

        var laid = ReceivedTextPositions.Lay(
            stated, units, await VerseWords(to.Id, cancellationToken), await Absent(from.Id, to.Id, cancellationToken));
        var numbered = await NumberedTargets(from.Id, to.Id, cancellationToken);

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var written = await LinkWriter.Write(
            (NpgsqlConnection)db.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction(),
            [
                .. laid.Links.Select(link => new NewLink(
                    from.Id, to.Id, LinkRelation.Renders, LinkMethod.StatedBySource, null, module.Credit, null,
                    link.From, link.To)),
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var contradicted = laid.Links
            .Where((link, at) => written.Fresh[at] && Numbered(link.From) is { Count: > 0 } targets && !targets.SetEquals(link.To))
            .Count();
        HashSet<long> Numbered(IEnumerable<long> words) =>
            [.. words.SelectMany(word => numbered.GetValueOrDefault(word) ?? [])];

        var outcome = new PositionLinkOutcome(
            witnessSlug, false, laid, written.Written, written.Joined, contradicted, started.Elapsed);
        logger.LogInformation("{Module}: {Outcome}", module.Module, outcome);
        return outcome;
    }

    /// <summary>The witness's words, each verse in order, by its canonical address.</summary>
    private async Task<Dictionary<(int Book, int Chapter, int Verse), IReadOnlyList<CorpusWord>>> VerseWords(
        int textId, CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.TextId == textId)
            .SelectMany(r => r.Verse!.Words.Select(w => new
            {
                r.CanonicalBook,
                r.CanonicalChapter,
                r.CanonicalVerse,
                w.Position,
                w.Id,
                w.Surface,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CorpusWord>)[.. group.OrderBy(r => r.Position).Select(r => new CorpusWord(r.Id, r.Surface))]);
    }

    /// <summary>
    /// The words the pair already says have no counterpart: a translation word with nothing in the
    /// Greek, a Greek word the translation leaves out. A word cannot be absent and rendered at once.
    /// </summary>
    private async Task<HashSet<long>> Absent(int fromTextId, int toTextId, CancellationToken cancellationToken) =>
        (await db.LinkWords
            .Where(lw => lw.Link!.FromTextId == fromTextId && lw.Link.ToTextId == toTextId
                         && ((lw.Side == LinkSide.From && lw.Link.Relation == LinkRelation.Expands)
                             || (lw.Side == LinkSide.To && lw.Link.Relation == LinkRelation.Omits)))
            .Select(lw => lw.WordId)
            .ToListAsync(cancellationToken))
        .ToHashSet();

    /// <summary>The Greek words the Strong-number links of the pair take each translation word to.</summary>
    private async Task<Dictionary<long, HashSet<long>>> NumberedTargets(
        int fromTextId, int toTextId, CancellationToken cancellationToken)
    {
        var rows = await db.LinkWords
            .Where(lw => lw.Link!.FromTextId == fromTextId && lw.Link.ToTextId == toTextId
                         && lw.Link.Method == LinkMethod.StrongNumber)
            .Select(lw => new { lw.LinkId, lw.WordId, lw.Side })
            .ToListAsync(cancellationToken);

        var targets = new Dictionary<long, HashSet<long>>();
        foreach (var link in rows.GroupBy(row => row.LinkId))
        {
            var greek = link.Where(row => row.Side == LinkSide.To).Select(row => row.WordId).ToList();
            foreach (var word in link.Where(row => row.Side == LinkSide.From))
            {
                if (!targets.TryGetValue(word.WordId, out var set))
                {
                    targets[word.WordId] = set = [];
                }

                set.UnionWith(greek);
            }
        }

        return targets;
    }

    private async Task<Database.Entities.Text> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
        ?? throw new InvalidOperationException(
            $"{slug} is not loaded, and the module's positions are only ever laid onto the texts the corpus "
            + "already holds. Load the corpus first.");
}
