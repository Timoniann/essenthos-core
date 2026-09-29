using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>One reading of a passage: the model, the effort, the prompt it answered and the day it was asked.</summary>
internal sealed record PassageReadingRun(string? Model, string? Effort, string? PromptVersion, string? AskedAt);

/// <summary>The second reading of one answer: whether it holds, and the words that decide it.</summary>
internal sealed record PassageReadingCheck(bool? Holds, string? Why, string? Model, string? PromptVersion, string? AskedAt);

/// <summary>
/// One word a reading of its passage found standing for a person the dataset lists in that verse, as
/// <c>scripts/references.py publish</c> wrote it.
/// </summary>
/// <param name="Reference">The verse, as <c>2KI 25:21</c>.</param>
/// <param name="Record">The slug of the person the word stands for.</param>
/// <param name="Text">The original the word is a word of: BHSA or NESTLE1904.</param>
/// <param name="Position">The word's position in that verse of that text.</param>
/// <param name="Strong">
/// The word's Strong number when it was read. A word at the position that no longer carries it is
/// not the word that was read, and is left.
/// </param>
/// <param name="Class">What the word is: a name, the head noun of a title, or a pronoun.</param>
/// <param name="Write">
/// Whether both readings agree, the class was accepted on the held-out set, the first reading was sure
/// enough, and no annotation stood on the word when it was read. Only these are written.
/// </param>
internal sealed record PassageReadingRecord(
    string Reference,
    string Record,
    string Text,
    int Position,
    string? Surface,
    string? Strong,
    string? Kjv,
    string? Kind,
    string? Class,
    double? Confidence,
    string? Reason,
    PassageReadingRun? Reading,
    PassageReadingCheck? Check,
    bool Write);

/// <summary>
/// Where the passage readings are read from. They are a model run and live beside the other runs under
/// this project's own folder of the corpus sources; a checkout without them writes nothing and says so.
/// </summary>
internal static class PassageReadingFiles
{
    public const string ConfigurationKey = "Dataset:PassageReadingsPath";

    public static readonly string DefaultFolder = Path.Combine("Essenthos", "references");

    public const string FilePattern = "reading-*.jsonl";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<PassageReadingRecord> Read(string directory)
    {
        var records = new List<PassageReadingRecord>();
        foreach (var file in Directory
                     .EnumerateFiles(directory, FilePattern, SearchOption.TopDirectoryOnly)
                     .Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                records.Add(
                    JsonSerializer.Deserialize<PassageReadingRecord>(line, Shape)
                    ?? throw new InvalidDataException(
                        $"A line of {file} is not a passage reading. Each line must be one JSON object with "
                        + "reference, record, text, position and write; regenerate the folder with "
                        + "\"python scripts/references.py publish\"."));
            }
        }

        return records;
    }
}
