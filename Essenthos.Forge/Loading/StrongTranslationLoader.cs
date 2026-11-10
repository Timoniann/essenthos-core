using Essenthos.Core.Corpus;
using System.Text.RegularExpressions;
using Essenthos.Core.Database.Entities;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading;

/// <param name="Refused">Why each rejected row was rejected, so a bad run is visible as a shape rather than a number.</param>
internal sealed record TranslationRefusals(
    int UnknownNumber,
    int UnknownMethod,
    int NothingSaid,
    int LostReference,
    int LostSense)
{
    public int Total => UnknownNumber + UnknownMethod + NothingSaid + LostReference + LostSense;

    public override string ToString() =>
        $"{UnknownNumber} for a number the dictionary does not hold, " +
        $"{UnknownMethod} produced by something this loader was not told about, " +
        $"{NothingSaid} translating no field at all, " +
        $"{LostReference} dropping a Strong reference the English carries, and " +
        $"{LostSense} whose sense numbering does not match the English";
}

/// <summary>
/// Strong's dictionary in a language other than English, as a translation run published it.
///
/// The reader of the Ukrainian text meets an English gloss the moment he asks what a word means,
/// and there is no Ukrainian Strong to load: every dictionary that circulates under his name is
/// either somebody else's lexicon keyed to his numbers or a real translation somebody owns.
/// Strong's own English is public domain, so this corpus translates it and says on every row that
/// it did.
///
/// <para>
/// **It never touches <see cref="StrongEntry"/>.** The English stays exactly where it was, and a
/// row here stands beside it. That is not tidiness: a machine's rendering of <em>of uncertain
/// affinity</em> is a new claim, and a claim written over the thing it renders cannot be checked
/// by the reader it is shown to.
/// </para>
///
/// <para>
/// **It refuses four things and counts each.** A number the dictionary does not hold has nothing to
/// stand beside. A row translating no field at all is an empty row wearing a provenance. A
/// translation that dropped a Strong reference or a sense number has lost an address rather than a
/// word, and that is the failure this whole exercise is measured on — the harness scores it at
/// 99.7% and the loader is where a run that did worse stops.
/// </para>
///
/// <para>
/// These rows are the build's <c>tables/strong_entry_translation</c> (<see cref="Build.TableProducts"/>), made from the
/// translated lexicon files under <c>Essenthos/lexicon</c>; nothing writes them into a database any other way.
/// </para>
/// </summary>
internal static partial class StrongTranslationLoader
{
    /// <summary>
    /// How every source string written here begins. It is what tells this loader's rows from any
    /// other row carrying the same method.
    /// </summary>
    public const string SourcePrefix = Sources.StrongTranslationPrefix;

    /// <summary>The date out of the run's timestamp, which is the part a reader can use.</summary>
    private const int DayLength = 10;

    /// <summary>
    /// The translations of a lexicon's files that stand against the English entries, as the build's table holds them,
    /// with what was refused and why.
    /// </summary>
    internal static List<StrongEntryTranslation> Accepted(
        IReadOnlyList<StrongTranslationRecord> records,
        IReadOnlyDictionary<string, StrongEntry> english,
        out TranslationRefusals refused)
    {
        int unknownNumber = 0, unknownMethod = 0, nothingSaid = 0, lostReference = 0, lostSense = 0;
        var rows = new List<StrongEntryTranslation>(records.Count);
        foreach (var record in records)
        {
            if (!string.Equals(record.Method, StrongTranslationFiles.ModelTranslation, StringComparison.Ordinal))
            {
                unknownMethod++;
                continue;
            }

            if (!english.TryGetValue(record.StrongNumber, out var source))
            {
                unknownNumber++;
                continue;
            }

            if (Empty(record))
            {
                nothingSaid++;
                continue;
            }

            switch (Kept(source, record))
            {
                case Skeleton.LostReference:
                    lostReference++;
                    continue;
                case Skeleton.LostSense:
                    lostSense++;
                    continue;
            }

            rows.Add(new StrongEntryTranslation
            {
                StrongNumber = record.StrongNumber,
                Language = record.Language,
                Definition = Text(record.Definition),
                Derivation = Text(record.Derivation),
                KjvDefinition = Text(record.KjvDefinition),
                DetailedDefinition = Text(record.DetailedDefinition),
                Method = LinkMethod.ModelReading,
                Confidence = null,
                Source = Source(record),
                Note = Doubt(record),
            });
        }

        refused = new TranslationRefusals(unknownNumber, unknownMethod, nothingSaid, lostReference, lostSense);
        return rows;
    }

    /// <summary>
    /// Who said it, in the words a reader gets: which model, under which prompt, and on which day.
    /// Taken from the record rather than declared here, so a second run under a different model or
    /// a revised prompt cannot be stored under the first one's name.
    /// </summary>
    private static string Source(StrongTranslationRecord record) =>
        $"{SourcePrefix} {record.Model}, prompt {record.PromptVersion}, run "
        + record.TranslatedAt[..Math.Min(DayLength, record.TranslatedAt.Length)];

    private static string? Doubt(StrongTranslationRecord record) =>
        record.Uncertain is { Count: > 0 } terms
            ? "the translator was unsure of: " + string.Join(", ", terms)
            : null;

    private static string? Text(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private static bool Empty(StrongTranslationRecord record) =>
        Text(record.Definition) is null
        && Text(record.Derivation) is null
        && Text(record.KjvDefinition) is null
        && Text(record.DetailedDefinition) is null;

    private enum Skeleton
    {
        Kept,
        LostReference,
        LostSense,
    }

    /// <summary>
    /// Whether the addresses inside the prose survived being translated.
    ///
    /// A Strong reference and a sense number are not words. <c>from G25;</c> points at another
    /// entry and <c>1a)</c> points at a sense, and a translation that dropped one reads perfectly
    /// and answers a different question. The run's own harness checks this too; it is checked again
    /// here because this is the last place before the corpus, and a run made by something other
    /// than that harness would otherwise arrive unchecked.
    /// </summary>
    private static Skeleton Kept(StrongEntry source, StrongTranslationRecord record)
    {
        foreach (var (english, translated) in new[]
                 {
                     (source.Definition, record.Definition),
                     (source.Derivation, record.Derivation),
                     (source.KjvDefinition, record.KjvDefinition),
                     (source.DetailedDefinition, record.DetailedDefinition),
                 })
        {
            if (Text(english) is not { } wanted || Text(translated) is not { } got)
            {
                continue;
            }

            if (References(wanted).Except(References(got), StringComparer.Ordinal).Any())
            {
                return Skeleton.LostReference;
            }
        }

        if (Text(source.DetailedDefinition) is { } senses
            && Text(record.DetailedDefinition) is { } rendered
            && !Senses(senses).SequenceEqual(Senses(rendered), StringComparer.Ordinal))
        {
            return Skeleton.LostSense;
        }

        return Skeleton.Kept;
    }

    private static IEnumerable<string> References(string text) =>
        StrongReference().Matches(text).Select(match => match.Value).Distinct(StringComparer.Ordinal);

    private static IEnumerable<string> Senses(string text) =>
        SenseNumber().Matches(text).Select(match => match.Groups[1].Value);

    [GeneratedRegex(@"\b[GH]\d{1,4}\b")]
    private static partial Regex StrongReference();

    [GeneratedRegex(@"^\s*(\d+[a-z]?)\)", RegexOptions.Multiline)]
    private static partial Regex SenseNumber();
}
