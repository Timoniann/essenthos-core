using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.TextusReceptus;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Reina-Valera 1909 reaches the Hebrew and the Greek by the Strong numbers Rubén Gómez put on it —
/// the route the Synodal has by Bob Jones University's.
///
/// <para>
/// **The numbers are an input and never a row.** The tagging is Gómez's, which eBible republishes
/// without his name, and the owner ruled on 2026-09-20 that it may be used for the mapping alone and
/// shown nowhere (Resources/ReinaValera1909/LICENCE.md). The load refuses the numbers
/// (<see cref="EbibleTextSource"/>); this reads them from the same files for the length of one run,
/// lays them onto the words the corpus loaded from those files (<see cref="SynodalStrongLayer"/>),
/// and what reaches the database is the links drawn from them, credited to him (<see cref="Credit"/>).
/// <c>word.strong_number</c> and <c>word_strong</c> are not touched.
/// </para>
///
/// <para>
/// Clear Bible's hand-made alignment of this same file is the Spanish's first answer, and a match
/// touching a word it names is not written (<see cref="TaggedTextLinkLoader"/>): the numbers reach
/// the words the alignment leaves, and settle the aligner's guesses there.
/// </para>
/// </summary>
internal sealed class ReinaValeraStrongLinkLoader(
    AppDbContext db,
    TaggedTextLinkLoader tagged,
    ILogger<ReinaValeraStrongLinkLoader> logger)
{
    /// <summary>The folder the fetch script puts the edition in, under the corpus sources.</summary>
    public static readonly string[] EditionFolder = ["ReinaValera1909"];

    /// <summary>What every link drawn from the numbering begins with, and the dataset declaration claims.</summary>
    public const string Credit = Sources.ReinaValeraStrongCredit;

    /// <summary>
    /// The witnesses the Reina-Valera is matched against. Gómez numbered a Hebrew Old Testament and the
    /// Received Text, so the three editions of it stand beside Nestle, as for the Synodal.
    /// </summary>
    public static readonly string[] Witnesses =
    [
        BhsaTextSource.Slug,
        TextusReceptusTextSource.Slug(Edition.Scrivener1894),
        TextusReceptusTextSource.Slug(Edition.Stephanus1550),
        ByzantineTextSource.Slug,
        NestleTextSource.Slug,
    ];

    /// <summary>The witnesses Clear Bible aligned the Spanish to, on which the numbers can be scored.</summary>
    public static readonly string[] Scored = [BhsaTextSource.Slug, NestleTextSource.Slug];

    public async Task<IReadOnlyList<TaggedTextLinkOutcome>> Load(
        string folder,
        IReadOnlyList<string> witnesses,
        CancellationToken cancellationToken = default)
    {
        var numbers = await Numbers(folder, cancellationToken);
        var outcomes = new List<TaggedTextLinkOutcome>(witnesses.Count);
        foreach (var witness in witnesses)
        {
            outcomes.Add(await tagged.Load(EbibleTextSource.ReinaValera, witness, numbers, cancellationToken));
        }

        return outcomes;
    }

    /// <summary>What the numbers would draw, scored against Clear Bible's links; nothing is written.</summary>
    public async Task<IReadOnlyList<TaggedTextScore>> Score(
        string folder,
        IReadOnlyList<string>? witnesses = null,
        CancellationToken cancellationToken = default)
    {
        var numbers = await Numbers(folder, cancellationToken);
        var scores = new List<TaggedTextScore>();
        foreach (var witness in witnesses ?? Scored)
        {
            scores.Add(await tagged.Score(EbibleTextSource.ReinaValera, witness, numbers, cancellationToken));
        }

        return scores;
    }

    /// <summary>Gómez's numbers laid onto the Reina-Valera's words, for the length of the caller's run.</summary>
    public async Task<EditionNumbers> Numbers(string folder, CancellationToken cancellationToken = default)
    {
        var text = await db.Texts.SingleOrDefaultAsync(t => t.Slug == EbibleTextSource.ReinaValera, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"{EbibleTextSource.ReinaValera} is not loaded, and its numbers are only ever laid onto the text "
                       + "the corpus already holds. Load the corpus first.");

        var laid = SynodalStrongLayer.Lay(EbibleTextSource.Numbers(folder), await Verses(text.Id, cancellationToken));
        logger.LogInformation(
            "{Slug}: the edition's numbers laid onto {Verses} verses, {Joined} of them with a neighbour, {Refused} "
            + "refused because the loaded words differ from the file's, {Unused} file verses no verse took, "
            + "{Words} words given a number",
            EbibleTextSource.ReinaValera, laid.Verses, laid.Joined, laid.Refused, laid.Unused, laid.TaggedWords);
        return new EditionNumbers(laid.Tags, Credit);
    }

    private async Task<List<CorpusVerse>> Verses(int textId, CancellationToken cancellationToken)
    {
        var verses = await db.Verses
            .Where(v => v.TextId == textId)
            .Select(v => new { v.Id, Book = v.Book!.CanonicalOrdinal, v.ChapterNumber, v.Number })
            .ToListAsync(cancellationToken);

        var words = (await db.Words
                .Where(w => w.TextId == textId)
                .Select(w => new { w.VerseId, w.Position, w.Id, w.Surface })
                .ToListAsync(cancellationToken))
            .GroupBy(w => w.VerseId)
            .ToDictionary(
                verse => verse.Key,
                verse => verse.OrderBy(w => w.Position).Select(w => new CorpusWord(w.Id, w.Surface)).ToList());

        // The file is read into the frame the text was loaded in, so every verse is laid at the
        // address it is stored under.
        return
        [
            .. verses
                .OrderBy(v => (v.Book, v.ChapterNumber, v.Number))
                .Select(v => new CorpusVerse(
                    v.Book,
                    v.ChapterNumber,
                    v.Number,
                    Array.Empty<Essenthos.Core.XmlBible.VerseAddress>(),
                    words.GetValueOrDefault(v.Id) ?? [])),
        ];
    }
}
