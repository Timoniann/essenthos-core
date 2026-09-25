using System.Text.RegularExpressions;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Corpus;

/// <summary>
/// Who a word names, as the reader is told it.
///
/// A word may carry several annotations, because several methods may speak about it and the schema
/// keeps them apart rather than letting the first one win. What a reader is shown is one answer, so
/// the rule for picking it lives here and nowhere else: a word read in a chapter and the same word
/// opened in the panel must not name two different people.
///
/// <para>
/// The rule is the one <see cref="ClaimStanding"/> already states for links — testimony outranks
/// inference and a person outranks both — with one addition. Where two methods of the same standing
/// name two different entities, nothing is shown. That is not a gap to be filled later by picking
/// the more frequent or the nearer one; it is the corpus saying it does not know, which is the
/// answer for the twenty-three men called Zechariah and the reason the reader can trust the card
/// when it does appear.
/// </para>
///
/// <para>
/// A model's reading of the passage stands below every resolution the lexicon made, so the readings
/// can only add answers where there were none and can never take one away: a word the numbers
/// already settled shows the same person it showed before any reading was loaded. The card says
/// which of the two it is — the method, the confidence and the source travel with it — because a
/// reader who cannot tell a reading from a resolution is being asked to trust both equally.
/// </para>
/// </summary>
internal static class Annotations
{
    /// <summary>
    /// The annotations on a set of words, in one query. A chapter is a thousand words and this is
    /// asked for every one of them, so it is a single indexed read rather than a lookup per word.
    /// </summary>
    public static async Task<Dictionary<long, EntityRefResponse>> Of(
        AppDbContext db,
        IEnumerable<long> wordIds,
        CancellationToken cancellationToken)
    {
        var ids = wordIds.Distinct().ToList();
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.WordEntities
            .Where(a => ids.Contains(a.WordId))
            .Select(a => new Claimed(
                a.WordId, a.Method, a.Confidence, a.Source, a.Note,
                a.Entity!.Kind, a.Entity.Slug, a.Entity.Name) { EntityId = a.EntityId })
            .ToListAsync(cancellationToken);

        var settled = Settle(rows);
        var namedAs = await NamedAs(db, rows, settled, cancellationToken);
        return namedAs.Count == 0
            ? settled
            : settled.ToDictionary(
                pair => pair.Key,
                pair => namedAs.TryGetValue(pair.Key, out var label) ? pair.Value with { NamedAs = label } : pair.Value);
    }

    /// <summary>
    /// The name each word names its record under, where that is not the name the record heads with.
    ///
    /// <para>
    /// A record holds every name its person bears, and heads with one: Abram, Jacob, Simon, Saul. The
    /// word a reader hovers is often the other — Abraham five chapters after Genesis 17:5 renames him,
    /// Paul after Acts 13:9 — and a card answering <em>Abram</em> over <em>Abraham</em> reads as a
    /// wrong annotation where the annotation is right. The name is not guessed from the verse's place
    /// in the book: the word was annotated through a Strong number, its own or that of the source word
    /// it was carried from, and the record's names each carry theirs, so the number says which name the
    /// verse prints. Where the number is the heading's, or more than one other name shares it, nothing
    /// is said and the card keeps the heading.
    /// </para>
    /// </summary>
    private static async Task<Dictionary<long, string>> NamedAs(
        AppDbContext db,
        List<Claimed> rows,
        Dictionary<long, EntityRefResponse> settled,
        CancellationToken cancellationToken)
    {
        var chosen = rows
            .Where(row => settled.TryGetValue(row.WordId, out var shown) && shown.Slug == row.Slug)
            .GroupBy(row => row.WordId)
            .Select(group => group.First())
            .ToList();
        if (chosen.Count == 0)
        {
            return [];
        }

        // The number the annotation went through: the word's own, or the source word's it was carried from.
        var through = chosen
            .Select(row => (row, Via: CarriedFrom(row.Note)))
            .ToList();
        var asked = through.Select(pair => pair.Via ?? pair.row.WordId).Distinct().ToList();
        var numbers = await db.Words
            .Where(w => asked.Contains(w.Id) && w.StrongNumber != null)
            .Select(w => new { w.Id, w.StrongNumber })
            .ToDictionaryAsync(w => w.Id, w => w.StrongNumber!, cancellationToken);

        var entityIds = chosen.Select(row => row.EntityId).Distinct().ToList();
        var names = (await db.EntityNames
                .Where(n => entityIds.Contains(n.EntityId) && n.Kind == ProperName)
                .Select(n => new { n.EntityId, n.Label, n.HebrewStrongNumber, n.GreekStrongNumber })
                .ToListAsync(cancellationToken))
            .ToLookup(n => n.EntityId);

        var found = new Dictionary<long, string>();
        foreach (var (row, via) in through)
        {
            var number = numbers.GetValueOrDefault(via ?? row.WordId) ?? LeadingNumber(row.Note);
            if (number is null)
            {
                continue;
            }

            var bearing = names[row.EntityId]
                .Where(n => n.HebrewStrongNumber == number || n.GreekStrongNumber == number)
                .Select(n => n.Label)
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (bearing.Count == 1 && bearing[0] != row.Name)
            {
                found[row.WordId] = bearing[0];
            }
        }

        return found;
    }

