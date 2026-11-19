using Essenthos.Core.Conversion;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.CrossReferences;
using Essenthos.Core.Products;

namespace Essenthos.Core.Tests;

/// <summary>The corrections files this checkout ships, for a test that reads a text the way the converter hands it its rows.</summary>
internal static class ShippedCorrections
{
    public static TextCorrections Of(string slug) => TextCorrections.Read(TestResources.Shipped("."), slug);

    public static TextCorrections Swete => Of(SweteTextSource.Slug);

    public static TextCorrections Brenton => Of(SeptuagintTextSource.Slug);

    public static IReadOnlyList<TreasuryReferences.Correction> Treasury =>
        TreasuryReferences.Corrections(new ProductStore(TestResources.Shipped(".")));
}
