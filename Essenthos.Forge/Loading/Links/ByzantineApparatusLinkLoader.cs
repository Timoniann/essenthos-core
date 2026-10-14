using System.Diagnostics;
using Essenthos.Core.Byzantine;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;

namespace Essenthos.Core.Loading.Links;

/// <param name="Witness">The loaded text the notes were laid against.</param>
/// <param name="Laid">How many verses and notes found their words, and how many readings the witness prints.</param>
/// <param name="Written">Links the notes drew that no link of the pair named before.</param>
/// <param name="Corroborated">Links over words a link of the pair already named, which became a claim on it.</param>
/// <param name="Contrary">
/// Notes left out because the pair already says the opposite of the same words: an absence where the
/// note gives a rendering, a rendering where it gives an absence, or another relation over the same words.
/// </param>
internal sealed record ApparatusLinkOutcome(
    string Witness,
    bool AlreadyLoaded,
    LaidApparatus? Laid,
    int Written,
    int Corroborated,
    int Contrary,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? $"Robinson's apparatus is already linked to {Witness}"
            : $"{Witness}: {Laid}; {Written} links written, {Corroborated} joined a link over the same words, "
              + $"{Contrary} left out where the pair already says otherwise, in {Elapsed}";
}

/// <summary>
/// Robinson's apparatus to the Byzantine Textform, written as the variation it states
/// (<see cref="ByzantineApparatus"/>): <c>stated-by-source</c>, no confidence, the shape
/// <see cref="PrintedEditionLinkLoader"/> writes for the Received Text's two editions.
///
/// <para>
/// **Each kind of note is laid against the loaded edition it describes.** The Nestle-Aland and Editio
/// Critica Maior notes against Nestle 1904, the critical text the corpus holds; the notes where the
/// Byzantine manuscripts divide against Scrivener's Received Text, which often prints the other half.
/// The three letters the repository does not explain (C, M and S, in Acts and the Catholic Letters)
/// are not read.
/// </para>
/// </summary>
internal sealed class ByzantineApparatusLinkLoader(AppDbContext db, ILogger<ByzantineApparatusLinkLoader> logger)
{
    public const string Credit = Sources.ByzantineApparatusCredit;

    /// <summary>The notes describing the critical editions: Nestle-Aland 28 and the Editio Critica Maior.</summary>
    public static readonly IReadOnlySet<char> Critical = new HashSet<char> { 'N', 'E' };

    /// <summary>The notes where the Byzantine manuscripts divide between two readings.</summary>
    public static readonly IReadOnlySet<char> Divided = new HashSet<char> { 'B' };

    /// <summary>Each witness, with the notes laid against it.</summary>
    public static readonly IReadOnlyList<(string Witness, IReadOnlySet<char> Kinds)> Witnesses =
    [
        (NestleTextSource.Slug, Critical),
        (Sources.ScrivenerSlug, Divided),
    ];

    private static readonly Dictionary<char, string> Notes = new()
    {
        ['N'] = "where Nestle-Aland 28 reads otherwise, and Nestle 1904 prints its reading at the place",
        ['E'] = "where the Editio Critica Maior reads otherwise, and Nestle 1904 prints its reading at the place",
        ['B'] = "where the Byzantine manuscripts divide, and Scrivener prints the other reading at the place",
    };

    /// <summary>The apparatus of every book, by canonical address.</summary>
    /// <param name="folder">The Byzantine Textform's folder, which holds <c>ccat/</c> beside the parse.</param>
    public static Dictionary<(int Book, int Chapter, int Verse), CcatVerse> Read(string folder)
    {
        var verses = new Dictionary<(int, int, int), CcatVerse>(8_000);
        for (var at = 0; at < ByzantineTextSource.Books.Count; at++)
        {
            var path = Path.Combine(folder, "ccat", $"{ByzantineTextSource.Books[at]}.TXT");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Robinson's apparatus is not on this machine: {path} is missing. Run scripts/fetch-byzantine.ps1, "
                    + "which takes it at the commit the loaded text came from.", path);
            }

