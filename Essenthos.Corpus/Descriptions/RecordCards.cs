using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Descriptions;

/// <summary>
/// A record as a card or a tree shows it, in a language of names and a language of words: the name, the line under it and
/// what the line was chosen from. Composed here from the served tables, by the serving build for every record
/// (<c>record_name</c>, <c>record_line</c>) and by a request for a pair of languages the build did not make.
/// </summary>
/// <param name="Name">The heading the record is filed under, or the name a renamed person is known by.</param>
/// <param name="LocalName">That name in the language of names, where the corpus holds one.</param>
/// <param name="Distinguisher">The source's English line.</param>
/// <param name="AlsoCalled">The name a renamed person's record is filed under, which a finder also knows them by.</param>
/// <param name="Local">Our own line in the language of words, where the corpus rendered one.</param>
/// <param name="Described">
/// What the corpus says the record is, in the language of words, as the index phrases it, where there is no
/// <paramref name="Local"/> line; null where nothing is said or it could only be said in another language.
/// </param>
internal sealed record RecordCard(
    int Id,
    string Slug,
    string Kind,
    string Name,
    string? LocalName,
    string? Distinguisher,
    string? Sex,
    string? AlsoCalled,
    string? Local,
    string? Described)
{
    /// <summary>
    /// The one plain line a reader is shown under the name, as the reader picks it: ours in the language of words, else the
    /// source's English words where the words are English, else what the corpus says. Null where there is none.
    /// </summary>
    public string? Line(string prose) => Local ?? (Quoted(prose) ? Distinguisher : Described);

    /// <summary>Whether <see cref="Line"/> is the source's words rather than ours.</summary>
    public bool Quoted(string prose) => Local is null && Distinguisher is not null && prose == RecordCards.English;
}

internal static class RecordCards
{
    /// <summary>Raise with any change to what a card is composed from or how: a stored card is made again only when it moves.</summary>
    public const int Version = 1;

    /// <summary>The language of words in which the source's own English line is the one shown.</summary>
    public const string English = "eng";

    /// <summary>The records named, in no particular order; a slug no record has is left out.</summary>
    public static async Task<List<RecordCard>> Of(
        AppDbContext db,
        IReadOnlyCollection<string> slugs,
        string? names,
        string? prose,
        CancellationToken cancellationToken)
    {
        var records = await db.Entities
            .Where(e => slugs.Contains(e.Slug))
            .Select(e => new { e.Id, e.Slug, e.Kind, e.Name, e.Distinguisher, e.Sex })
            .ToListAsync(cancellationToken);
        var ids = records.Select(p => p.Id).ToList();

        var local = await EntityNames.Of(db, ids, names, cancellationToken);
        var renamed = await RenamedHeadings.Of(db, ids, names, cancellationToken);
        var words = ReaderLanguages.Prose(prose, names);
        var lines = await EntityDistinguishers.OfSlugs(db, [.. records.Select(p => p.Slug)], words, cancellationToken);
        // Most people have no stored line in the language asked for; the line the index phrases from their descriptor tells
        // their namesakes apart instead. Only its words travel: the descriptor whole is some 18 KB a person.
        var spoken = DescriptorPhrasings.Spoken(words);
        var described = await Descriptors.Of(
            db,
            records.Where(p => !lines.ContainsKey(p.Slug)).Select(p => p.Slug),
            words,
            cancellationToken,
            names);

        return
        [
            .. records.Select(p => new RecordCard(
                p.Id,
                p.Slug,
                Spelled(p.Kind),
                renamed.GetValueOrDefault(p.Id)?.Name ?? p.Name,
                renamed.TryGetValue(p.Id, out var heads) ? heads.LocalName : local.GetValueOrDefault(p.Id),
                p.Distinguisher,
                p.Sex,
                renamed.TryGetValue(p.Id, out var also) ? also.AlsoCalled : null,
                lines.GetValueOrDefault(p.Slug),
                described.TryGetValue(p.Slug, out var descriptor) && descriptor.Language == spoken
                    ? string.Concat(descriptor.Parts.Select(part => part.Text)).Trim() is { Length: > 0 } said ? said : null
                    : null)),
        ];
    }

    private static string Spelled(Database.Entities.Enums.EntityKind kind) => EnumSpelling.Of(kind);
}
