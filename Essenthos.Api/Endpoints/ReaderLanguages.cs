namespace Essenthos.Core.Endpoints;

/// <summary>
/// The two languages a reader's page is in. The text the reader chose names the people and places —
/// <em>Дух Святой</em> over the Synodal — and the interface says every other word: the relation in a
/// line, our own line under a name. A Ukrainian page over the Synodal reads <em>син Марии</em>, and
/// never <em>son of Mary</em>.
///
/// <para>
/// The client sends the first as <c>language</c>, as it always has, and the second as <c>prose</c>.
/// A client that sends no <c>prose</c> asked for one language for both, and is answered so.
/// </para>
/// </summary>
internal static class ReaderLanguages
{
    /// <summary>The language a line's own words are said in.</summary>
    public static string? Prose(string? prose, string? language) =>
        string.IsNullOrWhiteSpace(prose) ? language : prose.Trim();
}
