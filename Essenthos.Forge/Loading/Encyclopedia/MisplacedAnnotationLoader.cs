using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <param name="Listed">Verses the list names a record wrongly named in.</param>
/// <param name="Removed">Annotations taken off the record on this run, in every text.</param>
/// <param name="Missing">Entries naming a record the encyclopedia does not hold, or no verse.</param>
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
/// The settled names (<c>names/&lt;REF&gt;</c>) were written with these taken off; what is left here is the list a
/// pass that names words holds out (<see cref="Withhold"/>).
/// </para>
/// </summary>
internal static class MisplacedAnnotationLoader
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.MisplacedAnnotations.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// The pending annotations the list would take off again, held out before a pass writes them:
    /// each record named, in each verse the list takes it off.
    /// </summary>
    internal const string Withhold =
        """
        DELETE FROM pending_annotation a
        USING word w, verse_reference r,
              unnest(@entities, @books, @chapters, @verses) AS m(entity_id, book, chapter, verse)
        WHERE a.entity_id = m.entity_id
          AND w.id = a.word_id
          AND r.verse_id = w.verse_id AND r.is_primary
          AND r.canonical_book = m.book AND r.canonical_chapter = m.chapter AND r.canonical_verse = m.verse
        """;

    /// <summary>
    /// The list as the parameters of <see cref="Withhold"/>, so a pass that names words writes nothing
    /// this step would take back. Entries naming no record or no verse are left for this step to report.
    /// </summary>
    internal static async Task<(string Name, object Value)[]> Withheld(AppDbContext db, CancellationToken cancellationToken)
    {
        var misplaced = Read().Annotations;
        var slugs = misplaced.Select(m => m.Record).Distinct().ToList();
        var records = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .ToDictionaryAsync(e => e.Slug, e => e.Id, StringComparer.Ordinal, cancellationToken);
        var rows = misplaced
            .Where(entry => records.ContainsKey(entry.Record))
            .SelectMany(entry => (Citation.Parse(entry.Reference)?.Verses ?? [])
                .Select(at => (Entity: records[entry.Record], at.Book, at.Chapter, at.Verse)))
            .ToList();
        return
        [
            ("entities", rows.Select(r => r.Entity).ToArray()), ("books", rows.Select(r => r.Book).ToArray()),
            ("chapters", rows.Select(r => r.Chapter).ToArray()), ("verses", rows.Select(r => r.Verse).ToArray()),
        ];
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