    /// <summary>The source word an annotation was carried from, as its note writes it.</summary>
    private static long? CarriedFrom(string? note)
    {
        var match = note is null ? null : Carried.Match(note);
        return match is { Success: true } && long.TryParse(match.Groups[1].ValueSpan, out var id) ? id : null;
    }

    /// <summary>The number a source-word annotation's note leads with, where the word row carries none.</summary>
    private static string? LeadingNumber(string? note)
    {
        var match = note is null ? null : Leading.Match(note);
        return match is { Success: true } ? match.Groups[1].Value : null;
    }

    private static readonly Regex Carried =
        new(@"\bthrough \S+ word (\d+)", RegexOptions.CultureInvariant);

    private static readonly Regex Leading =
        new(@"^([HG]\d+),", RegexOptions.CultureInvariant);

    /// <summary>The kind a record's own name has, as against its titles and epithets.</summary>
    private const string ProperName = "proper name";

    /// <summary>
    /// The canonical verses of one chapter where some word, in any text, names each entity as the
    /// reader is told it — so a record only the Hebrew or the Greek names is there as well. Keyed by
    /// slug, the verses in order.
    ///
    /// Read from the verse references rather than from the words: the chapter is a few hundred
    /// verses across every text, and starting there keeps the join to the words that stand in it.
    /// </summary>
    public static async Task<Dictionary<string, SortedSet<int>>> InChapter(
        AppDbContext db,
        int canonicalBook,
        int canonicalChapter,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from reference in db.VerseReferences
                where reference.IsPrimary
                      && reference.CanonicalBook == canonicalBook
                      && reference.CanonicalChapter == canonicalChapter
                join word in db.Words on reference.VerseId equals word.VerseId
                join annotation in db.WordEntities on word.Id equals annotation.WordId
                select new
                {
                    reference.CanonicalVerse,
                    Claimed = new Claimed(
                        annotation.WordId, annotation.Method, annotation.Confidence, annotation.Source,
                        annotation.Note, annotation.Entity!.Kind, annotation.Entity.Slug, annotation.Entity.Name),
                })
            .ToListAsync(cancellationToken);

        var verseOf = new Dictionary<long, int>();
        foreach (var row in rows)
        {
            verseOf[row.Claimed.WordId] = row.CanonicalVerse;
        }

        var verses = new Dictionary<string, SortedSet<int>>(StringComparer.Ordinal);
        foreach (var (wordId, named) in Settle([.. rows.Select(row => row.Claimed)]))
        {
            if (!verses.TryGetValue(named.Slug, out var at))
            {
                verses[named.Slug] = at = [];
            }

            at.Add(verseOf[wordId]);
        }

