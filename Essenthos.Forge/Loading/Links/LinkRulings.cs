using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Utils;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links;

/// <param name="Reference">The verse, canonically: <c>2 Corinthians 13:5</c>.</param>
/// <param name="Text">The translation's slug.</param>
/// <param name="Witness">The original's slug.</param>
/// <param name="Word">The witness word as printed; it is matched on its letters, accents and case aside.</param>
/// <param name="Occurrence">Which of the verse's words with those letters it is, counted from one.</param>
/// <param name="Stands">The source whose statement about the word stands.</param>
/// <param name="Over">The source whose statement about the word, and only that word, is set aside.</param>
/// <param name="Reason">What the translation reads, which is what settled it.</param>
internal sealed record LinkRuling(
    string Reference,
    string Text,
    string Witness,
    string Word,
    int Occurrence,
    string Stands,
    string Over,
    string Reason);

internal sealed record LinkRulingFile(string Method, string Decided, string DecidedBy, IReadOnlyList<LinkRuling> Rulings);

/// <summary>
/// Where two sources that state the same pair of texts disagree about one word, and this project
/// read the translation to settle which is right. They are kept in a tracked file rather than in
/// either loader so that a redraw of one source cannot undo a ruling the other depends on, and so
/// that every ruling carries the reading it was made on.
///
/// <para>
/// A ruling names its word canonically and by letters, never by row id: the ids are renumbered by
/// every rebuild, and a ruling that silently came to name another word would be worse than none.
/// </para>
/// </summary>
internal sealed class LinkRulings(IReadOnlyList<LinkRuling> rulings)
{
    public const string Berean = "berean";

    public const string ClearBible = "clearbible";

    public static readonly string DefaultFile = Path.Combine("Essenthos", "link-rulings.json");

    public static readonly LinkRulings None = new([]);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public IReadOnlyList<LinkRuling> All => rulings;

    public static LinkRulings Read(string resources)
    {
        var path = Path.Combine(resources, DefaultFile);
        if (!File.Exists(path))
        {
            return None;
        }

        using var stream = File.OpenRead(path);
        return new LinkRulings(JsonSerializer.Deserialize<LinkRulingFile>(stream, Json)?.Rulings ?? []);
    }

    /// <summary>
    /// The witness words whose statement by <paramref name="source"/> is set aside between two texts,
    /// as this database numbers them. A ruling whose verse or word is not there is logged and names
    /// nothing: guessing another word for it would be a ruling nobody made.
    /// </summary>
    public async Task<HashSet<long>> Overruled(
        AppDbContext db,
        string text,
        string witness,
        string source,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var overruled = new HashSet<long>();
        var mine = rulings
            .Where(ruling => ruling.Over == source && ruling.Text == text && ruling.Witness == witness)
            .ToList();
        if (mine.Count == 0)
        {
            return overruled;
        }

        var witnessText = await db.Texts
            .Where(t => t.Slug == witness)
            .Select(t => new { t.Id, t.Language })
            .SingleAsync(cancellationToken);

        foreach (var ruling in mine)
        {
            if (!Address(ruling.Reference, out var book, out var chapter, out var verse))
            {
                logger.LogWarning(
                    "The link ruling on {Reference} names no verse this corpus knows; write it as \"Matthew 16:21\"",
                    ruling.Reference);
                continue;
            }

            var words = await db.VerseReferences
                .Where(reference => reference.IsPrimary
                                    && reference.Verse!.TextId == witnessText.Id
                                    && reference.CanonicalBook == book
                                    && reference.CanonicalChapter == chapter
                                    && reference.CanonicalVerse == verse)
                .SelectMany(reference => reference.Verse!.Words.Select(word => new
                {
                    word.Id,
                    PrintedChapter = word.Verse!.ChapterNumber,
                    PrintedVerse = word.Verse!.Number,
                    word.Position,
                    word.Surface,
                }))
                .ToListAsync(cancellationToken);
            var ordered = words
                .OrderBy(word => word.PrintedChapter)
                .ThenBy(word => word.PrintedVerse)
                .ThenBy(word => word.Position)
                .ToList();

            if (Locate([.. ordered.Select(word => word.Surface)], ruling.Word, ruling.Occurrence, witnessText.Language)
                is { } at)
            {
                overruled.Add(ordered[at].Id);
            }
            else
            {
                logger.LogWarning(
                    "The link ruling on {Reference} names {Word} ({Occurrence}), which {Witness} does not have there; "
                    + "correct the ruling in {File}", ruling.Reference, ruling.Word, ruling.Occurrence, witness,
                    DefaultFile);
            }
        }

        return overruled;
    }

    /// <summary>
    /// Where in a verse's words the <paramref name="occurrence"/>th with the letters of
    /// <paramref name="word"/> stands, accents and case aside, so that <c>Ἑαυτοὺς</c> at the head of a
    /// verse is counted with the <c>ἑαυτοὺς</c> after it; null where the verse has fewer.
    /// </summary>
    internal static int? Locate(IReadOnlyList<string> surfaces, string word, int occurrence, string? language)
    {
        var letters = WordFolding.Fold(word, language);
        var seen = 0;
        for (var at = 0; at < surfaces.Count; at++)
        {
            if (string.Equals(WordFolding.Fold(surfaces[at], language), letters, StringComparison.Ordinal)
                && ++seen == occurrence)
            {
                return at;
            }
        }

        return null;
    }

    /// <summary><c>Matthew 16:21</c> as the canonical book, chapter and verse.</summary>
    internal static bool Address(string reference, out int book, out int chapter, out int verse)
    {
        (book, chapter, verse) = (0, 0, 0);
        var space = reference.LastIndexOf(' ');
        if (space <= 0
            || reference[(space + 1)..].Split(':') is not [var c, var v]
            || !int.TryParse(c, out chapter)
            || !int.TryParse(v, out verse)
            || BibleBookAbbreviation.GetAbbreviation(reference[..space]) is not { } name)
        {
            return false;
        }

        book = name.Ordinal;
        return true;
    }

    /// <summary>
    /// A record's witness words without the ones a ruling sets aside: whether it still names any,
    /// and whether it lost one.
    /// </summary>
    internal static (bool Kept, bool Trimmed) Trim(List<long> witness, IReadOnlySet<long> overruled)
    {
        if (overruled.Count == 0 || witness.RemoveAll(overruled.Contains) == 0)
        {
            return (true, false);
        }

        return (witness.Count > 0, true);
    }
}
