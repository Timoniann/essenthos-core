using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>One reading, with the model that made it, the prompt it answered and the day it was asked.</summary>
internal sealed record PersonReadingRecord(
    string? Model,
    string? Effort,
    string? PromptVersion,
    string? AskedAt,
    string? Note);

/// <summary>
/// The second reading of a bearer no verse establishes: whether the enumeration's item is a man of
/// this name at all, or a place, a people, or the same man another item already names.
/// </summary>
internal sealed record PersonCheckRecord(
    bool? Person,
    string? SameAs,
    string? Why,
    string? Model,
    string? PromptVersion,
    string? AskedAt);

/// <summary>
/// What the harness worked out about which held entity a bearer is. Carried so the loader's own
/// match can be compared against it rather than trusted — the rule is written twice, in Python and
/// in C#, and two implementations of one rule are only worth having if they are checked.
/// </summary>
internal sealed record PersonReachRecord(int EntityId, string? Slug, string? Name, string? Source, int Shared);

/// <summary>
/// One bearer of one shared name, as the pass in <c>scripts/persons.py</c> decided it.
/// </summary>
/// <param name="Group">The name the bearers share — the label a namesake group is made of.</param>
/// <param name="Standing">
/// What established the split: <see cref="PersonRegisterFiles.ByAVerse"/> where a verse of this
/// corpus prints the name and the reading assigns it here, <see cref="PersonRegisterFiles.ByTheLexicon"/>
/// where nothing we hold distinguishes this man from his namesake and the record rests on the
/// enumeration alone. Null on a refusal. It is the field a page needs in order to say which of the
/// two it is showing, which is the whole reason the maximal grain is affordable at all (RUL-0024).
/// </param>
/// <param name="Kept">
/// Whether the bearer becomes a record. False lines are carried on purpose: what a register is asked
/// next is why somebody is not in it, and <paramref name="Why"/> answers that for the refusals as
/// well as for the records.
/// </param>
/// <param name="References">
/// The verses that print the name and that the reading assigns to this bearer. These are the
/// evidence, and the only addresses matched against the encyclopedia's own verse lists.
/// </param>
/// <param name="OtherReferences">
/// Verses the corpus attributes to somebody of this name that do not print it — a coup narrated
/// around a byword, a genealogy under a second spelling. They are attached but never decisive
/// (PRB-0458).
/// </param>
internal sealed record PersonRegisterRecord(
    string Group,
    int Id,
    string Key,
    string Name,
    string? Description,
    string? Kind,
    bool Kept,
    string? Standing,
    string Why,
    string? Confidence,
    int? SameAs,
    int? Enumerated,
    string? Enumeration,
    IReadOnlyList<string>? StrongNumbers,
    IReadOnlyList<string>? References,
    IReadOnlyList<string>? OtherReferences,
    PersonReadingRecord? Reading,
    PersonCheckRecord? Check,
    PersonReachRecord? Reaches);

/// <summary>
/// Where the person register is read from, and what one line of one has to be.
///
/// It is the output of the pass in <c>scripts/persons.py</c> — one file per hundred bearers, one
/// JSON object per line — and it lives with the corpus sources under this project's own folder,
/// beside the descriptors, the name forms and the places, because it is a model run and not a fetch.
/// A checkout without it loads nothing and says so.
/// </summary>
internal static class PersonRegisterFiles
{
    public const string ConfigurationKey = "Dataset:PersonRegisterPath";

    public static readonly string DefaultFolder = Path.Combine("Essenthos", "persons");

    public const string FilePattern = "*.jsonl";

    /// <summary>A split some verse of this corpus makes.</summary>
    public const string ByAVerse = "verse";

    /// <summary>
    /// A split nothing we hold makes: the enumeration separates two men and no verse we have tells
    /// them apart. A record under the maximal grain, and one that may never cite a verse as its
    /// reason.
    /// </summary>
    public const string ByTheLexicon = "lexicon";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<PersonRegisterRecord> Read(string directory)
    {
        var records = new List<PersonRegisterRecord>();
        foreach (var file in Directory
                     .EnumerateFiles(directory, FilePattern, SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                records.Add(
                    JsonSerializer.Deserialize<PersonRegisterRecord>(line, Shape)
                    ?? throw new InvalidDataException(
                        $"A line of {file} is not a register entry. Each line must be one JSON "
                        + "object with at least a group, an id, a name and a kept flag; regenerate "
                        + "the folder with \"python scripts/persons.py publish\"."));
            }
        }

        return records;
    }
}
