using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

internal sealed record AnnalsOutcome(
    bool AlreadyLoaded,
    int Paragraphs,
    int Events,
    int Ranged,
    int Undated,
    int Generated,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the Annals are already loaded"
            : $"{Events} of {Paragraphs} paragraphs anchored to a New Testament verse, {Ranged} of " +
              $"them on the first verse of a range, {Generated} titled other than by quotation and " +
              $"{Undated} left without a year of Ussher's, in {Elapsed}";
}

/// <summary>
/// Ussher's <em>Annals of the World</em>, for the four centuries the computed chronology cannot reach.
///
/// The Old Testament chronology here is arithmetic over the genealogies and the reign lengths, and
/// those stop at Artaxerxes; nothing else loaded dates a single thing the gospels narrate. Ussher
/// does, because his method is not arithmetic over Scripture alone — he reads the consular lists,
/// Josephus and the eclipse records too, which is why he can put a year on the crucifixion and the
/// computed reckoning cannot.
///
/// **He is a second witness and never the timeline.** One seventeenth-century reckoning is what
/// this is, and with no computed New Testament chronology standing beside it there is nothing to
/// hold it in place — so every row says it is his, carries the paragraph it came from, and hangs
/// its year on his own <see cref="Chronology"/> rather than on the default one.
///
/// **Public domain, transcribed under CC BY 4.0.** Ussher died in 1656 and Pierce's English is
/// 1658. What Brady Stephenson contributes is the transcription into 7,000 numbered paragraphs,
/// and that is what the attribution is for. See <c>Resources/BibleData2026/LICENCE.md</c>.
/// </summary>
internal sealed partial class UssherAnnalsLoader(AppDbContext db, ILogger<UssherAnnalsLoader> logger)
{
    internal const string Source =
        "Ussher's Annals of the World, 1658, transcribed in BibleData by Brady Stephenson, CC BY 4.0";

    private const string File = "Ussher-AnnalsOfTheWorld.csv";

    /// <summary>Where a title written for the corpus is read from, when one has been written.</summary>
    private const string TitleFile = "Ussher-Titles.csv";

    private const string Chronology = "ussher";

    /// <summary>The first book of the New Testament in the shared frame.</summary>
    private const int FirstApostolicBook = 40;

    private const string Kind = "Annal";

    /// <summary>
    /// How much of Ussher's opening sentence a title may be before it is cut.
    ///
    /// His sentences run to 583 characters and half of them past 100, so some cutting is
    /// unavoidable. Cutting is still quotation — the words are his and an ellipsis says where they
    /// stop — which rephrasing them would not be.
    /// </summary>
    private const int LongestTitle = 120;

