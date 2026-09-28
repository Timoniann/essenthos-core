using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.XmlBible;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Chinese Union Version reaches the Hebrew and the Greek by the Strong numbers the Faith Hope
/// Love foundation (信望愛, FHL) put on it — the route Luther and the Synodal already have.
///
/// <para>
/// **The numbers are an input and never a row**, as the Synodal's are. FHL released them under the
/// GNU Free Documentation License, which permits this use without asking; they are read from the
/// module for the length of one run, laid onto the words the corpus loaded from the same module
/// (<see cref="SynodalStrongLayer"/>, which requires the two to write each verse the same way), and
/// the links drawn from them are what reaches the database, credited to FHL (<see cref="Credit"/>).
/// </para>
///
/// <para>
/// Both scripts are the same text with the same tagging, so each is matched on its own module's
/// numbers and neither borrows the other's.
/// </para>
/// </summary>
internal sealed class UnionStrongLinkLoader(
    AppDbContext db,
    TaggedTextLinkLoader tagged,
    ILogger<UnionStrongLinkLoader> logger)
{
    /// <summary>What every link drawn from FHL's numbers begins with, and the dataset declaration claims.</summary>
    public const string Credit = Sources.UnionStrongCredit;

    /// <summary>
    /// The witnesses the Union Version is matched against. Its New Testament was translated from the
    /// Greek behind the English Revised Version, which Westcott and Hort's text is the nearest
    /// printed form of; Nestle's is the critical text every other translation here meets.
    /// </summary>
    public static readonly string[] Witnesses =
    [
        BhsaTextSource.Slug,
        NestleTextSource.Slug,
        WestcottHortTextSource.Slug,
    ];

    /// <param name="modules">Each Chinese text's module root, by the slug it is loaded under.</param>
    public async Task<IReadOnlyList<TaggedTextLinkOutcome>> Load(
        IReadOnlyDictionary<string, string> modules,
        IReadOnlyList<string> witnesses,
        CancellationToken cancellationToken = default)
    {
        var outcomes = new List<TaggedTextLinkOutcome>();
        foreach (var (slug, module) in modules)
        {
            var numbers = await Numbers(slug, module, cancellationToken);
            foreach (var witness in witnesses)
            {
                outcomes.Add(await tagged.Load(slug, witness, numbers, cancellationToken));
            }
        }

        return outcomes;
    }

    /// <summary>FHL's numbers laid onto one Chinese text's words, for the length of the caller's run.</summary>
    /// <param name="module">The text's module root.</param>
    public async Task<EditionNumbers> Numbers(string slug, string module, CancellationToken cancellationToken = default)
    {
        var text = await db.Texts.SingleOrDefaultAsync(t => t.Slug == slug, cancellationToken)
                   ?? throw new InvalidOperationException(
                       $"{slug} is not loaded, and FHL's numbers are only ever laid onto the text the corpus "
                       + "already holds. Load the corpus first.");

        var laid = SynodalStrongLayer.Lay(
            SwordTextSource.Numbers(module), await Verses(text.Id, cancellationToken));
        logger.LogInformation(
            "{Slug}: FHL's numbers laid onto {Verses} verses, {Refused} refused because the loaded words differ "
            + "from the module's, {Words} words given a number",
            slug, laid.Verses, laid.Refused, laid.TaggedWords);
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

        // Every verse is laid at the address it is stored under: the module is read into the same
        // frame the text was loaded in, so nothing here needs the addresses the edition prints.
        return
        [
            .. verses
                .OrderBy(v => (v.Book, v.ChapterNumber, v.Number))
                .Select(v => new CorpusVerse(
                    v.Book,
                    v.ChapterNumber,
                    v.Number,
                    Array.Empty<VerseAddress>(),
                    words.GetValueOrDefault(v.Id) ?? [])),
        ];
    }
}
