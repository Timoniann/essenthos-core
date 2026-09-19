using System.Diagnostics;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Retitled">Records that were a person and are now held as a title.</param>
/// <param name="Written">Titles no dataset held as anybody, written as records of their own.</param>
/// <param name="Retired">Records written for one bearer of a title, which the title now answers for.</param>
/// <param name="Bearers">People the text gives a title to, newly joined to it.</param>
/// <param name="Missing">
/// Titles or bearers whose record the encyclopedia does not hold, which is a corpus not yet loaded
/// or a slug the file has to follow.
/// </param>
internal sealed record TitleOutcome(
    bool AlreadyLoaded,
    int Retitled,
    int Written,
    int Retired,
    int Bearers,
    int Missing,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the names the owner ruled titles are already held as titles"
            : $"{Retitled} records held as titles rather than as one person, {Written} titles written, " +
              $"{Bearers} bearers joined to their titles and {Retired} records for a single bearer of one " +
              $"withdrawn, in {Elapsed}" +
              (Missing > 0 ? $"; {Missing} titles or bearers name a record the encyclopedia does not hold" : "");
}

/// <summary>
/// The names the owner ruled are titles rather than one man's — Abimelech of the kings of Gerar,
/// Phicol, Ahuzzath — held as what the decision says they are.
///
/// <para>
/// **The record is kept and its kind changes.** The occurrences, the verses the datasets list, the
/// names, their Strong numbers and their forms in every language are all still true of the title,
/// and a slug readers and links already reach keeps reaching it. What stops being true is that it
/// is one person, so the kind, the line under the name and the notes are the decision's, the source
/// is the decision's, and the claims that established it as one man are withdrawn: a reading that
/// assigned sixteen verses to <em>this bearer</em> is the answer the decision replaced, and a page
/// listing it under what established the record would say the opposite of the record. A dataset's
/// own testimony that it holds the entry as a person stays, because that is still what it says.
/// </para>
///
/// <para>
/// **What the decision leaves open is written down, not implied.** Whether Abraham's Abimelech and
/// Isaac's are one man, two men of one name, or two holders of a title is exactly what is not
/// known, so each reading is an alternative on the record with its reason, and the page says the
/// identification is open.
/// </para>
///
/// <para>
/// **A record written for one bearer is withdrawn.** A reading of the namesake verses had split
/// Isaac's king off as a second Abimelech; the title answers for both accounts without deciding
/// that, and the second man survives as one of the alternatives rather than as a page.
/// </para>
///
/// <para>
/// **A title no dataset holds is written.** Pharaoh is eight persons in the dataset, one for each
/// king the episodes tell apart, and no record for the word itself; the high priest and the
/// governor are offices, not anybody's name. Those are records of this corpus's own, with the names
/// the file gives them and nothing inherited.
/// </para>
///
/// <para>
/// **A bearer is joined only where a verse names both.** <em>Pharaoh-nechoh</em>, <em>Hilkiah the
/// high priest</em>, <em>Tiberius Caesar</em>: the person and the title stand in one verse, and that
/// verse is kept with the row. Where the text gives the title and nobody's name, as with the
/// Rabshakeh at the wall or the Candace whose treasurer Philip met, the title stands with no bearer.
/// </para>
///
/// <para>
/// Idempotent per record. It runs after every pass that writes a person and before the references
/// and descriptions, so on a cold corpus the records are persons while names are resolved among
/// persons and titles by the time anything is read off them; on a loaded corpus a record already
/// held as a title under this decision is left exactly as it is.
/// </para>
/// </summary>
internal sealed class TitleLoader(AppDbContext db, ILogger<TitleLoader> logger)
{
    public async Task<TitleOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var decision = SenseReadingFiles.Titles();

        var wanted = decision.Titles
            .SelectMany(title => (title.Replaces ?? []).Select(replaced => replaced.Slug)
                .Concat(title.Alternatives?.Select(alternative => alternative.Slug) ?? [])
                .Concat(title.Bearers?.Select(bearer => bearer.Slug) ?? [])
                .Append(title.Slug))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var records = await db.Entities
            .Where(e => wanted.Contains(e.Slug))
            .Include(e => e.Claims)
            .Include(e => e.Alternatives)
            .AsSplitQuery()
            .ToDictionaryAsync(e => e.Slug, StringComparer.Ordinal, cancellationToken);

        var joined = await db.TitleBearers
            .Where(b => wanted.Contains(b.Title!.Slug))
            .Select(b => new { Title = b.Title!.Slug, Bearer = b.Bearer!.Slug })
            .ToListAsync(cancellationToken);
        var already = joined.Select(b => (b.Title, b.Bearer)).ToHashSet();

