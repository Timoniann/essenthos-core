using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Loading;
using Essenthos.Core.Products;

namespace Essenthos.Core.Conversion;

/// <summary>
/// A text's declaration as <c>texts/&lt;SLUG&gt;/text</c> holds it, one field a row: what the text is, its licence
/// and its provenance, as the reader's definition states them, then every source it was converted from with
/// its hash.
/// </summary>
internal static class TextDeclaration
{
    public static IEnumerable<TextField> Fields(TextDefinition definition, IReadOnlyList<ProductInput> inputs)
    {
        yield return new("slug", definition.Slug);
        yield return new("name", definition.Name);
        yield return new("name-native", definition.NameNative);
        yield return new("kind", EnumSpelling.Of(definition.Kind));
        yield return new("language", definition.Language);
        yield return new("script", definition.Script);
        yield return new("direction", EnumSpelling.Of(definition.Direction));
        yield return new("versification", EnumSpelling.Of(definition.Versification));
        yield return new("published-year", definition.PublishedYear?.ToString());
        yield return new("edition", definition.Edition);
        yield return new("edition-year", definition.EditionYear?.ToString());
        yield return new("translators", definition.Translators);
        yield return new("editors", definition.Editors);
        yield return new("textual-family", definition.TextualFamily);
        yield return new("source-url", definition.SourceUrl);
        yield return new("rights-holder", definition.RightsHolder);
        yield return new("licence", definition.Licence);
        yield return new("licence-url", definition.LicenceUrl);
        yield return new("redistribution", EnumSpelling.Of(definition.Redistribution));
        yield return new("citation", definition.Citation);
        yield return new("rights-note", definition.RightsNote);
        yield return new("about", definition.About);
        foreach (var part in definition.PartSources)
        {
            yield return new("part-source", JsonSerializer.Serialize(part));
        }

        foreach (var input in inputs)
        {
            yield return new("source", input.ToString());
        }
    }
}
