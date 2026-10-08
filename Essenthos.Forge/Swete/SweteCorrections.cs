using System.Reflection;
using System.Text.Json;

namespace Essenthos.Core.Swete;

/// <summary>
/// Faults of Swete's transcription a rule settles without the page, because the token carries the
/// evidence itself — written out one by one in <c>SweteCorrections.json</c> by
/// <c>scripts/swete-corrections.py</c>, so each can be read and none is made anywhere it was not.
///
/// <para>
/// Three kinds. A Latin letter standing for the Greek one it looks exactly like — <c>Aἴγυπτον</c>,
/// <c>ΚAI</c>, <c>oἱ</c> — where the word it gives is one Swete, Brenton or GLAUx prints, or is within
/// two letters of the name a witness prints at that place; after a breathing the transcription wrote
/// as an apostrophe, a Latin <c>l</c> or <c>o</c> is the capital the name opens with. The chapter's
/// Roman numeral or the verse's own letter run into its first word, <c>IXκαὶ</c> and <c>aἔσται</c>:
/// Swete prints them in the margin, and they are the verse's address. And two words run together,
/// <c>τῶνβρωμάτων</c>, where Brenton and GLAUx both read the two at that place, Swete prints each of
/// them elsewhere, and the token shows it is two — a final sigma or a mark inside it, a breathing or
/// a capital opening the second, an accent on each.
/// </para>
///
/// <para>
/// And the figures. Swete prints a verse's number in the margin, and small in the line where the verse
/// begins inside one; the transcription let hundreds of them into the text — glued to a word,
/// <c>⁶⁵εὐλογεῖτε</c> and <c>(17)ἐν</c>, between two, or standing as a token of their own — and a word
/// carrying a figure matches nothing. The figure is taken out and the word kept, where the word is one
/// Swete prints elsewhere or the figure is the verse's own number, the next one or his bracketed second
/// numbering; one standing alone goes. Where it also took a word's first letters, the word is put back
/// only from the verses on either side printing it whole at the same place — the song of Daniel 3,
/// whose every verse opens <c>εὐλογεῖτε</c>. A figure inside a word is the capital iota or the omicron
/// it looks like, or a stray mark, where the word it leaves is one Swete prints; and the lunate sigma the
/// conversion wrote as its code point is a sigma.
/// </para>
///
/// <para>
/// Nothing is restored that the transcription does not hold: a word both witnesses read and Swete
/// lacks is as often a reading of Vaticanus as a word the transcription lost, and only the page tells
/// them apart — which <see cref="SwetePage"/> reads. Misread letters stay as they are, for the reason <see cref="SweteRestorations"/> gives.
/// </para>
/// </summary>
internal static class SweteCorrections
{
    private const string Resource = "Essenthos.Core.Swete.SweteCorrections.json";

    /// <summary>What the text's row says about these corrections, on a cold load and on a warm one.</summary>
    public const string Note =
        "Letters and spaces corrected by Essenthos throughout: a Latin letter the transcription wrote for the "
        + "Greek one it looks like is written as Greek, the chapter or verse number Swete prints in the margin "
        + "is taken out of the word it was run into, and two words run together are divided where Brenton's "
        + "Greek and the GLAUx treebank both read them apart.";

    /// <summary>What the text's row says about the figures, which a later pass took out.</summary>
    public const string FiguresNote =
        "The verse numbers the transcription let into the text in figures are taken out by Essenthos and the "
        + "words they were glued to kept; where a figure also took a word's first letters, the word is written "
        + "as the verses on either side print it.";

    /// <summary>The kinds the first pass made, which a corpus restored before the figures holds.</summary>
    private static readonly HashSet<string> FirstKinds = ["latin", "figure", "fused"];

    /// <summary>The kinds that leave the word the transcription had, with its letters put right.</summary>
    private static readonly HashSet<string> SameWord = ["latin", "figure", "number", "overrun", "digit", "escape"];

    private static readonly IReadOnlyDictionary<string, string> Why = new Dictionary<string, string>
    {
        ["latin"] = "A Latin letter for the Greek one it looks exactly like",
        ["figure"] = "The chapter or verse number Swete prints in the margin, run into the verse's first word",
        ["fused"] = "Two words run together; Brenton's Greek and GLAUx both read them apart, and Swete prints each",
        ["number"] = "The verse number Swete prints in figures, let into the text",
        ["overrun"] = "The verse number Swete prints in figures, over the first letters of the word the verses beside it print",
        ["digit"] = "A figure inside a word: the letter it looks like, or a stray mark, where Swete prints the word it leaves",
        ["escape"] = "The lunate sigma, which the conversion wrote as its code point",
    };

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    private static readonly IReadOnlyList<Entry> Entries = Read();

    public static readonly IReadOnlyList<SweteRestoration> All = [.. Entries.Select(Restoration)];

    /// <summary>The corrections the first pass made, before the figures were taken out.</summary>
    public static readonly IReadOnlyList<SweteRestoration> First =
        [.. Entries.Where(entry => FirstKinds.Contains(entry.Kind)).Select(Restoration)];

    private static IReadOnlyList<Entry> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        return JsonSerializer.Deserialize<List<Entry>>(stream, Shape) ?? [];
    }

    private static SweteRestoration Restoration(Entry entry)
    {
        var digits = entry.Verse.TakeWhile(char.IsAsciiDigit).Count();
        return new SweteRestoration(
            entry.Book,
            int.Parse(entry.Chapter),
            int.Parse(entry.Verse[..digits]),
            entry.Digitised,
            entry.Printed,
            Why.TryGetValue(entry.Kind, out var why)
                ? why
                : throw new InvalidOperationException(
                    $"{Resource} names a kind of correction, \"{entry.Kind}\", that nothing here explains. " +
                    $"Give it its reason in {nameof(SweteCorrections)}.{nameof(Why)} or regenerate the list."),
            entry.Verse[digits..],
            SameWord.Contains(entry.Kind));
    }

    private sealed record Entry(string Book, string Chapter, string Verse, string Kind, string Digitised, string Printed);
}