        int retitled = 0, written = 0, retired = 0, bearers = 0, missing = 0;
        foreach (var title in decision.Titles)
        {
            var source = title.Source ?? decision.Source;
            if (!records.TryGetValue(title.Slug, out var record) && title.Names is not null)
            {
                record = Write(title, source);
                records[title.Slug] = record;
                db.Entities.Add(record);
                written++;
            }

            if (record is null)
            {
                logger.LogWarning(
                    "The decision holds \"{Slug}\" as a title, and the encyclopedia holds no record of that " +
                    "slug. Either the encyclopedia has not been loaded yet or the record was renamed, and " +
                    "TitleRecords.json has to follow it",
                    title.Slug);
                missing++;
                continue;
            }

            if (record.Kind != EntityKind.Title
                || !record.Claims.Any(claim => claim.Source == source))
            {
                Retitle(record, title, decision, source, records);
                retitled++;
            }

            foreach (var bearer in title.Bearers ?? [])
            {
                if (already.Contains((title.Slug, bearer.Slug)))
                {
                    continue;
                }

                if (!records.TryGetValue(bearer.Slug, out var holder) || Verse(bearer.Reference) is not { } verse)
                {
                    logger.LogWarning(
                        "The title {Title} names {Bearer} at {Reference} as its bearer, and either the " +
                        "encyclopedia holds no record of that slug or the reference is not a verse. " +
                        "TitleRecords.json has to follow the record, or the reference be written as 2KI 23:29",
                        title.Slug, bearer.Slug, bearer.Reference);
                    missing++;
                    continue;
                }

                db.TitleBearers.Add(new TitleBearer
                {
                    Title = record,
                    Bearer = holder,
                    CanonicalBook = verse.Book,
                    CanonicalChapter = verse.Chapter,
                    CanonicalVerse = verse.Verse,
                    Note = bearer.Why,
                    Source = source,
                });
                already.Add((title.Slug, bearer.Slug));
                bearers++;
            }

            foreach (var replaced in title.Replaces ?? [])
            {
                if (records.Remove(replaced.Slug, out var bearer))
                {
                    logger.LogInformation(
                        "Withdrew {Slug}, a record for one bearer of the title {Title}: {Why}",
                        replaced.Slug, title.Slug, replaced.Why);
                    db.Entities.Remove(bearer);
                    retired++;
                }
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        var outcome = new TitleOutcome(
            retitled == 0 && written == 0 && retired == 0 && bearers == 0 && missing == 0,
            retitled,
            written,
            retired,
            bearers,
            missing,
            started.Elapsed);
        logger.LogInformation("The titles: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>
    /// A title no dataset holds, as a record of this corpus's own. Its claim and alternatives are
    /// written by <see cref="Retitle"/> like any other title's, so a record written here and one
    /// that was a person say the same things about themselves.
    /// </summary>
    private static Entity Write(TitleRecord title, string source) =>
        new()
        {
            Kind = EntityKind.Title,
            Slug = title.Slug,
            Name = title.Name,
            SourceId = OwnSourceId + title.Slug,
            Source = source,
            Names =
            [
                .. title.Names!.Select(name => new EntityName
                {
                    Label = name.Label,
                    Hebrew = name.Hebrew,
                    HebrewTransliterated = name.HebrewTransliterated,
                    Greek = name.Greek,
                    GreekTransliterated = name.GreekTransliterated,
                    Meaning = name.Meaning,
                    HebrewStrongNumber = name.HebrewStrongNumber,
                    GreekStrongNumber = name.GreekStrongNumber,
                    Kind = TitleName,
                }),
            ],
        };

    private const string OwnSourceId = "essenthos:title:";

    /// <summary>The kind of label every name of a written title is, as the dataset spells it.</summary>
    private const string TitleName = "title";

    /// <summary>
    /// A reference as the file writes it, a book's code and then chapter and verse, in the
    /// canonical frame; null where it is not one.
    /// </summary>
    internal static (int Book, int Chapter, int Verse)? Verse(string reference)
    {
        var space = reference.LastIndexOf(' ');
        if (space <= 0
            || BookReferences.ResolveOrdinal(reference[..space]) is not { } book
            || reference[(space + 1)..].Split(':') is not [var chapter, var verse]
            || !int.TryParse(chapter, out var c)
            || !int.TryParse(verse, out var v))
        {
            return null;
        }

        return (book, c, v);
    }

    private static void Retitle(
        Entity record,
        TitleRecord title,
        TitleDecision decision,
        string source,
        IReadOnlyDictionary<string, Entity> records)
    {
        record.Kind = EntityKind.Title;
        record.Name = title.Name;
        record.Distinguisher = title.Distinguisher;
        record.Notes = title.Notes;
        record.Sex = null;
        record.Source = source;

        foreach (var superseded in record.Claims
                     .Where(claim => claim.Method != LinkMethod.StatedBySource)
                     .ToList())
        {
            record.Claims.Remove(superseded);
        }

        record.Claims.Add(new EntityClaim
        {
            Method = EnumSpelling.ToLinkMethod(decision.Method),
            Confidence = null,
            Source = source,
            Note = title.Why,
        });

        foreach (var stale in record.Alternatives.Where(a => a.Source == source).ToList())
        {
            record.Alternatives.Remove(stale);
        }

        foreach (var alternative in title.Alternatives ?? [])
        {
            var other = alternative.Slug is null ? null : records.GetValueOrDefault(alternative.Slug);
            record.Alternatives.Add(new EntityAlternative
            {
                Alternative = other,
                Describes = other is null ? alternative.Describes ?? alternative.Slug : null,
                Reason = alternative.Reason,
                Source = source,
            });
        }
    }
}
