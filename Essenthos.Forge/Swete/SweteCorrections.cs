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

    private static readonly IReadOnlyDictionary<string, string> Why = new Dictionary<string, string>
    {
        ["latin"] = "A Latin letter for the Greek one it looks exactly like",
        ["figure"] = "The chapter or verse number Swete prints in the margin, run into the verse's first word",
        ["fused"] = "Two words run together; Brenton's Greek and GLAUx both read them apart, and Swete prints each",
    };

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    public static readonly IReadOnlyList<SweteRestoration> All = Read();

    /// <summary>
    /// Whether the correction leaves the word the one the transcription had, with its letters put
    /// right, rather than making another word of it.
    /// </summary>
    public static bool KeepsTheWord(SweteRestoration correction) =>
        correction.Why == Why["latin"] || correction.Why == Why["figure"];

    private static IReadOnlyList<SweteRestoration> Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new InvalidOperationException($"{Resource} is not embedded in the Forge assembly.");
        var entries = JsonSerializer.Deserialize<List<Entry>>(stream, Shape) ?? [];

        return [.. entries.Select(entry =>
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
                entry.Verse[digits..]);
        })];
    }

    private sealed record Entry(string Book, string Chapter, string Verse, string Kind, string Digitised, string Printed);
}
