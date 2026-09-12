using System.Diagnostics;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Written">Entries added, which is all of them on a cold corpus and none after.</param>
/// <param name="Referenced">
/// Verse references added: one per entry and canonical verse, however many times the word stands in it.
/// </param>
internal sealed record TermOutcome(bool AlreadyLoaded, int Written, int Referenced, TimeSpan Elapsed)
{
    public override string ToString() =>
        AlreadyLoaded
            ? "the words for God are already entries of their own, with their verses"
            : $"{Written} words for God written as entries of their own and {Referenced} verse references " +
              $"read off every word BHSA numbers with them, in {Elapsed}";
}

/// <summary>
/// The words the text uses of God and of gods — elohim, el, eloah — as entries of their own.
///
/// <para>
/// **They are not YHVH.** The divine name is its letters, יהוה, and belongs to the God of Israel's
/// record; these are words the text says of him most of the time and of other gods, of judges and of
/// might the rest. An entry for one of them is an entry for a word and where it stands, which is why
/// its kind is <see cref="EntityKind.Term"/> and not a person.
/// </para>
///
/// <para>
/// **Everything on the record is read, nothing is guessed.** The name, the Hebrew and the definition
/// are Strong's entry; the verses are every verse in which BHSA numbers a word with it. What the page
/// cannot say yet is which of those verses mean the God of Israel and which mean a god or a judge —
/// that is a reading of each occurrence, and none is claimed here. The sentence under the name says
/// only what the counts of the King James's renderings show, with a verse for each other sense.
/// </para>
///
/// <para>
/// **Nothing annotates a word with these.** BHSA marks none of the three as a name, so the annotation
/// pass never resolves an occurrence to one, and a Strong number on a <c>term</c> record cannot
/// compete with a person's or a place's for a word marked as either.
/// </para>
///
/// <para>
/// Idempotent per entry and per verse, so a boot after the first writes nothing and a word added to
/// the list later is written on its own.
/// </para>
/// </summary>
internal sealed class TermLoader(AppDbContext db, ILogger<TermLoader> logger)
{
    private const string FromTheDictionary =
        "Essenthos, from Strong's Dictionary entry for the word and every word BHSA numbers with it";

    private const string SourceIdPrefix = "essenthos:term:";

    /// <summary>What a name row on one of these records is, beside a proper name and a title.</summary>
    internal const string NameKind = "term";

    /// <param name="Distinguisher">The line a reader meets under the name.</param>
    /// <param name="Notes">The counts that line rests on.</param>
    internal sealed record Term(string StrongNumber, string Slug, string Name, string Distinguisher, string Notes);

    /// <summary>
    /// The three words. The counts are the King James words the corpus's links set opposite each
    /// Hebrew word, measured on 2026-09-11; a verse is cited for every sense besides the God of Israel.
    /// </summary>
    internal static readonly IReadOnlyList<Term> Terms =
    [
        new("H430", "elohim", "Elohim",
            "a word for God, plural in form: most often said of YHVH, but not always — also of gods " +
            "(GEN 35:2) and of judges (EXO 22:8)",
            "The King James renders it God 2,262 times, gods 208 times, god 31 times, goddess twice and " +
            "judges four times, of the 2,518 words BHSA numbers with it. It is not the divine name: YHVH " +
            "is written יהוה, and this word is said of him and of others alike."),
        new("H410", "el", "El",
            "a word for God, singular: most often said of YHVH, but not always — also of other gods " +
            "(PSA 81:9), and of might",
            "The King James renders it God 200 times, god 13 times, gods twice, and mighty, might, power " +
            "or great nine times, of the 231 words BHSA numbers with it. It is not the divine name: YHVH " +
            "is written יהוה, and this word is said of him and of others alike."),
        new("H433", "eloah", "Eloah",
            "a word for God, singular: most often said of YHVH, but not always — also of the god of " +
            "another nation (2CH 32:15) and of a strange god (DAN 11:39)",
            "The King James renders it God 53 times, god four times and gods once, of the 59 words BHSA " +
            "numbers with it, 41 of them in Job. It is not the divine name: YHVH is written יהוה, and this " +
            "word is said of him and of others alike."),
    ];

