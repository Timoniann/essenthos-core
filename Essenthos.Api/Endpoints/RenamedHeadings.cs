using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>The name a renamed person is headed with, and the name the record was headed with before.</summary>
/// <param name="Name">The name most verses call the person by, as the dataset labels it: <em>Paul</em>.</param>
/// <param name="LocalName">
/// The same name in the language asked for, as its texts print it most: <em>Павло</em>. Null where
/// the language is the headword's, or its texts print no spelling of the person.
/// </param>
/// <param name="Former">The name the record is filed under, which the reader is told it is also called: <em>Saul</em>.</param>
/// <param name="LocalFormer">That name in the language asked for, where the corpus holds it: <em>Савл</em>.</param>
internal sealed record RenamedHeading(string Name, string? LocalName, string Former, string? LocalFormer)
{
    /// <summary>
    /// The genitive of <see cref="LocalName"/> as the same texts print it, where the language declines
    /// a name and one of the printed spellings is the genitive by its ending: <em>Павла</em>,
    /// <em>Авраама</em>. Null where the language does not decline, the heading is the filed name's, or
    /// no printed spelling ends as the genitive does. A line that puts the name in the genitive names
    /// the person by the heading only where this is known.
    /// </summary>
    public string? LocalGenitive { get; init; }

    /// <summary>
    /// What a reader of the language asked for is told the person is also called: the filed name in
    /// that language, and in English the English one. Null where that language heads the person as
    /// before, so a Ukrainian page never says it in English.
    /// </summary>
    public string? AlsoCalled => LocalName is null ? Former : LocalFormer;
}

/// <summary>
/// The heading of a person Scripture calls by more than one name: the name the text uses most.
///
/// <para>
/// The God of Israel is not renamed by the many verses that call him LORD: his record is headed by
/// the name itself, and every language already heads it by its own word.
/// </para>
///
/// <para>
/// The dataset files Paul under <em>Saul</em>, Barnabas under <em>Joseph</em> and Abraham under
/// <em>Abram</em> — the name each first bears — while the text calls them Paul in 158 verses and Saul
/// in 20, Barnabas in 28 and Joseph in one, Abraham in 206 and Abram in 51. Each verse the dataset
/// lists says which name it uses, so the count is read from the records rather than kept as a list of
/// renamed people: whichever proper name labels the most verses heads, where it labels more than the
/// name the record is filed under.
/// </para>
///
/// <para>
/// In another language the heading is the spelling the texts in that language print most, taken as
/// the nominative of its family of cases: <em>Павло</em>, <em>Варнава</em>, <em>Авраам</em>. Where
/// that family is the one the corpus already holds a nominative for, the nominative stands.
/// </para>
///
/// <para>
/// Only the heading moves. The slug, the links and every other name stay as they are, and the name
/// the record was filed under is still said, as another name of the same person.
/// </para>
/// </summary>
internal static class RenamedHeadings
{
    /// <summary>
    /// A spelling printed at least this share as often as the commonest of its family can head it: the
    /// nominative of a name whose genitive is printed more, as the renderings choose their heading.
    /// </summary>
    private const double HeadingShare = 1.0 / 3.0;

    public static async Task<Dictionary<int, RenamedHeading>> Of(
        AppDbContext db,
        IReadOnlyCollection<int> ids,
        string? language,
        CancellationToken cancellationToken)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var people = await db.Entities
            .Where(e => ids.Contains(e.Id) && e.Kind == EntityKind.Person)
            .Select(e => new { e.Id, e.Name, e.SourceId })
            .ToListAsync(cancellationToken);
        people.RemoveAll(p => DivineRecords.Contains(p.SourceId));
        var personIds = people.Select(p => p.Id).ToList();
        if (personIds.Count == 0)
        {
            return [];
        }

        var labels = await db.EntityNames
            .Where(n => personIds.Contains(n.EntityId))
            .Select(n => new { n.EntityId, n.Label, n.Kind })
            .ToListAsync(cancellationToken);
        var labelled = await db.EntityVerses
            .Where(v => personIds.Contains(v.EntityId) && v.Label != null)
            .GroupBy(v => new { v.EntityId, v.Label })
            .Select(g => new { g.Key.EntityId, g.Key.Label, Verses = g.Count() })
            .ToListAsync(cancellationToken);

