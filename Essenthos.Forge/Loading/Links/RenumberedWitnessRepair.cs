using System.Diagnostics;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading.Links;

/// <param name="Pairs">The numbered pairs read at the shared addresses.</param>
/// <param name="Stale">Links a witness word they name no longer bears out, before the repair.</param>
/// <param name="Unread">Pairs whose links name a numbering this cannot read, left alone.</param>
/// <param name="Verses">The translation and witness verses a changed link stands in.</param>
internal sealed record RenumberedWitnessOutcome(
    int Pairs,
    int Stale,
    int Changed,
    int Removed,
    int Added,
    int Joined,
    IReadOnlyList<string> Unread,
    IReadOnlySet<int> Verses,
    TimeSpan Elapsed)
{
    public override string ToString() =>
        Stale == 0
            ? $"The Strong-number links of {Pairs} pairs at the Hebrew's shared addresses all stand on the numbers " +
              $"the Hebrew carries now ({Elapsed})"
            : $"{Stale} Strong-number links at the Hebrew's shared addresses stood on numbers the Hebrew no longer " +
              $"carries: {Changed} redrawn in place, {Joined} joined to the link already on their new words, " +
              $"{Removed} removed, {Added} added, in {Verses.Count} verses " +
              $"({Elapsed})" + (Unread.Count == 0 ? "" : $"; left alone, their numbering unread: {string.Join(", ", Unread)}");
}

