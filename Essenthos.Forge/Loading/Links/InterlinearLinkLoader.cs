using Essenthos.Core.Database;
using Essenthos.Core.Door43;
using Essenthos.Core.Corpus;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The stated word-level correspondence the Slavic texts have, which is the whole of what anyone
/// publishes for either of them.
///
/// Everything else the Ukrainian and the Synodal reach, they reach through a model: hundreds of
/// thousands of links to BHSA and to the Greek, every one of them inferred, none of them asserted
/// by anybody. unfoldingWord's alignment is people saying *this Ukrainian word renders that Hebrew
/// word*, and that is a different kind of claim — <c>stated-by-source</c>, no confidence, the same
/// standing as the King James mapping file.
///
/// The Russian half of it is three books, Titus, Philemon and 2 John, against 66. That is small
/// enough to be worth saying why it is here at all: it is not coverage, it is a standard. Until it
/// was loaded, every Russian link in the corpus came out of a model and nothing could say whether
/// any of them was right.
///
/// It joins without alignment because the source marks its own morpheme boundaries. BHSA holds
/// <c>וַ⁠יְהִי</c> as two words, the conjunction and the verb, and the interlinear writes it with
/// U+2060 in exactly that place and tags it <c>c:H1961</c>. So the pieces are matched to BHSA's
/// words by their form and by the occurrence the source states, and a span where that does not come
/// out exact is refused rather than forced — <see cref="InterlinearJoin"/> says how, and
/// <see cref="InterlinearJoinAccount"/> counts what was refused and why.
///
/// <para>
/// The links are the build's (<see cref="Mapping.Door43AlignmentSource"/>, from the store's files, by
/// <see cref="Book"/>); here the join is made over a database's words for EVIDENTIA's measurements
/// (<see cref="Pairs"/>), and nothing is written.
/// </para>
/// </summary>
internal sealed class InterlinearLinkLoader(AppDbContext db, ILogger<InterlinearLinkLoader> logger)
{
    /// <summary>
    /// The folder under <c>Resources/Door43</c> and the source recorded on the links, for each text
    /// an interlinear aligns. The source is what a reload finds the stored statements by, so the
    /// startup load and a reload have to spell it identically.
    /// </summary>
    public static (string Folder, string Source) Interlinear(string slug) => slug switch
    {
        Bible4uTextSource.Ohienko => ("uk_ubio",
            "unfoldingWord's Ukrainian Bible Interlinear Ogienko, git.door43.org/uk_ts/uk_ubio, CC BY-SA 4.0"),
        Bible4uTextSource.Synodal => ("ru_rsb",
            "Door43 Russian Synodal alignment of Titus, Philemon and 2 John, made in "
            + "translationCore and published at git.door43.org under CC0 1.0"),
        UnfoldingWordTextSource.Slug => (UnfoldingWordTextSource.Folder, UnfoldingWordSource),
        _ when Door43AlignedBible.Of(slug) is { } aligned => (aligned.Folder, aligned.Source),
        _ => throw new ArgumentException(
            $"Door43 interlinears exist for {Bible4uTextSource.Ohienko}, {Bible4uTextSource.Synodal}, " +
            $"{UnfoldingWordTextSource.Slug} and {string.Join(", ", Door43AlignedBible.All.Select(bible => bible.Slug))}; " +
            $"{slug} has none. Name one of those as the source, or score against stored links instead."),
    };

    /// <summary>
    /// What the links drawn from the unfoldingWord Literal Text's alignment say about themselves. It
    /// names the release, because the alignment is revised from one release to the next.
    /// </summary>
    public const string UnfoldingWordSource =
        "unfoldingWord Literal Text alignment to unfoldingWord's Hebrew Bible and Greek New Testament, "
        + "release 90, git.door43.org/unfoldingWord/en_ult, CC BY-SA 4.0";

    /// <summary>
    /// The links this join makes, as word ids, without writing them: an answer key a measurement can
    /// read when the rows the corpus holds were written by an older join.
    /// </summary>
    public async Task<IReadOnlyList<(IReadOnlyList<long> From, IReadOnlyList<long> To)>> Pairs(
        string folder,
        string translationSlug,
        CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(folder))
        {
            throw new DirectoryNotFoundException(
                $"No interlinear at {folder}. Point Dataset:ResourcesPath at the Resources folder that holds Door43/.");
        }

