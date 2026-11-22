using Essenthos.Core.Catss;
using Essenthos.Core.Corpus;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Brenton's Greek against BHSA as CATSS aligns the Masoretic text with Rahlfs (<see cref="CatssLinks"/>), read from the
/// Oxford Text Archive's copy (item 1111, CC BY-NC-SA 3.0): <c>renders</c> for a line's Greek and Hebrew, <c>expands</c>
/// for a Greek plus Brenton prints, <c>omits</c> for a Hebrew word the Greek leaves out where Brenton has nothing there either.
/// The archive's copy has no Psalms, Proverbs or Job.
/// </summary>
internal sealed class CatssSource : IPairSource
{
    public const string Pair = "GRCBRENT-BHSA";

    /// <summary>The folder under Resources holding the archive's four files.</summary>
    public const string Folder = "CATSS";

    public string Method => MappingMethods.Stated;

    public string Name => "catss";

    public bool States(string pair) => pair == Pair;

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

        var (drafts, tallies) = CatssLinks.Draw(CatssReader.ReadFiles(files), bhsa, greek);
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