/// <summary>
/// The links every Strong numbering drew to BHSA at the addresses two of BHSA's verses share, matched
/// again where BHSA's numbers there no longer bear them out.
///
/// <para>
/// BHSA numbers the titles of Psalms 51, 52 and 60 as two verses at one address, and a load before
/// the numbers were read in written order gave each of their words its neighbour's number. The
/// old-testament-links step puts the numbers right on a loaded corpus, but the links the numberings
/// had already matched against them stayed where the old numbers put them: the Synodal's, Luther's,
/// Darby's, the Segond's, the Schlachter's, the Revised Literal's and the Chinese Union's
/// <em>Doeg</em> all named the preposition before David. Each pair is matched again at those
/// addresses (<see cref="TaggedTextLinkLoader.RematchAt"/>), and a pair with nothing stale is left
/// as it is.
/// </para>
///
/// <para>
/// <strong>The numbers are read the way the pair's load read them.</strong> A link records only the
/// credit of its numbering, so that is what decides where its numbers are: Bob Jones University's
/// edition for the Synodal, FHL's module for the Union Version, Gómez's edition for the Reina-Valera,
/// the CrossWire module named for Darby, the Segond, the Schlachter and the Revised Literal, and the
/// translation's own words for any other pair, as Luther's are. A credit naming none of these is
/// reported and left alone.
/// </para>
/// </summary>
internal sealed class RenumberedWitnessRepair(
    AppDbContext db,
    TaggedTextLinkLoader tagged,
    SynodalStrongLinkLoader synodal,
    UnionStrongLinkLoader union,
    ReinaValeraStrongLinkLoader reinaValera,
    ILogger<RenumberedWitnessRepair> logger)
{
    public async Task<RenumberedWitnessOutcome> Run(string resources, CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.StartNew();
        var witness = BhsaTextSource.Slug;
        var addresses = await SharedAddresses(witness, cancellationToken);
        if (addresses.Count == 0)
        {
            return new RenumberedWitnessOutcome(0, 0, 0, 0, 0, 0, [], new HashSet<int>(), started.Elapsed);
        }

        var pairs = (await db.Links
                .Where(l => l.Method == LinkMethod.StrongNumber && l.ToText!.Slug == witness)
                .Select(l => new { From = l.FromText!.Slug, l.Provenance!.Source })
                .Distinct()
                .ToListAsync(cancellationToken))
            .GroupBy(pair => pair.From, pair => pair.Source)
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();

        int stale = 0, changed = 0, removed = 0, added = 0, joined = 0;
        var unread = new List<string>();
        var touched = new HashSet<long>();
        foreach (var pair in pairs)
        {
            var from = pair.Key;
            var readings = pair.Select(source => Reading(resources, from, witness, source)).Distinct().ToList();
            if (readings is not [var reading] || reading.Kind == Numbering.Unknown)
            {
                unread.Add(from);
                logger.LogWarning(
                    "{From} → {Witness}: left alone, its links name numberings this cannot read — {Sources}",
                    from, witness, string.Join("; ", pair));
                continue;
            }

            var numbers = reading.Kind switch
            {
                Numbering.Synodal => await synodal.Numbers(reading.Path!, cancellationToken),
                Numbering.Union => await union.Numbers(from, reading.Path!, cancellationToken),
                Numbering.ReinaValera => await reinaValera.Numbers(reading.Path!, cancellationToken),
                Numbering.CrossWire => await union.Numbers(from, reading.Path!, reading.Credit!, cancellationToken),
                _ => null,
            };

            var (outcome, links, changedWords) = await tagged.RematchAt(
                from, witness, numbers, addresses, apply: true, cancellationToken);
            stale += links;
            changed += outcome.Changed;
            removed += outcome.Removed;
            added += outcome.Added;
            joined += outcome.Joined;
            touched.UnionWith(changedWords);
        }

        var words = touched.ToList();
        var verses = words.Count == 0
            ? new HashSet<int>()
            : (await db.Words.Where(w => words.Contains(w.Id)).Select(w => w.VerseId).Distinct()
                .ToListAsync(cancellationToken)).ToHashSet();
        var result = new RenumberedWitnessOutcome(
            pairs.Count, stale, changed, removed, added, joined, unread, verses, started.Elapsed);
        logger.LogInformation("{Outcome}", result);
        return result;
    }

    /// <summary>The canonical addresses two or more of the witness's verses stand at.</summary>
    private async Task<HashSet<(int, int, int)>> SharedAddresses(string slug, CancellationToken cancellationToken) =>
        (await db.VerseReferences
            .Where(r => r.IsPrimary && r.Verse!.Text!.Slug == slug)
            .GroupBy(r => new { r.CanonicalBook, r.CanonicalChapter, r.CanonicalVerse })
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToListAsync(cancellationToken))
        .Select(address => (address.CanonicalBook, address.CanonicalChapter, address.CanonicalVerse))
        .ToHashSet();

    private enum Numbering
    {
        Own,
        Synodal,
        Union,
        ReinaValera,
        CrossWire,
        Unknown,
    }

    /// <summary>Where a numbering is read from, by the credit its links carry.</summary>
    private sealed record NumberingReading(Numbering Kind, string? Path = null, string? Credit = null);

    private static NumberingReading Reading(string resources, string from, string to, string source)
    {
        if (source.StartsWith(SynodalStrongLinkLoader.Credit, StringComparison.Ordinal))
        {
            return new(Numbering.Synodal, ResourcePaths.File(resources, SynodalStrongLinkLoader.EditionFile));
        }

        if (source.StartsWith(UnionStrongLinkLoader.Credit, StringComparison.Ordinal))
        {
            return SwordTextSource.Texts.Values.FirstOrDefault(
                    text => text.Segmentation == SwordSegmentation.Tagged && text.Definition.Slug == from) is { } module
                ? new(Numbering.Union, System.IO.Path.Combine(resources, module.Folder))
                : new(Numbering.Unknown);
        }

        if (source.StartsWith(ReinaValeraStrongLinkLoader.Credit, StringComparison.Ordinal))
        {
            return new(Numbering.ReinaValera, System.IO.Path.Combine([resources, .. ReinaValeraStrongLinkLoader.EditionFolder]));
        }

        if (CrossWireStrongLinkLoader.All.FirstOrDefault(
                numbering => numbering.Slug == from && source.StartsWith(numbering.Credit, StringComparison.Ordinal))
            is { } crosswire)
        {
            return new(Numbering.CrossWire, CrossWireStrongLinkLoader.Folder(resources, crosswire), crosswire.Credit);
        }

        return TaggedTextLinkLoader.Written(from, to, null).Contains(source)
            ? new(Numbering.Own)
            : new(Numbering.Unknown);
    }
}