        var join = await Join(
            folder, await Text(translationSlug, cancellationToken), Unnumbered(translationSlug), cancellationToken);
        return [.. join.Drafts.Select(draft => ((IReadOnlyList<long>)draft.From, (IReadOnlyList<long>)draft.To))];
    }

    /// <summary>
    /// Whether the interlinear names some original words by spelling alone, as the releases exported by
    /// older translationCore do. See <see cref="Door43AlignedBible"/>.
    /// </summary>
    internal static bool Unnumbered(string translationSlug) => Door43AlignedBible.Of(translationSlug) is not null;

    private async Task<InterlinearJoinResult> Join(
        string folder,
        int translation,
        bool unnumbered,
        CancellationToken cancellationToken)
    {
        var witnesses = new Dictionary<string, int>();
        foreach (var slug in (string[])[BhsaTextSource.Slug, NestleTextSource.Slug])
        {
            witnesses[slug] = await Text(slug, cancellationToken);
        }

        var drafts = new List<InterlinearDraft>(40_000);
        var books = new List<(string Book, InterlinearJoinAccount Account)>();
        var total = new InterlinearJoinAccount();

        foreach (var file in Directory.GetFiles(folder, "*.usfm").OrderBy(path => path))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            var ordinal = Ordinal(Path.GetFileName(file));
            if (ordinal is null)
            {
                // Said out loud, because it used to be said to nobody. A file named for a book the
                // abbreviation table does not answer to is skipped whole, and skipping a book of a
                // stated source in silence is indistinguishable from the source not covering it.
                logger.LogWarning(
                    "{File} names no book this corpus knows, so nothing in it was read. Add the " +
                    "name it uses to BibleBookAbbreviation", file);
                continue;
            }

            var witness = witnesses[ordinal <= BookReferences.OldTestamentBookCount
                ? BhsaTextSource.Slug
                : NestleTextSource.Slug];
            var read = Usfm3AlignmentReader.Read(await File.ReadAllTextAsync(file, cancellationToken), unnumbered);
            var here = await Words(translation, ordinal.Value, cancellationToken);
            var there = await Words(witness, ordinal.Value, cancellationToken);
            var (pairs, account) = Book(name, read, here, there, address => address);

            drafts.AddRange(pairs.Select(pair => new InterlinearDraft(translation, witness, pair.From, pair.To)));
            books.Add((name, account));
            total.Add(account);
        }

        return new InterlinearJoinResult(drafts, books, total);
    }

    /// <summary>
    /// One book of the interlinear joined, verse by verse: <paramref name="here"/> holds the translation's words by the
    /// verse the interlinear numbers, <paramref name="there"/> the witness's by frame address, and
    /// <paramref name="frame"/> says at which address of the witness a verse of the interlinear is read.
    /// </summary>
    internal static (List<InterlinearPair> Pairs, InterlinearJoinAccount Account) Book(
        string name,
        IReadOnlyList<AlignedVerse> read,
        Dictionary<(int, int), List<InterlinearWord>> here,
        Dictionary<(int, int), List<InterlinearWord>> there,
        Func<(int Chapter, int Number), (int Chapter, int Number)> frame)
    {
        var account = new InterlinearJoinAccount();
        var pairs = new List<InterlinearPair>();
        foreach (var verse in read)
        {
            var address = (verse.Chapter, verse.Number);
            InterlinearJoin.Verse(
                $"{name} {verse.Chapter}:{verse.Number}",
                verse,
                here.GetValueOrDefault(address) ?? [],
                Witness(there, frame(address)),
                pairs,
                account);
        }

        return (pairs, account);
    }

    /// <summary>
    /// The witness words a verse of the interlinear is joined against. A psalm's first verse is printed
    /// with its title at its head, and the witness numbers the title as a verse of its own, so the two
    /// are read as one: a title word the verse repeats then counts twice and is refused rather than
    /// guessed.
    /// </summary>
    private static List<InterlinearWord> Witness(
        Dictionary<(int, int), List<InterlinearWord>> there,
        (int Chapter, int Number) address) =>
        address.Number == 1 && there.TryGetValue((address.Chapter, 0), out var title)
            ? [.. title, .. there.GetValueOrDefault(address) ?? []]
            : there.GetValueOrDefault(address) ?? [];

    /// <summary>
    /// The book a file is for, from a name like <c>17-EST.usfm</c>, or <c>EST.usfm</c> where a Scripture
    /// Burrito names its books by their code alone.
    /// </summary>
    internal static int? Ordinal(string fileName) =>
        BookReferences.ResolveOrdinal(Path.GetFileNameWithoutExtension(fileName)[(fileName.IndexOf('-') + 1)..]);

    private async Task<Dictionary<(int, int), List<InterlinearWord>>> Words(
        int textId,
        int ordinal,
        CancellationToken cancellationToken)
    {
        var rows = await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.TextId == textId && r.CanonicalBook == ordinal)
            .SelectMany(r => r.Verse!.Words.Select(w => new
            {
                r.CanonicalChapter,
                r.CanonicalVerse,
                w.Id,
                w.Position,
                w.NormalisedText,
                Written = w.Surface,
                w.StrongNumber,
                w.Trailer,
                Language = w.Text!.Language,
            }))
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(r => (r.CanonicalChapter, r.CanonicalVerse))
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(r => r.Position)
                    .Select(r => new InterlinearWord(r.Id, r.NormalisedText ?? string.Empty, r.Language, r.Written, r.StrongNumber, r.Trailer.Length == 0))
                    .ToList());
    }

    private async Task<int> Text(string slug, CancellationToken cancellationToken) =>
        await db.Texts.Where(t => t.Slug == slug).Select(t => t.Id).FirstOrDefaultAsync(cancellationToken)
            is var id and not 0
            ? id
            : throw new InvalidOperationException($"The text \"{slug}\" must be loaded before it can be linked.");

    private sealed record InterlinearJoinResult(
        List<InterlinearDraft> Drafts,
        List<(string Book, InterlinearJoinAccount Account)> Books,
        InterlinearJoinAccount Total);
}

/// <summary>One link of the join: the translation's words and the witness's they render.</summary>
internal sealed record InterlinearDraft(int FromTextId, int ToTextId, List<long> From, List<long> To);