    /// <summary>
    /// Every verse in which BHSA numbers a word with the entry, once per verse, on the record whose
    /// name row carries the number.
    /// </summary>
    private const string References =
        """
        INSERT INTO entity_verse (entity_id, canonical_book, canonical_chapter, canonical_verse,
                                  label, disputed, source)
        SELECT DISTINCT e.id, r.canonical_book, r.canonical_chapter, r.canonical_verse, NULL, FALSE, @source
        FROM entity e
        JOIN entity_name n ON n.entity_id = e.id AND n.kind = @nameKind
        JOIN text t ON t.slug = @witness
        JOIN word w ON w.text_id = t.id AND w.strong_number = n.hebrew_strong_number
        JOIN verse_reference r ON r.verse_id = w.verse_id AND r.is_primary
        WHERE e.source_id LIKE @prefix
          AND NOT EXISTS (
              SELECT 1 FROM entity_verse cited
              WHERE cited.entity_id = e.id
                AND cited.canonical_book = r.canonical_book
                AND cited.canonical_chapter = r.canonical_chapter
                AND cited.canonical_verse = r.canonical_verse
                AND cited.source = @source)
        """;

    public async Task<TermOutcome> Load(CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var numbers = Terms.Select(t => t.StrongNumber).ToList();

        var entries = await db.StrongEntries
            .Where(e => numbers.Contains(e.StrongNumber))
            .ToDictionaryAsync(e => e.StrongNumber, cancellationToken);
        if (entries.Count == 0)
        {
            logger.LogWarning(
                "Strong's Dictionary holds none of {Numbers}, so the words for God have nothing to be read " +
                "from. The lexicon is an earlier step of the same pipeline",
                string.Join(", ", numbers));
            return new TermOutcome(false, 0, 0, started.Elapsed);
        }

        var held = (await db.Entities
                .Where(e => e.SourceId.StartsWith(SourceIdPrefix))
                .Select(e => e.SourceId)
                .ToListAsync(cancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var written = 0;
        foreach (var term in Terms)
        {
            if (held.Contains(SourceIdPrefix + term.StrongNumber) || !entries.TryGetValue(term.StrongNumber, out var entry))
            {
                continue;
            }

            db.Entities.Add(new Entity
            {
                Kind = EntityKind.Term,
                Slug = await FreeSlug(term.Slug, cancellationToken),
                Name = term.Name,
                Distinguisher = term.Distinguisher,
                Notes = term.Notes,
                SourceId = SourceIdPrefix + term.StrongNumber,
                Source = FromTheDictionary,
                Names =
                [
                    new EntityName
                    {
                        Label = term.Name,
                        Hebrew = entry.Lemma,
                        HebrewTransliterated = entry.Transliteration,
                        Meaning = entry.Definition,
                        HebrewStrongNumber = term.StrongNumber,
                        Kind = NameKind,
                    },
                ],
            });
            written++;
        }

        await db.SaveChangesAsync(cancellationToken);

        var referenced = await db.Database.ExecuteSqlRawAsync(
            References,
            [
                new NpgsqlParameter("source", FromTheDictionary),
                new NpgsqlParameter("nameKind", NameKind),
                new NpgsqlParameter("witness", EntityCandidates.Witness),
                new NpgsqlParameter("prefix", SourceIdPrefix + "%"),
            ],
            cancellationToken);

        var outcome = new TermOutcome(written == 0 && referenced == 0, written, referenced, started.Elapsed);
        logger.LogInformation("The words for God: {Outcome}", outcome);
        return outcome;
    }

    /// <summary>The slug asked for, or the first numbered one nobody holds.</summary>
    private async Task<string> FreeSlug(string slug, CancellationToken cancellationToken)
    {
        var candidate = slug;
        for (var n = 2; await db.Entities.AnyAsync(e => e.Slug == candidate, cancellationToken); n++)
        {
            candidate = $"{slug}-{n}";
        }

        return candidate;
    }
}
