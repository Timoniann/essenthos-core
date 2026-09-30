using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>One original word a reading found standing for the record, addressed as the text holds it.</summary>
/// <param name="Text">The witness the word is a word of: BHSA or NESTLE1904.</param>
/// <param name="Position">The word's position in its verse of that text.</param>
/// <param name="Strong">
/// The word's Strong number when it was read. A word at the position that no longer carries it is not
/// the word that was read, and is left.
/// </param>
/// <param name="Surface">The word as it then read, for a person reading the file.</param>
internal sealed record VerseReadingWord(string Text, int Position, string? Strong, string? Surface);

/// <summary>
/// One verse a dataset lists for a record and no word of ours named it in, as it was read: that the
/// verse speaks of the record, and by which words.
/// </summary>
/// <param name="Reference">The verse, as <c>2KI 25:6</c>. Canonical numbering.</param>
/// <param name="Record">The slug of the record the verse speaks of.</param>
/// <param name="Kind">
/// <see cref="ReferenceKinds.SpokenOf"/> where the verse speaks of the record without a name of its
/// own, by a title, a description or a pronoun; <see cref="ReferenceKinds.Named"/> where a name stands
/// in the verse and means the record: the name a people shares with its ancestor, or a spelling the
/// record was not held under.
/// </param>
/// <param name="How">What stands for the record: a title, a description, another name, a pronoun, the name.</param>
/// <param name="Words">The King James words of the verse that stand for the record, as it prints them.</param>
/// <param name="Named">
/// The original words annotated to the record: the head noun of a title or a description, or the
/// name. Empty where a pronoun or a verb's person is all that stands for it, and the reference is of
/// the verse alone.
/// </param>
/// <param name="Confidence">How sure the two readings were together, which the annotation carries.</param>
/// <param name="First">The first reader's sentence.</param>
/// <param name="Second">The second reader's sentence.</param>
/// <param name="Settled">
/// Where the two readings differed, what a third reading of the verse settled and why; null where they
/// agreed.
/// </param>
internal sealed record VerseReading(
    string Reference,
    string Record,
    string Kind,
    string? How,
    string? Words,
    IReadOnlyList<VerseReadingWord>? Named,
    double Confidence,
    string First,
    string Second,
    string? Settled = null);

/// <summary>
/// The verses read, in the file that says who read them, when and under what rule.
/// </summary>
/// <param name="Readers">The two models that read each verse without seeing each other's answer.</param>
/// <param name="Third">The model that read a verse a third time where the two differed.</param>
/// <param name="Asked">The day they were asked, as <c>2026-09-30</c>.</param>
internal sealed record VerseReadings(
    string DecidedBy,
    string Policy,
    IReadOnlyList<string> Readers,
    string Third,
    string Asked,
    IReadOnlyList<VerseReading> Readings)
{
    /// <summary>What the verse reference of one reading says about itself.</summary>
    public string ReferenceSource(VerseReading reading) =>
        Read(reading.Settled is not null) + ": " + (reading.Kind == ReferenceKinds.SpokenOf
            ? ReferenceKinds.SpokenOfMark
            : NamedMark);

    /// <summary>What the annotation of a word a reading found says about itself.</summary>
    public string WordSource(VerseReading reading) =>
        Read(reading.Settled is not null) + ": the word that stands for the record";

    /// <summary>Every source a row of these readings can carry, so that what a file no longer holds can be taken back.</summary>
    public IReadOnlyList<string> ReferenceSources =>
    [
        Read(false) + ": " + ReferenceKinds.SpokenOfMark, Read(false) + ": " + NamedMark,
        Read(true) + ": " + ReferenceKinds.SpokenOfMark, Read(true) + ": " + NamedMark,
    ];

    public IReadOnlyList<string> WordSources =>
        [Read(false) + ": the word that stands for the record", Read(true) + ": the word that stands for the record"];

    private const string NamedMark = "the name in the verse names the record";

    private string Read(bool settled) =>
        string.Format(
            CultureInfo.InvariantCulture,
            settled
                ? "Essenthos, read from the verse by two language models, {0} and {1}, on {2}, and by {3} a third time where the two differed"
                : "Essenthos, read from the verse by two language models, {0} and {1}, on {2}",
            Readers[0], Readers[1], Asked, Third);
}

/// <summary>
/// Where the readings of the verses are read from. They are small and they are the record of what two
/// readers said, so they travel inside the assembly with the other rulings, where they cannot be missing.
/// </summary>
internal static class VerseReadingFiles
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.VerseReadings.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static VerseReadings Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new FileNotFoundException(
                               $"The embedded resource \"{Resource}\" is not in this assembly. It is added by "
                               + "the EmbeddedResource item in Essenthos.Forge.csproj; if the file was moved or "
                               + "renamed, that item and this name have to move with it.",
                               Resource);

        return JsonSerializer.Deserialize<VerseReadings>(stream, Shape)
               ?? throw new InvalidDataException($"The embedded resource \"{Resource}\" is empty.");
    }
}
