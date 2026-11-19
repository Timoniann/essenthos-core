using Essenthos.Core.Strong;

namespace Essenthos.Core.Tests;

/// <summary>The Strong number lists and the dictionary's bare references as the checkout ships them (<c>Resources/Essenthos/strong</c>).</summary>
internal static class ShippedStrongNumbers
{
    public static StrongNumberLists Lists => StrongNumberLists.Of(TestResources.Shipped("."));

    public static IReadOnlyDictionary<string, (string Written, string Number)[]> References => StrongReferences.Read(TestResources.Shipped("."));

    /// <summary>Puts the shipped files into a test's store, which a producer reading the lists needs as the build's store has them.</summary>
    public static void CopyInto(string resources)
    {
        var into = Path.Combine(resources, "Essenthos", "strong");
        Directory.CreateDirectory(into);
        foreach (var file in Directory.GetFiles(TestResources.Shipped(Path.Combine("Essenthos", "strong"))))
        {
            File.Copy(file, Path.Combine(into, Path.GetFileName(file)), overwrite: true);
        }
    }
}