    /// <summary>
    /// The one book code the Annals use for a different book than the shared table does.
    ///
    /// <c>JUD</c> is Jude in the USX codes this corpus resolves through, and the Annals never once
    /// mean Jude by it: all 64 uses are Judges, or Judith where the marker beside them says
    /// apocryphal. Nine of them are in chapter 1, which is the only chapter Jude has — so left
    /// alone they do not fail, they resolve, and file Judges 1 under a New Testament epistle. A
    /// citation that quietly lands in the wrong testament is exactly the wrong kind of wrong.
    /// </summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["JUD"] = "JDG",
    };

    /// <summary>
    /// The marker the transcription puts before a citation outside the sixty-six books — the
    /// Maccabees, Tobit, Esdras, the Greek additions to Esther, and Judith, which it cites as
    /// <c>JUD</c> like the book of Judges.
    ///
    /// It is what keeps <see cref="Aliases"/> honest. Judith has chapters where Judges has
    /// chapters, so five citations of a book the corpus does not hold would otherwise resolve
    /// cleanly to a book it does, and the marker beside them is the source saying which it meant.
    /// </summary>
    private const string Apocryphal = "Apc";

    private const int MarkerReach = 14;

    private const string CommonEra = "AD";

    /// <summary>
    /// The year the default reckoning calls 1 BCE, which is the axis every event row is placed on
    /// whichever reckoning dated it. Ussher's own figure is a <see cref="EventDate"/> and counts
    /// from his own creation, which is forty-two years earlier.
    /// </summary>
    private const int DefaultReckoningZero = 3961;

    public async Task<AnnalsOutcome> Load(string folder, CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(folder, File);
        if (await db.Events.AnyAsync(e => e.Source == Source, cancellationToken))
        {
            logger.LogInformation("The Annals are already loaded; nothing to do");
            return new AnnalsOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        if (!System.IO.File.Exists(path))
        {
            logger.LogWarning(
                "No {File} at {Folder}, so nothing dates the New Testament. Run scripts/fetch-bibledata.ps1",
                File, folder);
            return new AnnalsOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var ussher = await db.Chronologies.SingleOrDefaultAsync(c => c.Slug == Chronology, cancellationToken);
        if (ussher is null)
        {
            logger.LogWarning(
                "No {Chronology} chronology, so his years would have nowhere to hang. The encyclopedia " +
                "declares it and has to be loaded first",
                Chronology);
            return new AnnalsOutcome(true, 0, 0, 0, 0, 0, TimeSpan.Zero);
        }

        var started = Stopwatch.StartNew();
        var frame = BibleDataLoader.ReferenceTable.Read(folder);
        var titles = Titles(folder);
        var taken = new HashSet<string>(await db.Events.Select(e => e.Slug).ToListAsync(cancellationToken),
            StringComparer.Ordinal);

        var paragraphs = 0;
        var ranged = 0;
        var undated = 0;
        var generated = 0;
        var unresolved = new Dictionary<string, int>(StringComparer.Ordinal);
        var annals = new List<(Event One, int? Year, string Number)>(400);

        foreach (var row in Csv.Read(path))
        {
            paragraphs++;
            if (!string.Equals(row["gc_bc_ad"], CommonEra, StringComparison.Ordinal)
                || !int.TryParse(row["gc_year"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
            {
                continue;
            }

            var paragraph = row["event"];
            if (Anchor(paragraph, frame, unresolved) is not { } anchor)
            {
                continue;
            }

            var (book, chapter, verse, cited) = anchor;

            var reckoned = Reckoned(row, year, ussher.LastYearBeforeTheCommonEra);
            var isRange = cited.IndexOfAny(RangeMarks) >= 0;
            var number = row["paragraph_nr"];

            var (name, provenance, madeBy) = Title(paragraph, number, titles);
            if (provenance == EventNames.Generated)
            {
                generated++;
            }

            if (isRange)
            {
                ranged++;
            }

            if (reckoned is null)
            {
                undated++;
            }

            annals.Add((new Event
            {
                Slug = Unique($"ussher-{number}", taken),
                Name = name,
                NameSource = provenance,
                Description = paragraph,
                Kind = Kind,
                YearFromCreation = DefaultReckoningZero + year,
                CanonicalBook = book,
                CanonicalChapter = chapter,
                CanonicalVerse = verse,
                Realm = Realms.Scripture,
                Notes = Note(cited, isRange, reckoned is null ? row["am_year"] : null, year, madeBy),
                Source = Source,
            }, reckoned, number));
        }

        db.Events.AddRange(annals.Select(a => a.One));
        await db.SaveChangesAsync(cancellationToken);

        db.EventDates.AddRange(annals
            .Where(a => a.Year is not null)
            .Select(a => new EventDate
            {
                EventId = a.One.Id,
                ChronologyId = ussher.Id,
                Year = a.Year,
                Citation = $"¶{a.Number}",
            }));
        await db.SaveChangesAsync(cancellationToken);

        Report(unresolved, undated, frame);

        var outcome = new AnnalsOutcome(
            false, paragraphs, annals.Count, ranged, undated, generated, started.Elapsed);
        logger.LogInformation("Loaded the Annals: {Outcome}", outcome);
        return outcome;
    }

    private static readonly char[] RangeMarks = ['-', ','];

    /// <summary>Punctuation that means nothing at the end of a title cut short.</summary>
    private static readonly char[] Trailing = [',', ';', ':'];

    /// <summary>
    /// The first citation in the paragraph that names a verse of the New Testament, or nothing.
    ///
    /// Ussher writes his citation after the sentence it belongs to rather than before it, so the
    /// first one in the paragraph is what the paragraph is about — and a paragraph with none is
    /// not New Testament narrative at all but the Roman and Jewish history he sets beside it, of
    /// which there is more than there is gospel.
    ///
    /// The date column decides the scope and the citation decides the address, in that order,
    /// because they answer differently: forty of his paragraphs on the creation, the flood and
    /// Abraham cite Hebrews or Colossians as a proof text, and anchoring those on the first New
    /// Testament verse they name would file the sixth day of creation under Colossians 3.
    /// </summary>
    internal static (int Book, int Chapter, int Verse, string Cited)? Anchor(
        string paragraph,
        BibleDataLoader.ReferenceTable frame,
        Dictionary<string, int> unresolved)
    {
        foreach (var match in Citation().EnumerateMatches(paragraph))
        {
            var written = paragraph[match.Index..(match.Index + match.Length)];
            var space = written.IndexOf(' ');
            var code = written[..space];
            var start = Math.Max(0, match.Index - MarkerReach);
            var marked = paragraph.AsSpan(start, match.Index - start)
                .Contains(Apocryphal, StringComparison.Ordinal);

            var address = $"{(Aliases.TryGetValue(code, out var canonical) ? canonical : code)}{written[space..]}";

            // The tail of a range is not part of the address; the anchor is its first verse.
            var comma = address.IndexOfAny(RangeMarks);
            var single = comma < 0 ? address : address[..comma];

            if (marked || frame.Resolve(single) is not { } resolved)
            {
                unresolved[code] = unresolved.GetValueOrDefault(code) + 1;
                continue;
            }

            if (resolved.Book < FirstApostolicBook)
            {
                continue;
            }

            return (resolved.Book, resolved.Chapter, resolved.Verse, written);
        }

        return null;
    }

    /// <summary>
    /// Ussher's own year from his own creation, where his three date columns agree about it.
    ///
    /// His anno mundi year begins in the autumn, so a Julian year straddles two of them and the
    /// column's letter suffix says which half — which is a real distinction and the reason this
    /// reads his figure rather than computing one from the Gregorian column.
    ///
    /// It also means the two can be checked against each other, and on 118 paragraphs they do not
    /// agree: everything the transcription dates AD 33 carries anno mundi 4046 where its own
    /// Gregorian and Julian Period columns both say 4036, a ten-year gap across the whole passion
    /// narrative. Repairing a digit is a guess and writing 4046 is a wrong date, so those
    /// paragraphs get no year of his at all and say so; they keep their place on the axis, which
    /// the other two columns establish twice over.
    /// </summary>
    internal static int? Reckoned(Dictionary<string, string> row, int year, int zero)
    {
        if (!int.TryParse(row["am_year_only"], NumberStyles.Integer, CultureInfo.InvariantCulture, out var anno))
        {
            return null;
        }

        var opening = zero + year;
        return anno == opening || anno == opening + 1 ? anno : null;
    }

    private static string Note(string cited, bool isRange, string? contradicted, int year, string? madeBy)
    {
        var note = new StringBuilder(220);
        note.Append("Ussher cites ").Append(cited).Append(". ");
        note.Append(isRange
            ? "This row is anchored on the first verse of that citation; the citation is his and the " +
              "resolution into book, chapter and verse is Essenthos's."
            : "The citation is his and the resolution into book, chapter and verse is Essenthos's.");

        if (contradicted is not null)
        {
            note.Append(" His anno mundi column reads ").Append(contradicted)
                .Append(" where its own Gregorian and Julian Period columns both give AD ").Append(year)
                .Append(", so no year of his reckoning is written here.");
        }

        if (madeBy is not null)
        {
            note.Append(' ').Append(madeBy);
        }

        return note.ToString();
    }

    /// <summary>
    /// A title, and whose words it is.
    ///
    /// Ussher wrote no headings, and <see cref="Event.Name"/> cannot be null. His opening sentence
    /// is his own and needs no attribution beyond his name, so that is what a row takes unless
    /// somebody has written a better one — and a written one is marked, named and dated, because a
    /// summary that reads as a seventeenth-century chronologer's own words is a claim nobody made.
    /// Whichever it is, the paragraph itself is on the row verbatim, so the title can be checked
    /// against the thing it stands for without leaving the page.
    ///
    /// A quoted title is a **verbatim prefix** of the paragraph, and only ever shortened from the
    /// right: his own parenthetical asides stay where he put them, a citation that closes the
    /// sentence comes off the end, and a sentence too long to be a heading is cut at a word with
    /// an ellipsis saying so. Lifting an aside out of the middle would read as one sentence he
    /// never wrote, which is the small version of the thing this whole loader is careful about.
    /// </summary>
    internal static (string Name, string Provenance, string? MadeBy) Title(
        string paragraph,
        string number,
        IReadOnlyDictionary<string, (string Title, string By)> written)
    {
        if (written.TryGetValue(number, out var made))
        {
            return (made.Title, EventNames.Generated,
                $"The title is not Ussher's: it was written for this corpus by {made.By}, from the " +
                "paragraph above.");
        }

        var body = Whitespace().Replace(paragraph, " ").Trim();
        var sentence = Sentence().Match(body);
        var opening = ClosingAside().Replace(sentence.Success ? sentence.Groups[1].Value : body, "$1").Trim();

        return (opening.Length <= LongestTitle ? opening : Cut(opening), EventNames.Quoted, null);
    }

    private static string Cut(string opening)
    {
        var space = opening.LastIndexOf(' ', LongestTitle);
        return string.Concat(opening.AsSpan(0, space > 0 ? space : LongestTitle).TrimEnd(Trailing), "…");
    }

    /// <summary>
    /// Titles written for the corpus rather than quoted, keyed by paragraph number.
    ///
    /// A file beside the Annals rather than a column in them, because it is not Ussher's and must
    /// never be mistaken for a correction to what he wrote. Absent, which is what it is until
    /// somebody writes one, every title is a quotation.
    /// </summary>
    private static Dictionary<string, (string Title, string By)> Titles(string folder)
    {
        var path = Path.Combine(folder, TitleFile);
        if (!System.IO.File.Exists(path))
        {
            return [];
        }

        var written = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var row in Csv.Read(path))
        {
            var title = BibleDataLoader.Blank(row.GetValueOrDefault("title"));
            var by = BibleDataLoader.Blank(row.GetValueOrDefault("written_by"));
            var when = BibleDataLoader.Blank(row.GetValueOrDefault("written_on"));
            if (title is null || by is null || when is null)
            {
                continue;
            }

            written[row["paragraph_nr"]] = (title, $"{by} on {when}");
        }

        return written;
    }

    /// <summary>
    /// What the source cited that this corpus could not reach, and what it dated twice over.
    ///
    /// Both are upstream repairs somebody can make once, and both are counts that ought to stay
    /// still: a number that moves between two loads is how a new defect in the transcription gets
    /// noticed at all.
    /// </summary>
    private void Report(Dictionary<string, int> unresolved, int undated, BibleDataLoader.ReferenceTable frame)
    {
        if (unresolved.Count > 0)
        {
            logger.LogInformation(
                "{Rows} citations name a book outside the sixty-six or a verse the reference table does " +
                "not hold, and anchored nothing: {Codes}",
                unresolved.Values.Sum(),
                string.Join(", ", unresolved.OrderByDescending(u => u.Value).Select(u => $"{u.Key} ×{u.Value}")));
        }

        if (frame.Dangling.Count > 0)
        {
            logger.LogInformation(
                "{Rows} of those parsed as a known book and named no verse it has: {References}",
                frame.Dangling.Values.Sum(),
                string.Join(", ", frame.Dangling.OrderByDescending(d => d.Value).Select(d => $"{d.Key} ×{d.Value}")));
        }

        if (undated > 0)
        {
            logger.LogWarning(
                "{Rows} paragraphs carry an anno mundi year their own Gregorian and Julian Period columns " +
                "contradict, and were given no year of Ussher's reckoning. They keep their place on the " +
                "axis; correcting the figure is upstream work, in BibleData itself.",
                undated);
        }
    }

    private static string Unique(string slug, HashSet<string> taken)
    {
        var candidate = slug;
        var suffix = 2;
        while (!taken.Add(candidate))
        {
            candidate = $"{slug}-{suffix++}";
        }

        return candidate;
    }

    /// <summary>
    /// A book code, a chapter and a verse, with any range or list of further verses after it. The
    /// tail stops short of a second chapter — <c>ACT 8:1, 11:19</c> is two citations and not one
    /// running to verse eleven.
    /// </summary>
    [GeneratedRegex(@"\b[1-4]?[A-Z]{2,3} \d+:\d+(?:\s*[-,]\s*\d+\b(?!\s*:))*")]
    private static partial Regex Citation();

    /// <summary>
    /// A citation or a marginal source closing the sentence, which belongs to the paragraph and
    /// not to a heading taken from it. Both brackets close unreliably in the transcription, so
    /// either closing is accepted for either opening.
    /// </summary>
    [GeneratedRegex(@"\s*[({][^)}]*[)}]?\s*([.!?])?$")]
    private static partial Regex ClosingAside();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^(.+?[.!?])(?:\s|$)")]
    private static partial Regex Sentence();
}
