using Essenthos.Core.ClearBible;
using Essenthos.Core.Loading.Links;
using Essenthos.Core.Mapping;
using Essenthos.Core.Products;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Strong number lists, the dictionary's bare references, the function words, the prefixes and the sources' lists as the
/// checkout ships them (<c>Resources/Essenthos/strong</c>, <c>lexicon/function-words.tsv</c>, <c>lexicon/prefixes.tsv</c>,
/// <c>sources</c>).
/// </summary>
internal static class ShippedStrongNumbers
{
    public static StrongNumberLists Lists => StrongNumberLists.Of(TestResources.Shipped("."));

    public static IReadOnlyDictionary<string, (string Written, string Number)[]> References => StrongReferences.Read(TestResources.Shipped("."));

    /// <summary>Whether an English word is one of the shipped function words.</summary>
    public static bool EnglishFunctionWord(string word) => FunctionWords.Of(TestResources.Shipped(".")).Contains("eng", word);

    /// <summary>A Clear Bible set as the shipped <c>sources/clear-bible.tsv</c> declares it.</summary>
    public static ClearBibleSet ClearBible(string pair) => ClearBibleSet.All(new ProductStore(TestResources.Shipped("."))).Single(set => set.Pair == pair);

    /// <summary>The shipped words and shares the Berean tables are read with.</summary>
    public static BereanLists Berean => BereanLists.Of(new ProductStore(TestResources.Shipped(".")));

    /// <summary>The shipped list of the nouns a translator adds.</summary>
    public static EnglishAddedNouns AddedNouns => EnglishAddedNouns.Of(new ProductStore(TestResources.Shipped(".")));

    /// <summary>Puts the shipped files into a test's store, which a producer reading the lists needs as the build's store has them.</summary>
    public static void CopyInto(string resources)
    {
        var into = Path.Combine(resources, "Essenthos", "strong");
        Directory.CreateDirectory(into);
        foreach (var file in Directory.GetFiles(TestResources.Shipped(Path.Combine("Essenthos", "strong"))))
        {
            File.Copy(file, Path.Combine(into, Path.GetFileName(file)), overwrite: true);
        }

        var lexicon = Path.Combine(resources, "Essenthos", "lexicon");
        Directory.CreateDirectory(lexicon);
        File.Copy(TestResources.Shipped(FunctionWords.Source), Path.Combine(lexicon, Path.GetFileName(FunctionWords.Source)), overwrite: true);
        File.Copy(TestResources.Shipped(StrongNumberLists.PrefixSource), Path.Combine(lexicon, Path.GetFileName(StrongNumberLists.PrefixSource)), overwrite: true);

        var sources = Path.Combine(resources, "Essenthos", "sources");
        Directory.CreateDirectory(sources);
        foreach (var file in Directory.GetFiles(TestResources.Shipped(Path.Combine("Essenthos", "sources"))))
        {
            File.Copy(file, Path.Combine(sources, Path.GetFileName(file)), overwrite: true);
        }
    }
}
