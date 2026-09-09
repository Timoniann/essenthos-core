using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// One entity's name, in the languages and cases a rendered line puts it in.
/// </summary>
/// <param name="Names">
/// Language to case to form — <c>{"ukr": {"nominative": "Левити", "genitive": "Левитів"}}</c>, the
/// same block a descriptor record carries, so a pass that produces both writes one shape.
/// </param>
/// <param name="AskedAt">
/// The date of the run, which with <paramref name="Model"/> is the whole of who said this. Both end
/// up on every row, so a pass is identifiable and removable.
/// </param>
internal sealed record NameFormRecord(
    string Entity,
    IReadOnlyDictionary<string, Dictionary<string, string>>? Names,
    string Model,
    string AskedAt);

/// <summary>
/// Where the name-form files are read from, and what one line of one has to be.
///
/// They are the output of a generation pass — one file per batch, one JSON object per line — and
/// they live with the corpus sources rather than in the repository, like every other by-product of
/// running a model over it. A checkout without them loads nothing and says so.
///
/// <para>
/// **Their own folder and their own key, beside the descriptors rather than inside them.** A
/// descriptor file says what an entity <em>is</em> and carries its subject's forms along the way; a
/// file here says only how a name declines, for an entity somebody else's clause names. Mixing them
/// would make a record with no claims in it look like a pass that failed to produce any.
/// </para>
/// </summary>
internal static class NameFormFiles
{
    public const string ConfigurationKey = "Dataset:NameFormsPath";

    /// <summary>Under the corpus sources, in this project's own folder rather than a dataset's.</summary>
    public static readonly string DefaultFolder = Path.Combine("Essenthos", "name-forms");

    public const string FilePattern = "*.jsonl";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Every record of every file under a directory, in the order the files sort and the lines run.
    ///
    /// An entity written twice is the later file's, because a re-generated batch is meant to
    /// replace the one before it. That is decided here rather than by which file a directory walk
    /// reached first.
    /// </summary>
    public static (IReadOnlyList<NameFormRecord> Records, int Files, int Replaced) Read(string directory)
    {
        var bySlug = new Dictionary<string, NameFormRecord>(StringComparer.Ordinal);
        var order = new List<string>();
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

                var record = JsonSerializer.Deserialize<NameFormRecord>(line, Shape)
                             ?? throw new InvalidDataException(
                                 $"A line of {file} is not a name form. Each line must be one JSON " +
                                 "object with entity, names, model and askedAt.");

                if (string.IsNullOrWhiteSpace(record.Entity))
                {
                    throw new InvalidDataException(
                        $"A line of {file} names no entity. Every record declines one entity's " +
                        "name, given as its slug in \"entity\".");
                }

                if (bySlug.ContainsKey(record.Entity))
                {
                    replaced++;
                }
                else
                {
                    order.Add(record.Entity);
                }

                bySlug[record.Entity] = record;
            }
        }

        return ([.. order.Select(slug => bySlug[slug])], files, replaced);
    }
}
