using Essenthos.Core.Catss;
using Essenthos.Core.Corpus;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// A Greek text against BHSA as CATSS aligns the Masoretic text with Rahlfs (<see cref="CatssLinks"/>), read from the
/// Oxford Text Archive's copy (item 1111, CC BY-NC-SA 3.0): <c>renders</c> for a line's Greek and Hebrew, <c>expands</c>
/// for a Greek plus the text prints, <c>omits</c> for a Hebrew word the Greek leaves out where the text has nothing there either.
/// Brenton's Greek takes every book of the form he prints; Swete's Old Greek of Daniel takes CATSS's Old Greek of Daniel.
/// The archive's copy has no Psalms, Proverbs, Job or Esther.
/// </summary>
internal sealed class CatssSource : IPairSource
{
    public const string Pair = "GRCBRENT-BHSA";

    /// <summary>Each pair drawn, with the CATSS books its Greek text prints (null: Brenton's, <see cref="CatssLinks.Books"/>).</summary>
    public static readonly IReadOnlyDictionary<string, Func<string, bool>?> Pairs = new Dictionary<string, Func<string, bool>?>(StringComparer.Ordinal)
    {
        [Pair] = null,
        ["SWETEOG-BHSA"] = book => book == "Dan",
    };

    /// <summary>The folder under Resources holding the archive's four files.</summary>
    public const string Folder = "CATSS";

    public string Method => MappingMethods.Stated;

    public string Name => "catss";

    public bool States(string pair) => Pairs.ContainsKey(pair);

    public bool Makes(string source) => source.StartsWith(Sources.CatssCredit, StringComparison.Ordinal);

    public IEnumerable<string> Inputs(ProductStore store, string pair) => [Folder];

    public IEnumerable<MappingRow> Rows(ProductTexts texts, string pair, Action<string> log)
    {
        var (fromText, toText) = MappingProducts.Split(pair);
        var greek = texts[fromText];
        var bhsa = texts[toText];
        var folder = Path.Combine(texts.Store.ResourcesPath, Folder);
        var files = Directory.Exists(folder) ? Directory.GetFiles(folder, "file*-1111.txt").Order(StringComparer.Ordinal).ToList() : [];
        if (files.Count == 0)
        {
            throw new FileNotFoundException($"CATSS's parallel text is not in {folder}: scripts/fetch-catss.ps1 says how to put it there");
        }

        var (drafts, tallies) = CatssLinks.Draw(CatssReader.ReadFiles(files), bhsa, greek, Pairs[pair]);
        foreach (var tally in tallies)
        {
            log(tally.ToString());
        }

        return drafts.Select(draft => new MappingRow(
            MappingText.Keys(draft.Greek.Select(id => greek.Key(id))),
            MappingText.Keys(draft.Hebrew.Select(id => bhsa.Key(id))),
            draft.Relation.ToString().ToLowerInvariant(),
            Method,
            null,
            draft.Relation switch
            {
                Database.Entities.Enums.LinkRelation.Expands => Sources.CatssPlusCredit,
                Database.Entities.Enums.LinkRelation.Omits => Sources.CatssMinusCredit,
                _ => Sources.CatssCredit,
            },
            draft.Evidence,
            null,
            null));
    }
}
