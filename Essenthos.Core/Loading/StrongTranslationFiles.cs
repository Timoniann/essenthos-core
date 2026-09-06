using System.Text.Json;

namespace Essenthos.Core.Loading;

/// <summary>
/// One entry of Strong's dictionary as a translation run published it.
/// </summary>
/// <param name="Language">
/// The three-letter code the corpus tags everything with — <c>ukr</c>, not <c>uk</c>. A language
/// column holding two spellings of Ukrainian answers half of every question asked of it.
/// </param>
/// <param name="Method">
/// What produced it, in the run's own words. Only <c>model-translation</c> is loaded; a file
/// claiming anything else is a file made by something this loader has not been told about.
/// </param>
/// <param name="Uncertain">
/// The English terms the translator said it was unsure of, where it named any. It is the only
/// per-row doubt a run produces, and it is the translator's own rather than a rule's.
/// </param>
internal sealed record StrongTranslationRecord(
    string StrongNumber,
    string Language,
    string Method,
    string Model,
    string PromptVersion,
    string TranslatedAt,
    string? Definition,
    string? Derivation,
    string? KjvDefinition,
    string? DetailedDefinition,
    IReadOnlyList<string>? Uncertain);

/// <summary>
/// Where the translated lexicon files are read from, and what one line of one has to be.
///
/// They are the output of a model run — <c>scripts/lexicon.py publish</c>, one JSON object per
/// line — and they live with the corpus sources rather than in the repository, like every other
/// by-product of running a model over it. A checkout without them loads nothing and says so.
/// </summary>
internal static class StrongTranslationFiles
{
    public const string ConfigurationKey = "Dataset:LexiconTranslationsPath";

    /// <summary>Under the corpus sources, in this project's own folder rather than a dataset's.</summary>
    public static readonly string DefaultFolder = Path.Combine("Essenthos", "lexicon");

    public const string FilePattern = "*.jsonl";

    /// <summary>The one method this loader knows how to store, as the runs write it.</summary>
    public const string ModelTranslation = "model-translation";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
    };

    /// <summary>
    /// Every record of every file under a directory, in the order the files sort and the lines run.
    ///
    /// A number written twice in one language is the later file's, because a re-run of a batch is
    /// meant to replace the one before it. That is decided here rather than by which file a
    /// directory walk reached first.
    /// </summary>
    public static (IReadOnlyList<StrongTranslationRecord> Records, int Files, int Replaced) Read(
        string directory)
    {
        var byKey = new Dictionary<(string Number, string Language), StrongTranslationRecord>();
        var order = new List<(string Number, string Language)>();
        var files = 0;
        var replaced = 0;

        foreach (var file in Directory
                     .EnumerateFiles(directory, FilePattern, SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            files++;
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                var record = JsonSerializer.Deserialize<StrongTranslationRecord>(line, Shape)
                             ?? throw new InvalidDataException(
                                 $"A line of {file} is not a translated lexicon entry. Each line "
                                 + "must be one JSON object with strong_number, language, method, "
                                 + "model, prompt_version and translated_at, as "
                                 + "scripts/lexicon.py publish writes it.");

                if (string.IsNullOrWhiteSpace(record.StrongNumber)
                    || string.IsNullOrWhiteSpace(record.Language))
                {
                    throw new InvalidDataException(
                        $"A line of {file} names no Strong number or no language. Both are how the "
                        + "row is addressed, and a row that cannot be addressed cannot be replaced "
                        + "by a better one later.");
                }

                var key = (record.StrongNumber, record.Language);
                if (!byKey.TryAdd(key, record))
                {
                    byKey[key] = record;
                    replaced++;
                }
                else
                {
                    order.Add(key);
                }
            }
        }

        return ([.. order.Select(key => byKey[key])], files, replaced);
    }
}
