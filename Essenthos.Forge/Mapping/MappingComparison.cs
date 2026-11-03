using System.Diagnostics;
using System.Globalization;
using Essenthos.Core.Comparison;
using Npgsql;

namespace Essenthos.Core.Mapping;

/// <summary>
/// The links of two databases compared by key, pair by pair (DOC-0224 §15.3): <c>link</c> by the words it names,
/// with what it says (relation, settled method, confidence, source, roles) and its id apart; <c>link_claim</c> by
/// the link's words, the method and the source, with the confidence. Differences are grouped by pair and by the
/// mapping method of the settled claim (links) or of the claim (claims). Provenance notes are not compared: they
/// are evidence, and stay in the method files.
/// </summary>
internal static class MappingComparison
{
    public static async Task<KeyedComparison> Run(string before, string after, int parallel, IReadOnlySet<string>? only, ILogger logger, CancellationToken cancellationToken)
    {
        var clock = Stopwatch.StartNew();
        var (keysBefore, provBefore, pairsBefore) = await Side(before, cancellationToken);
        var (keysAfter, provAfter, pairsAfter) = await Side(after, cancellationToken);
        logger.LogInformation("Both databases' keys read in {Elapsed}", clock.Elapsed);

        var names = pairsBefore.Select(p => Name(keysBefore, p)).Union(pairsAfter.Select(p => Name(keysAfter, p)))
            .Where(p => only is null || only.Contains(p)).Order(StringComparer.Ordinal).ToList();
        var comparison = new KeyedComparison();
        await Parallel.ForEachAsync(names, new ParallelOptions { MaxDegreeOfParallelism = parallel, CancellationToken = cancellationToken }, async (pair, token) =>
        {
            var (fromText, toText) = MappingProducts.Split(pair);
            var a = await Rows(before, keysBefore, provBefore, pair, fromText, toText, token);
            var b = await Rows(after, keysAfter, provAfter, pair, fromText, toText, token);
            lock (comparison)
            {
                comparison.Add("link", a.Links, b.Links);
                comparison.Add("link_claim", a.Claims, b.Claims);
            }

            logger.LogInformation("{Pair} compared at {Elapsed}", pair, clock.Elapsed);
        });

        return comparison;
    }

    private static string Name(WordKeys keys, (int From, int To) pair) => MappingProducts.Pair(keys.Slug(pair.From), keys.Slug(pair.To));

    private static async Task<(WordKeys, Provenances, List<(int, int)>)> Side(string connection, CancellationToken cancellationToken)
    {
        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(cancellationToken);
        return (await WordKeys.Load(db, reverse: false, cancellationToken), await Provenances.Load(db, notes: false, cancellationToken),
            await PairSnapshot.Pairs(db, cancellationToken));
    }

    private static async Task<(List<KeyedRow> Links, List<KeyedRow> Claims)> Rows(
        string connection, WordKeys keys, Provenances provenances, string pair, string fromText, string toText, CancellationToken cancellationToken)
    {
        var links = new List<KeyedRow>();
        var claims = new List<KeyedRow>();
        if (keys.TextId(fromText) is not { } fromId || keys.TextId(toText) is not { } toId)
        {
            return (links, claims);
        }

        await using var db = new NpgsqlConnection(connection);
        await db.OpenAsync(cancellationToken);
        foreach (var link in await PairSnapshot.Read(db, fromId, toId, cancellationToken))
        {
            var shape = MappingText.Keys(link.From.Select(keys.Key)) + "|" + MappingText.Keys(link.To.Select(keys.Key));
            var source = provenances[link.Provenance].Source;
            var roles = string.Join(' ', link.Roles.OrderBy(r => keys.Key(r.Word)).Select(r => $"{keys.Own(r.Word)}={r.Role}@{keys.Own(r.Head)}"));
            links.Add(new KeyedRow(pair, MappingMethods.Classify(link.Method, source), shape,
                string.Join(" | ", link.Relation, link.Method, Confidence(link.Confidence), source, roles), link.Id));
            foreach (var claim in link.Claims)
            {
                var claimSource = provenances[claim.Provenance].Source;
                claims.Add(new KeyedRow(pair, MappingMethods.Classify(claim.Method, claimSource),
                    string.Join(" | ", shape, claim.Method, claimSource), Confidence(claim.Confidence)));
            }
        }

        return (links, claims);
    }

    private static string Confidence(double? value) => value?.ToString("R", CultureInfo.InvariantCulture) ?? "-";
}