        return verses;
    }

    /// <summary>
    /// The same over a whole book: the canonical verses naming each entity, as chapter and verse.
    /// Genesis is some thirty-five thousand annotations across its texts, read in one query.
    /// </summary>
    public static async Task<Dictionary<string, HashSet<(int Chapter, int Verse)>>> InBook(
        AppDbContext db,
        int canonicalBook,
        CancellationToken cancellationToken)
    {
        var rows = await (
                from reference in db.VerseReferences
                where reference.IsPrimary && reference.CanonicalBook == canonicalBook
                join word in db.Words on reference.VerseId equals word.VerseId
                join annotation in db.WordEntities on word.Id equals annotation.WordId
                select new
                {
                    reference.CanonicalChapter,
                    reference.CanonicalVerse,
                    Claimed = new Claimed(
                        annotation.WordId, annotation.Method, annotation.Confidence, annotation.Source,
                        annotation.Note, annotation.Entity!.Kind, annotation.Entity.Slug, annotation.Entity.Name),
                })
            .ToListAsync(cancellationToken);

        var verseOf = new Dictionary<long, (int, int)>();
        foreach (var row in rows)
        {
            verseOf[row.Claimed.WordId] = (row.CanonicalChapter, row.CanonicalVerse);
        }

        var verses = new Dictionary<string, HashSet<(int Chapter, int Verse)>>(StringComparer.Ordinal);
        foreach (var (wordId, named) in Settle([.. rows.Select(row => row.Claimed)]))
        {
            if (!verses.TryGetValue(named.Slug, out var at))
            {
                verses[named.Slug] = at = [];
            }

            at.Add(verseOf[wordId]);
        }

        return verses;
    }

    /// <summary>The same for one word, which is what the word panel asks.</summary>
    public static async Task<EntityRefResponse?> Of(
        AppDbContext db,
        long wordId,
        CancellationToken cancellationToken)
    {
        var found = await Of(db, [wordId], cancellationToken);
        return found.GetValueOrDefault(wordId);
    }

    /// <summary>
    /// One answer per word, or none where the strongest two disagree. Ordered by standing first and
    /// by confidence within it, so a hand correction beats a resolution however sure the resolution
    /// was — the standing is what the method knew before it started, and no confidence can make a
    /// guess into a reading.
    /// </summary>
    private static Dictionary<long, EntityRefResponse> Settle(List<Claimed> rows) =>
        rows.GroupBy(row => row.WordId)
            .Select(group => new
            {
                group.Key,
                Ranked = group
                    .OrderByDescending(row => ClaimStanding.Of(row.Method))
                    .ThenByDescending(row => row.Confidence ?? 1)
                    .ToList(),
            })
            .Where(word => word.Ranked.Count == 1 || !Disputed(word.Ranked[0], word.Ranked[1]))
            .ToDictionary(word => word.Key, word => Show(word.Ranked[0]));

    private static bool Disputed(Claimed best, Claimed next) =>
        best.Slug != next.Slug
        && ClaimStanding.Of(best.Method) == ClaimStanding.Of(next.Method)
        && (best.Confidence ?? 1) == (next.Confidence ?? 1);

    private static EntityRefResponse Show(Claimed claimed) =>
        new(EnumSpelling.Of(claimed.Kind), claimed.Slug, claimed.Name)
        {
            Method = EnumSpelling.Of(claimed.Method),
            Confidence = claimed.Confidence,
            Source = claimed.Source,
            Note = claimed.Note,
        };

    /// <summary>What a claim about one word says, flattened for the pick.</summary>
    private sealed record Claimed(
        long WordId,
        LinkMethod Method,
        double? Confidence,
        string Source,
        string? Note,
        EntityKind Kind,
        string Slug,
        string Name)
    {
        /// <summary>The record's row, read only where the name an occurrence is shown under is asked.</summary>
        public int EntityId { get; init; }
    }
}
