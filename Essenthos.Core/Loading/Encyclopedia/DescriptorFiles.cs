using System.Text.Json;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// One clause a generation pass claims about one entity, exactly as DOC-0191 writes it.
/// </summary>
/// <param name="Reference">
/// A canonical address in the corpus's own spelling — <c>NUM 10:29</c>. It must be a verse the
/// entity is actually named in; a reference nobody can follow is worse than none.
/// </param>
/// <param name="Confidence">
/// The model's own, in 0..1. Required, because the method is an inference and this corpus refuses
/// to store an inference that looks like testimony.
/// </param>
internal sealed record DescriptorClaimRecord(
    string Relation,
    string Target,
    string Reference,
    double? Confidence,
    string? Reason);

/// <summary>
/// Everything one pass says about one entity: the ordered clauses, the name forms a language needs
/// to render them, the targets it could not resolve, and who produced it.
/// </summary>
/// <param name="Names">
/// Language to case to form — <c>{"ukr": {"nominative": "Ховав", "genitive": "Ховава"}}</c>. The
/// forms are produced with the name by the same pass and never computed by a stemmer.
/// </param>
/// <param name="Unresolved">
/// Targets the corpus does not hold, as plain strings. They are not written as clauses, so this is
/// what keeps the gap countable rather than invisible.
/// </param>
/// <param name="AskedAt">
/// The date of the run, which with <paramref name="Model"/> is the whole of who said this. Both end
/// up on every row, so a pass is identifiable and removable.
/// </param>
internal sealed record DescriptorRecord(
    string Entity,
    string? Kind,
    IReadOnlyList<DescriptorClaimRecord>? Claims,
    IReadOnlyDictionary<string, Dictionary<string, string>>? Names,
    IReadOnlyList<string>? Unresolved,
    string Model,
    string AskedAt);

/// <summary>
/// Where the descriptor files are read from, and what one line of one has to be.
///
/// They are the output of a generation pass — one file per batch, one JSON object per line — and
/// they live with the corpus sources rather than in the repository, like every other by-product of
/// running a model over it. A checkout without them loads nothing and says so.
/// </summary>
internal static class DescriptorFiles
{
    public const string ConfigurationKey = "Dataset:DescriptorsPath";

    /// <summary>Under the corpus sources, in this project's own folder rather than a dataset's.</summary>
    public static readonly string DefaultFolder = Path.Combine("Essenthos", "descriptors");

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
    public static (IReadOnlyList<DescriptorRecord> Records, int Files, int Replaced) Read(string directory)
    {
        var bySlug = new Dictionary<string, DescriptorRecord>(StringComparer.Ordinal);
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

                var record = JsonSerializer.Deserialize<DescriptorRecord>(line, Shape)
                             ?? throw new InvalidDataException(
                                 $"A line of {file} is not a descriptor. Each line must be one JSON " +
                                 "object with entity, claims, model and askedAt, as DOC-0191 spells it.");

                if (string.IsNullOrWhiteSpace(record.Entity))
                {
                    throw new InvalidDataException(
                        $"A line of {file} names no entity. Every descriptor is about one entity, " +
                        "given as its slug in \"entity\".");
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
