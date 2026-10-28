using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// What a person's line says of their work and of what the text calls them: <em>physician</em> for
/// Luke (COL 4:14), <em>purple dealer</em> for Lydia, <em>thief</em> and <em>traitor</em> for Judas
/// (JHN 12:6, LUK 6:16), <em>harlot</em> for Rahab. Said after the offices and before the family,
/// each word a link to the occupation's page and cited by the verses that say it.
///
/// <para>
/// Only trades and designations are said here. Offices, military, religious and heavenly roles stay
/// in the person's section on occupations: the offices a line most needs are already its own clauses
/// (<em>king of the Israelites</em>), and a role said twice, once with its people and once without,
/// says nothing the first did not.
/// </para>
/// </summary>
internal static class OccupationLines
{
    /// <summary>The relation a person's trades are handed to a client under, among the words the API says itself.</summary>
    public const string Trade = "occupation";

    /// <summary>The relation the text's own names for a person are handed to a client under: <em>the harlot</em>.</summary>
    public const string Designation = "designation";

    /// <summary>The kind a target of these words is sent as, so a client links it to the occupation's page.</summary>
    public const string TargetKind = "occupation";

    private static readonly string[] InTheLine = ["trade", "designation"];

    /// <summary>One occupation as the line says it, in both genders, with the verses saying it.</summary>
    internal sealed record Work(
        string Relation, string Slug, string Masculine, string Feminine, string English, IReadOnlyList<VerseRefResponse> Verses);

    /// <summary>
    /// The trades and designations of these people in <paramref name="language"/>, trades first, each
    /// group in the order the text first says them.
    /// </summary>
    public static async Task<Dictionary<int, List<Work>>> Of(
        AppDbContext db, IReadOnlyCollection<int> people, string language, CancellationToken cancellationToken)
    {
        if (people.Count == 0)
        {
            return [];
        }

        var rows = await db.PersonOccupations
            .Where(p => people.Contains(p.PersonId) && InTheLine.Contains(p.Occupation!.Category))
            .Select(p => new
            {
                p.PersonId,
                p.Occupation!.Slug,
                p.Occupation.Category,
                Label = p.Occupation.Labels.Where(l => l.Language == language).Select(l => new { l.Masculine, l.Feminine }).First(),
                English = p.Occupation.Labels.Where(l => l.Language == DescriptorPhrasings.English).Select(l => l.Masculine).First(),
                Verses = p.Witnesses.Where(w => w.Kind == "profession")
                    .Select(w => new { w.CanonicalBook, w.CanonicalChapter, w.CanonicalVerse }).ToList(),
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(row =>
            {
                var verses = row.Verses
                    .Select(v => (v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse))
                    .Distinct()
                    .Order()
                    .Select(v => BookReferences.At(v.CanonicalBook, v.CanonicalChapter, v.CanonicalVerse)!)
                    .ToList();
                return (row.PersonId, Work: new Work(
                    row.Category == "trade" ? Trade : Designation, row.Slug,
                    InLine(language, row.Label.Masculine), InLine(language, row.Label.Feminine), row.English, verses));
            })
            .GroupBy(row => row.PersonId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => row.Work)
                    .OrderBy(work => work.Relation == Trade ? 0 : 1)
                    .ThenBy(work => work.Verses.Count > 0 ? work.Verses[0].BookOrdinal : int.MaxValue)
                    .ThenBy(work => work.Verses.Count > 0 ? work.Verses[0].Chapter : 0)
                    .ThenBy(work => work.Verses.Count > 0 ? work.Verses[0].Verse : 0)
                    .ThenBy(work => work.Slug, StringComparer.Ordinal)
                    .ToList());
    }

    /// <summary>
    /// The name as it stands inside a line: lower case where the language writes a common noun so
    /// (<em>physician</em>, <em>лікар</em>, <em>médico</em>), as written in German (<em>Arzt</em>).
    /// </summary>
    public static string InLine(string language, string name) =>
        language == DescriptorPhrasings.German || name.Length == 0
            ? name
            : char.ToLowerInvariant(name[0]) + name[1..];
}
