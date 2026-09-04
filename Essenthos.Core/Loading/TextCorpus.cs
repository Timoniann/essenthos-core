using Essenthos.Core.TextusReceptus;

namespace Essenthos.Core.Loading;

/// <summary>
/// Every text the corpus loads, declared once.
///
/// A <see cref="TextDefinition"/> is what a text says about itself before any of its bytes are
/// read, so the whole list can be had without the corpus on disk — which is what makes it usable
/// as the thing checks are written against. It was written after two test classes each kept their
/// own copy of it and one of them had already fallen a text behind: the Samaritan Pentateuch was
/// loading, and the checks that every text answers to its other identifiers silently stopped
/// covering it.
///
/// <see cref="DatasetLoader"/> does not read this. It cannot: it builds each source from files and
/// hands the loader a live reader, not a declaration. So the two can still drift, and the guard
/// against that is a check that reflects over the assembly and refuses a definition this list has
/// never heard of, rather than a second list somewhere else.
/// </summary>
internal static class TextCorpus
{
    /// <summary>In the order they load, which is originals, then editions, then translations.</summary>
    public static IReadOnlyList<TextDefinition> Definitions =>
    [
        BhsaTextSource.Definition,
        NestleTextSource.Definition,
        SeptuagintTextSource.Definition(),
        TextusReceptusTextSource.Definition(Edition.Scrivener1894),
        TextusReceptusTextSource.Definition(Edition.Stephanus1550),
        ByzantineTextSource.Definition,
        SamaritanTextSource.Definition,
        BereanTextSource.Definition,
        .. Bible4uTextSource.Definitions.Values,
    ];

    /// <summary>The slugs of <see cref="Definitions"/>, which is what most checks actually want.</summary>
    public static IReadOnlyList<string> Slugs => [.. Definitions.Select(definition => definition.Slug)];
}
