using System.Text.RegularExpressions;

namespace Essenthos.Core.Corpus;

/// <summary>
/// What of a relationship's note a reader is shown.
///
/// A note is written by whoever settled the row, and part of what they wrote is about the settling:
/// who wrote it (<em>agent:</em>, <em>owner, in chat 2026-09-27:</em>), which earlier ruling it
/// follows (<em>as decided for Telah and Ammihud</em>), or nothing but that (<em>the verse states
/// it, decided on the review page</em>). That part stays in the stored note and never reaches a page;
/// what is left is what the verse says, or nothing.
/// </summary>
internal static partial class ReaderNotes
{
    /// <summary>The language of every note a model or an agent wrote, which is all but the owner's.</summary>
    public const string English = "eng";

    /// <summary>The language the owner writes his notes in.</summary>
    public const string Ukrainian = "ukr";

    /// <summary>The note without who wrote it or which ruling it follows; null where nothing else is left.</summary>
    public static string? Of(string? note)
    {
        if (string.IsNullOrWhiteSpace(note))
        {
            return null;
        }

        var shown = Author().Replace(note, " ");
        shown = Settled().Replace(shown, string.Empty);
        shown = Following().Replace(shown, string.Empty);
        shown = Spaces().Replace(shown, " ").Trim().TrimStart('-', ';', ',', ':', ' ');

        if (!shown.Any(char.IsLetter))
        {
            return null;
        }

        if (!shown.EndsWith('.') && note.TrimEnd().EndsWith('.'))
        {
            shown = shown.TrimEnd(';', ',', '-', ' ') + ".";
        }

        return shown;
    }

    /// <summary>
    /// The language a note shown to a reader is written in, so a page in another language leaves it
    /// out rather than quoting it: the owner writes in Ukrainian and every other author in English.
    /// </summary>
    public static string? LanguageOf(string? shown) =>
        shown is null ? null : shown.Any(IsCyrillic) ? Ukrainian : English;

    private static bool IsCyrillic(char letter) => letter is >= 'Ѐ' and <= 'ӿ';

    /// <summary>Who wrote the note, at its start or at the start of one of its sentences.</summary>
    [GeneratedRegex(@"(?:^|(?<=[.;]\s*))\s*(?:agent|owner(?:,\s*in\s+chat\s+\d{4}-\d{2}-\d{2})?)\s*:\s*", RegexOptions.IgnoreCase)]
    private static partial Regex Author();

    /// <summary>The sentence the review page wrote for a row it confirmed, which says only that.</summary>
    [GeneratedRegex(@"\bthe verse states it,\s*decided on the review page\.?", RegexOptions.IgnoreCase)]
    private static partial Regex Settled();

    /// <summary>Which earlier ruling a row follows, to the end of its sentence.</summary>
    [GeneratedRegex(@"\s*[-;,–—]?\s*\bas decided (?:for|on|in)\b[^.]*\.?", RegexOptions.IgnoreCase)]
    private static partial Regex Following();

    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex Spaces();
}
