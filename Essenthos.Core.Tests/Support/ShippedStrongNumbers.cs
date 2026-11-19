using Essenthos.Core.Mapping;
using Essenthos.Core.Strong;

namespace Essenthos.Core.Tests;

/// <summary>
/// The Strong number lists, the dictionary's bare references and the function words as the checkout ships them
/// (<c>Resources/Essenthos/strong</c>, <c>Resources/Essenthos/lexicon/function-words.tsv</c>).
/// </summary>
internal static class ShippedStrongNumbers
{
    public static StrongNumberLists Lists => StrongNumberLists.Of(TestResources.Shipped("."));

    public static IReadOnlyDictionary<string, (string Written, string Number)[]> References => StrongReferences.Read(TestResources.Shipped("."));

    /// <summary>Whether an English word is one of the shipped function words.</summary>
    public static bool EnglishFunctionWord(string word) => FunctionWords.Of(TestResources.Shipped(".")).Contains("eng", word);

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
    }
}