        var renamed = new Dictionary<int, (string Name, string Former)>();
        foreach (var person in people)
        {
            var names = labels
                .Where(n => n.EntityId == person.Id && !EntityNames.IsTitle(n.Kind))
                .Select(n => n.Label)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var counted = labelled
                .Where(l => l.EntityId == person.Id && names.Contains(l.Label!) && !Extends(l.Label!, person.Name))
                .GroupBy(l => l.Label!, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Label: g.First().Label!, Verses: g.Sum(l => l.Verses)))
                .ToList();
            var filedUnder = counted
                .Where(l => string.Equals(l.Label, person.Name, StringComparison.OrdinalIgnoreCase))
                .Sum(l => l.Verses);
            var most = counted
                .OrderByDescending(l => l.Verses)
                .ThenBy(l => l.Label, StringComparer.Ordinal)
                .FirstOrDefault();
            if (most.Label is not null
                && !string.Equals(most.Label, person.Name, StringComparison.OrdinalIgnoreCase)
                && most.Verses > filedUnder)
            {
                renamed[person.Id] = (most.Label, person.Name);
            }
        }

        if (renamed.Count == 0)
        {
            return [];
        }

        var local = EntityNames.Local(language);
        if (local is null)
        {
            return renamed.ToDictionary(r => r.Key, r => new RenamedHeading(r.Value.Name, null, r.Value.Former, null));
        }

        var held = await EntityNames.Of(db, renamed.Keys, language, cancellationToken);
        var spellings = await db.EntityRenderings
            .Where(r => renamed.Keys.Contains(r.EntityId) && r.Text!.Language == local)
            .Select(r => new { r.EntityId, r.Form, r.Folded, r.Occurrences })
            .ToListAsync(cancellationToken);

        return renamed.ToDictionary(
            r => r.Key,
            r =>
            {
                var former = held.GetValueOrDefault(r.Key);
                var family = Family(
                    [.. spellings.Where(s => s.EntityId == r.Key).Select(s => (s.Form, s.Folded, s.Occurrences))],
                    former);
                var printed = family is null ? null : Heading(family);
                return new RenamedHeading(r.Value.Name, printed ?? former, r.Value.Former, printed is null ? null : former)
                {
                    LocalGenitive = printed is null ? null : Genitive(local, family!, printed),
                };
            });
    }

    /// <summary>The languages whose texts decline a name, so that a genitive is a spelling of its own.</summary>
    private static readonly IReadOnlySet<string> Declining = new HashSet<string>(StringComparer.Ordinal) { "ukr", "rus" };

    /// <summary>Whether a phrase in this language that wants a genitive needs a spelling of its own for it.</summary>
    public static bool Declines(string? language) => EntityNames.Local(language) is { } local && Declining.Contains(local);

    /// <summary>
    /// The genitive among the printed spellings of the heading's family: the one printed most of those
    /// whose ending a genitive of that nominative takes. A name ending in <em>-а</em> or <em>-я</em>
    /// takes <em>-и</em> (<em>Юда</em>, <em>Юди</em>; never the dative <em>Юді</em>), one in <em>-я</em>
    /// takes <em>-і</em> or <em>-ї</em> (<em>Марія</em>, <em>Марії</em>), and any other takes <em>-а</em> or
    /// <em>-я</em> (<em>Павло</em>, <em>Павла</em>); Russian likewise, with <em>-ы</em>. Chosen among what the text prints,
    /// never made: where no printed spelling ends so, there is no genitive.
    /// </summary>
    internal static string? Genitive(
        string language,
        IReadOnlyList<(string Form, string Folded, int Occurrences)> family,
        string nominative)
    {
        if (!Declining.Contains(language) || nominative.Length == 0)
        {
            return null;
        }

        var last = char.ToLowerInvariant(nominative[^1]);
        var endings = (last, language) switch
        {
            ('а', "rus") => "ыи",
            ('а', _) => "и",
            ('я', "rus") => "и",
            ('я', _) => "іїи",
            _ => "ая",
        };
        return family
            .Where(s => !string.Equals(s.Form, nominative, StringComparison.Ordinal)
                        && s.Form.Length > 0
                        && endings.Contains(char.ToLowerInvariant(s.Form[^1]))
                        && !s.Form.Contains(' '))
            .OrderByDescending(s => s.Occurrences)
            .ThenBy(s => s.Form, StringComparer.Ordinal)
            .Select(s => s.Form)
            .FirstOrDefault();
    }

    /// <summary>
    /// Whether a label is the filed name with more words to it — <em>Mary Magdalene</em>,
    /// <em>Simon the zealot</em>, <em>Sergius Paulus</em> — which says which Mary is meant rather than
    /// that she was called something else.
    /// </summary>
    private static bool Extends(string label, string name) =>
        label.Length > name.Length
        && label.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries)
            .Any(word => string.Equals(word, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The nominative of the family of spellings printed most, or null where that family is the held
    /// name's own or nothing is printed. A family is a name and its cases: <em>Авраам</em>,
    /// <em>Авраама</em>, <em>Авраамові</em>.
    /// </summary>
    internal static string? Heading(IReadOnlyList<(string Form, string Folded, int Occurrences)> spellings, string? held) =>
        Family(spellings, held) is { } family ? Heading(family) : null;

    /// <summary>
    /// The family of spellings printed most, or null where that family is the held name's own or
    /// nothing is printed.
    /// </summary>
    private static List<(string Form, string Folded, int Occurrences)>? Family(
        IReadOnlyList<(string Form, string Folded, int Occurrences)> spellings,
        string? held)
    {
        var families = new List<List<(string Form, string Folded, int Occurrences)>>();
        foreach (var spelling in spellings.OrderByDescending(s => s.Occurrences).ThenBy(s => s.Folded, StringComparer.Ordinal))
        {
            var family = families.FirstOrDefault(f => EntityNames.SameName(f[0].Folded, spelling.Folded)
                                                      || EntityNames.SameName(spelling.Folded, f[0].Folded));
            if (family is null)
            {
                families.Add([spelling]);
            }
            else
            {
                family.Add(spelling);
            }
        }

        var most = families.OrderByDescending(f => f.Sum(s => s.Occurrences)).FirstOrDefault();
        if (most is null)
        {
            return null;
        }

        var heldFolded = held is null ? null : NameFolding.Fold(held);
        if (heldFolded is not null && most.Any(s => EntityNames.SameName(heldFolded, s.Folded)))
        {
            return null;
        }

        return most;
    }

    /// <summary>
    /// The nominative of a family: its shortest spelling printed often enough to head it, never one
    /// that only declines a printed <em>-а</em> or <em>-я</em> of the same length.
    /// </summary>
    private static string Heading(List<(string Form, string Folded, int Occurrences)> most)
    {
        var commonest = most[0].Occurrences;
        var heads = most.Where(s => s.Occurrences >= commonest * HeadingShare).ToList();
        var nominatives = heads.Where(s => !Declines(s.Form, most)).ToList();
        return (nominatives.Count > 0 ? nominatives : heads)
            .OrderBy(s => s.Form.Length)
            .ThenByDescending(s => s.Occurrences)
            .ThenBy(s => s.Form, StringComparer.Ordinal)
            .First().Form;
    }

    /// <summary>The endings by which a feminine-declined name in <em>-а</em> or <em>-я</em> is in another case.</summary>
    private const string ObliqueEndings = "ыиіїуюе";

    /// <summary>
    /// Whether a spelling is another case of a printed spelling ending in <em>-а</em> or <em>-я</em> on the
    /// same stem: <em>Васемафы</em> and <em>Васемафу</em> beside <em>Васемафа</em>, <em>Варнави</em> beside
    /// <em>Варнава</em>. The genitive a text prints more often must not head the name it declines.
    /// </summary>
    private static bool Declines(string form, List<(string Form, string Folded, int Occurrences)> family) =>
        form.Length > 1
        && ObliqueEndings.Contains(char.ToLowerInvariant(form[^1]))
        && family.Any(s => s.Form.Length == form.Length
                           && "ая".Contains(char.ToLowerInvariant(s.Form[^1]))
                           && string.Equals(s.Form[..^1], form[..^1], StringComparison.Ordinal));
}
