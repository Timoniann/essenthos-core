using Essenthos.Core.Database;
using Essenthos.Core.TextusReceptus;
using Essenthos.Core.XmlBible;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// The Russian Synodal reaches the Hebrew and the Greek by the Strong numbers Bob Jones University
/// put on it in 1996 — the route the King James and Luther already have, and the first one the
/// Synodal has that is not this project's model.
///
/// <para>
/// **The numbering is an input and never a row.** Its only statement of terms permits use of the
/// work unmodified, and the owner decided on 2026-09-13 to use it for the mapping alone
/// (Resources/SynodalStrong/LICENCE.md). So the edition is read from the file, laid onto the
/// Synodal words the corpus already holds (<see cref="SynodalStrongLayer"/>), and the numbers live
/// in memory for the length of the
/// run: <c>word.strong_number</c> and <c>word_strong</c> are not touched, the tagged edition is not
/// a text, and nothing serves its numbers. What is written is the links, credited to the numbering
/// (<see cref="Credit"/>).
/// </para>
///
/// <para>
/// The Synodal already has the aligner's links to every one of these witnesses, so each pair is
/// settled as it is written: a guess the numbers confirm becomes a claim on the match, and a guess
/// they refute is removed (<see cref="NumberedLinkSettlement"/>).
/// </para>
/// </summary>
internal sealed class SynodalStrongLinkLoader(
    AppDbContext db,
    TaggedTextLinkLoader tagged,
    ILogger<SynodalStrongLinkLoader> logger)
{
    /// <summary>Where the fetch script puts the edition, under the corpus sources.</summary>
    public static readonly string[] EditionFile = ["SynodalStrong", "RSTE_verse_words.xml"];

    /// <summary>
    /// What every link drawn from the numbering begins with, and what the dataset declaration
    /// claims. It names whose numbering it is and where the copy came from, because the copy
    /// carries no credit of its own.
    /// </summary>
    public const string Credit =
        "the Bob Jones University Strong numbering of the Synodal (1996), read from swmail/RST";

    /// <summary>
    /// The witnesses the Synodal is matched against. The numbering's Greek is the King James's
    /// Received Text, so the three editions of it are here beside Nestle.
    /// </summary>
    public static readonly string[] Witnesses =
    [
        BhsaTextSource.Slug,
        TextusReceptusTextSource.Slug(Edition.Scrivener1894),
        TextusReceptusTextSource.Slug(Edition.Stephanus1550),
        ByzantineTextSource.Slug,
        NestleTextSource.Slug,
    ];

    public async Task<IReadOnlyList<TaggedTextLinkOutcome>> Load(
        string editionPath,
        IReadOnlyList<string> witnesses,
        CancellationToken cancellationToken = default)
    {
        var synodal = await db.Texts.SingleOrDefaultAsync(t => t.Slug == Bible4uTextSource.Synodal, cancellationToken)
                      ?? throw new InvalidOperationException(
                          $"The Synodal ({Bible4uTextSource.Synodal}) is not loaded, and the numbering is only " +
                          "ever laid onto the Synodal the corpus already holds. Load the corpus first.");

        var edition = SynodalStrongEdition.Read(editionPath);
        var laid = SynodalStrongLayer.Lay(edition, await Verses(synodal.Id, cancellationToken));
        logger.LogInformation("{Laid}", laid);

        var numbers = new EditionNumbers(laid.Tags, Credit);
        var outcomes = new List<TaggedTextLinkOutcome>(witnesses.Count);
        foreach (var witness in witnesses)
        {
            outcomes.Add(await tagged.Load(synodal.Slug, witness, numbers, cancellationToken));
        }

        return outcomes;
    }

    private async Task<List<CorpusVerse>> Verses(int textId, CancellationToken cancellationToken)
    {
        var verses = await db.Verses
            .Where(v => v.TextId == textId)
            .Select(v => new { v.Id, Book = v.Book!.CanonicalOrdinal, v.ChapterNumber, v.Number })
            .ToListAsync(cancellationToken);

        var stated = (await db.StatedVerseNumbers
                .Where(s => s.Verse!.TextId == textId)
                .Select(s => new { s.VerseId, s.Position, s.ChapterNumber, s.Number })
                .ToListAsync(cancellationToken))
            .GroupBy(s => s.VerseId)
            .ToDictionary(
                verse => verse.Key,
                verse => verse.OrderBy(s => s.Position).Select(s => new VerseAddress(s.ChapterNumber, s.Number)).ToList());

        var words = (await db.Words
                .Where(w => w.TextId == textId)
                .Select(w => new { w.VerseId, w.Position, w.Id, w.Surface })
                .ToListAsync(cancellationToken))
            .GroupBy(w => w.VerseId)
            .ToDictionary(
                verse => verse.Key,
                verse => verse.OrderBy(w => w.Position).Select(w => new CorpusWord(w.Id, w.Surface)).ToList());

        return
        [
            .. verses
                .OrderBy(v => (v.Book, v.ChapterNumber, v.Number))
                .Select(v => new CorpusVerse(
                    v.Book,
                    v.ChapterNumber,
                    v.Number,
                    stated.GetValueOrDefault(v.Id) ?? [],
                    words.GetValueOrDefault(v.Id) ?? [])),
        ];
    }
}
