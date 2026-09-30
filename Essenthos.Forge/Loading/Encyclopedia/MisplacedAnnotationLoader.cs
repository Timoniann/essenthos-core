using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Listed">Verses the list names a record wrongly named in.</param>
/// <param name="Removed">Annotations taken off the record on this run, in every text.</param>
/// <param name="Missing">Entries naming a record the encyclopedia does not hold, or no verse.</param>
internal sealed record MisplacedAnnotationOutcome(int Listed, int Removed, int Missing, TimeSpan Elapsed)
{
    public override string ToString() =>
        Removed == 0 && Missing == 0
            ? $"the {Listed} verses a record was wrongly named in name it no more"
            : $"{Removed} annotations taken off the records the {Listed} verses wrongly named, " +
              $"{Missing} entries naming no record or no verse, in {Elapsed}";
}

/// <summary>
/// The verses where a word names a record it does not mean, taken off the record in every text.
///
/// <para>
/// A resolution by Strong number names every word of a number after the one record the number
/// reaches, and a number can be borne by what the record is not: <em>Mercurius</em> in Acts 14:12 is
/// the god the Lycaonians called Paul, and the one record of the name is the man Paul greets in Romans
/// 16:14. The list says which record, at which verse, and why; every annotation of that record on a
/// word of that verse goes, the carried copies with the seed, and the reference read off them goes
/// with the next reading of the words. Nothing is written in its place.
/// </para>
///
/// <para>
/// Run after every pass that names a word, so an answer a later pass wrote again is taken back
/// again. Idempotent.
/// </para>
/// </summary>
internal sealed class MisplacedAnnotationLoader(AppDbContext db, ILogger<MisplacedAnnotationLoader> logger)
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.MisplacedAnnotations.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private const string Removal =
        """
        DELETE FROM word_entity a
        USING word w, verse_reference r
        WHERE a.entity_id = {0}
          AND w.id = a.word_id
          AND r.verse_id = w.verse_id AND r.is_primary
          AND r.canonical_book = {1} AND r.canonical_chapter = {2} AND r.canonical_verse = {3}
        """;

    public async Task<MisplacedAnnotationOutcome> Load(CancellationToken cancellationToken = default) =>
        await Load(Read().Annotations, cancellationToken);

    internal async Task<MisplacedAnnotationOutcome> Load(
        IReadOnlyList<MisplacedAnnotation> misplaced,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.StartNew();
        var slugs = misplaced.Select(m => m.Record).Distinct().ToList();
        var records = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);

        int removed = 0, missing = 0;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        foreach (var entry in misplaced)
        {
            var citation = Citation.Parse(entry.Reference);
            if (!records.TryGetValue(entry.Record, out var record) || citation is null)
            {
                logger.LogWarning(
                    "MisplacedAnnotations.json takes \"{Record}\" off {Reference}, and the encyclopedia holds no " +
                    "such record or the reference is no verse. Correct the entry, or take it off the list",
                    entry.Record, entry.Reference);
                missing++;
                continue;
            }

            foreach (var (book, chapter, verse) in citation.Verses)
            {
                removed += await db.Database.ExecuteSqlRawAsync(
                    Removal, [record, book, chapter, verse], cancellationToken);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        var outcome = new MisplacedAnnotationOutcome(misplaced.Count, removed, missing, started.Elapsed);
        logger.LogInformation("Took the misplaced annotations off their records: {Outcome}", outcome);
        return outcome;
    }

    internal static MisplacedAnnotationFile Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<MisplacedAnnotationFile>(stream, Shape)
               ?? throw new InvalidDataException($"{Resource} is empty. It should hold who decided and the annotations.");
    }
}

/// <param name="DecidedBy">Who read the verses, and on whose word.</param>
internal sealed record MisplacedAnnotationFile(string DecidedBy, IReadOnlyList<MisplacedAnnotation> Annotations);

/// <param name="Record">The record the verse's words wrongly name, by address.</param>
/// <param name="Reference">The verse, as <c>ACT 14:12</c>.</param>
/// <param name="Why">What the word names instead.</param>
internal sealed record MisplacedAnnotation(string Record, string Reference, string Why);
