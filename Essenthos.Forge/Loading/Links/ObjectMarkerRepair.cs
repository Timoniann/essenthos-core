using System.Text;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links;

/// <summary>
/// Every pair a numbering already links to the Hebrew, matched again where the object marker made
/// its links (<see cref="ObjectMarker"/>, <see cref="TaggedTextLinkLoader.Rematch"/>).
///
/// <para>
/// **The numbers are read the way the pair's load read them.** A link records only the credit of the
/// numbering it came from, so that is what decides where its numbers are: the Synodal's from Bob Jones
/// University's edition, the Union Version's from FHL's module for that script, and any other pair's
/// from the translation's own words, as Luther's are. A pair whose credit names none of these is
/// reported and left alone rather than matched on numbers it was not drawn from.
/// </para>
///
/// <para>
/// It writes nothing unless told to. Words a rematch leaves without a link are left for the pair's
/// aligner or composition to reach, which reads only the words no number answers for.
/// </para>
/// </summary>
internal sealed class ObjectMarkerRepair(
    AppDbContext db,
    TaggedTextLinkLoader tagged,
    SynodalStrongLinkLoader synodal,
    UnionStrongLinkLoader union)
{
    private const string Greek = "grc";

    /// <param name="synodalEdition">The Synodal numbering's edition file.</param>
    /// <param name="unionModules">Each Chinese text's module root, by the slug it is loaded under.</param>
    /// <param name="texts">The translations to repair; null repairs every one a numbering links to the Hebrew.</param>
    public async Task<string> Run(
        string synodalEdition,
        IReadOnlyDictionary<string, string> unionModules,
        IReadOnlySet<string>? texts,
        bool apply,
        CancellationToken cancellationToken = default)
    {
        var pairs = await db.Links
            .Where(l => l.Method == LinkMethod.StrongNumber && l.ToText!.Language != Greek)
            .Select(l => new { From = l.FromText!.Slug, To = l.ToText!.Slug, l.Source })
            .Distinct()
            .ToListAsync(cancellationToken);

        var report = new StringBuilder()
            .AppendLine(apply ? "Rematched and written:" : "What a rematch would change (nothing written; add --apply):");
        foreach (var pair in pairs
                     .Where(pair => texts is null || texts.Contains(pair.From))
                     .GroupBy(pair => (pair.From, pair.To))
                     .OrderBy(pair => pair.Key))
        {
            var (from, to) = pair.Key;
            var credits = pair.Select(link => Credit(from, to, link.Source)).Distinct().ToList();
            if (credits is not [var credit] || credit == Numbering.Unknown)
            {
                report.AppendLine($"  {from} → {to}: left alone, its links name numberings this cannot read — " +
                                  string.Join("; ", pair.Select(link => link.Source)));
                continue;
            }

            var numbers = credit switch
            {
                Numbering.Synodal => await synodal.Numbers(synodalEdition, cancellationToken),
                Numbering.Union when unionModules.TryGetValue(from, out var module) =>
                    await union.Numbers(from, module, cancellationToken),
                Numbering.Union => throw new InvalidOperationException(
                    $"{from} is linked by FHL's numbers and no module for it is known. Its module belongs in " +
                    "SwordTextSource.Texts with tagged segmentation."),
                _ => null,
            };

            report.AppendLine($"  {from} → {to}: {await tagged.Rematch(from, to, numbers, apply, cancellationToken)}");
        }

        return report.ToString();
    }

    private enum Numbering
    {
        Own,
        Synodal,
        Union,
        Unknown,
    }

    private static Numbering Credit(string from, string to, string source) =>
        source.StartsWith(SynodalStrongLinkLoader.Credit, StringComparison.Ordinal) ? Numbering.Synodal
        : source.StartsWith(UnionStrongLinkLoader.Credit, StringComparison.Ordinal) ? Numbering.Union
        : TaggedTextLinkLoader.Written(from, to, null).Contains(source) ? Numbering.Own
        : Numbering.Unknown;
}