            foreach (var verse in CcatReader.Read(File.ReadAllText(path)))
            {
                verses[(ByzantineTextSource.FirstCanonicalOrdinal + at, verse.Chapter, verse.Number)] = verse;
            }
        }

        return verses;
    }

    public async Task<IReadOnlyList<ApparatusLinkOutcome>> Load(string folder, CancellationToken cancellationToken = default)
    {
        Dictionary<(int, int, int), CcatVerse>? apparatus = null;
        var outcomes = new List<ApparatusLinkOutcome>();
        foreach (var (witness, kinds) in Witnesses)
        {
            if (await Linked(witness, cancellationToken))
            {
                outcomes.Add(new ApparatusLinkOutcome(witness, true, null, 0, 0, 0, TimeSpan.Zero));
                logger.LogInformation("{Outcome}", outcomes[^1]);
                continue;
            }

            apparatus ??= Read(folder);
            outcomes.Add(await Write(apparatus, witness, kinds, cancellationToken));
        }

        return outcomes;
    }

    /// <summary>Whether the pair already holds what the apparatus says, as links or as claims on them.</summary>
    public async Task<bool> Linked(string witness, CancellationToken cancellationToken = default)
    {
        var from = await Text(ByzantineTextSource.Slug, cancellationToken);
        var to = await Text(witness, cancellationToken);
        return await db.LinkClaims.AnyAsync(
            c => c.Link!.FromTextId == from.Id && c.Link.ToTextId == to.Id && c.Provenance!.Source == Credit,
            cancellationToken);
    }

    /// <summary>The apparatus laid against one witness and written.</summary>
    public async Task<ApparatusLinkOutcome> Write(
        IReadOnlyDictionary<(int Book, int Chapter, int Verse), CcatVerse> apparatus,
        string witness,
        IReadOnlySet<char> kinds,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var from = await Text(ByzantineTextSource.Slug, cancellationToken);
        var to = await Text(witness, cancellationToken);

        var laid = ByzantineApparatus.Lay(
            apparatus, await VerseWords(from.Id, cancellationToken), await VerseWords(to.Id, cancellationToken), kinds);
        var standing = await Standing(from.Id, to.Id, cancellationToken);
        var admitted = laid.Links.Where(link => !Contrary(link, standing)).ToList();

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var written = await LinkWriter.Write(
            (NpgsqlConnection)db.Database.GetDbConnection(),
            (NpgsqlTransaction)transaction.GetDbTransaction(),
            [
                .. admitted.Select(link => new NewLink(
                    from.Id, to.Id, link.Relation, LinkMethod.StatedBySource, null, Credit, Notes[link.Kind],
                    link.From, link.To)),
            ],
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var outcome = new ApparatusLinkOutcome(
            witness, false, laid, written.Written, written.Joined, laid.Links.Count - admitted.Count, started.Elapsed);
        logger.LogInformation("{Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// Whether the pair already says the opposite about the note's words. A word cannot be shown as
    /// having no counterpart and as rendered at once, and one set of words carries one relation.
    /// </summary>
    private static bool Contrary(ApparatusLink link, PairStanding standing)
    {
        if (standing.Shapes.TryGetValue(LinkShape.Of(link.From, link.To), out var relation))
        {
            return relation != link.Relation;
        }

        var absence = link.Relation is LinkRelation.Expands or LinkRelation.Omits;
        return link.From.Concat(link.To).Any(absence ? standing.Rendered.Contains : standing.Absent.Contains);
    }

    private sealed record PairStanding(
        Dictionary<Guid, LinkRelation> Shapes,
        HashSet<long> Rendered,
        HashSet<long> Absent);

    private async Task<PairStanding> Standing(int fromTextId, int toTextId, CancellationToken cancellationToken)
    {
        var links = await db.Links
            .Where(l => l.FromTextId == fromTextId && l.ToTextId == toTextId)
            .Select(l => new { l.Fingerprint, l.Relation, Words = l.Words.Select(w => w.WordId).ToList() })
            .ToListAsync(cancellationToken);

        var standing = new PairStanding([], [], []);
        foreach (var link in links)
        {
            if (link.Fingerprint is { } shape)
            {
                standing.Shapes.TryAdd(shape, link.Relation);
            }

            (link.Relation is LinkRelation.Expands or LinkRelation.Omits ? standing.Absent : standing.Rendered)
                .UnionWith(link.Words);
        }

        return standing;
    }

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

    private async Task<Database.Entities.Text> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
        ?? throw new InvalidOperationException(
            $"{slug} is not loaded, and Robinson's apparatus is only ever laid onto texts the corpus already "
            + "holds. Load the corpus first.");
}
