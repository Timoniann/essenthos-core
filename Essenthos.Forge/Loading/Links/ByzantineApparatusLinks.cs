using Essenthos.Core.Byzantine;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Robinson's apparatus to the Byzantine Textform, written as the variation it states
/// (<see cref="ByzantineApparatus"/>): <c>stated-by-source</c>, no confidence, the shape
/// <see cref="PrintedEditionLinks"/> writes for the Received Text's two editions.
///
/// <para>
/// **Each kind of note is laid against the loaded edition it describes.** The Nestle-Aland and Editio
/// Critica Maior notes against Nestle 1904, the critical text the corpus holds; the notes where the
/// Byzantine manuscripts divide against Scrivener's Received Text, which often prints the other half.
/// The three letters the repository does not explain (C, M and S, in Acts and the Catholic Letters)
/// are not read.
/// </para>
/// </summary>
internal static class ByzantineApparatusLinks
{
    public const string Credit = Sources.ByzantineApparatusCredit;

    /// <summary>The notes describing the critical editions: Nestle-Aland 28 and the Editio Critica Maior.</summary>
    public static readonly IReadOnlySet<char> Critical = new HashSet<char> { 'N', 'E' };

    /// <summary>The notes where the Byzantine manuscripts divide between two readings.</summary>
    public static readonly IReadOnlySet<char> Divided = new HashSet<char> { 'B' };

    /// <summary>Each witness, with the notes laid against it.</summary>
    public static readonly IReadOnlyList<(string Witness, IReadOnlySet<char> Kinds)> Witnesses =
    [
        (NestleTextSource.Slug, Critical),
        (Sources.ScrivenerSlug, Divided),
    ];

    internal static readonly Dictionary<char, string> Notes = new()
    {
        ['N'] = "where Nestle-Aland 28 reads otherwise, and Nestle 1904 prints its reading at the place",
        ['E'] = "where the Editio Critica Maior reads otherwise, and Nestle 1904 prints its reading at the place",
        ['B'] = "where the Byzantine manuscripts divide, and Scrivener prints the other reading at the place",
    };

    /// <summary>The apparatus of every book, by canonical address.</summary>
    /// <param name="folder">The Byzantine Textform's folder, which holds <c>ccat/</c> beside the parse.</param>
    public static Dictionary<(int Book, int Chapter, int Verse), CcatVerse> Read(string folder)
    {
        var verses = new Dictionary<(int, int, int), CcatVerse>(8_000);
        for (var at = 0; at < ByzantineTextSource.Books.Count; at++)
        {
            var path = Path.Combine(folder, "ccat", $"{ByzantineTextSource.Books[at]}.TXT");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    $"Robinson's apparatus is not on this machine: {path} is missing. Run scripts/fetch-byzantine.ps1, "
                    + "which takes it at the commit the loaded text came from.", path);
            }

            foreach (var verse in CcatReader.Read(File.ReadAllText(path)))
            {
                verses[(ByzantineTextSource.FirstCanonicalOrdinal + at, verse.Chapter, verse.Number)] = verse;
            }
        }

        return verses;
    }

    /// <summary>
    /// Whether the pair already says the opposite about the note's words. A word cannot be shown as
    /// having no counterpart and as rendered at once, and one set of words carries one relation.
    /// </summary>
    internal static bool Contrary(ApparatusLink link, PairStanding standing)
    {
        if (standing.Shapes.TryGetValue(LinkShape.Of(link.From, link.To), out var relation))
        {
            return relation != link.Relation;
        }

        var absence = link.Relation is LinkRelation.Expands or LinkRelation.Omits;
        return link.From.Concat(link.To).Any(absence ? standing.Rendered.Contains : standing.Absent.Contains);
    }

    internal sealed record PairStanding(
        Dictionary<Guid, LinkRelation> Shapes,
        HashSet<long> Rendered,
        HashSet<long> Absent);
}
