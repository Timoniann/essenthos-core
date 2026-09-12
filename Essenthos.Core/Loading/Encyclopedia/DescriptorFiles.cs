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
/// to store an inference that looks like testimony — unless a person decided the clause.
/// </param>
/// <param name="DecidedBy">
/// Who decided this clause against the verse, and when — <c>the project owner, decided 2026-09-11</c>.
/// Present only where a person and not the pass settled it; the clause is then that person's
/// judgement and says so in its credit, and carries a confidence only where that person said the
/// verse does not settle it.
/// </param>
internal sealed record DescriptorClaimRecord(
    string Relation,
    string Target,
    string Reference,
    double? Confidence,
    string? Reason,
    string? DecidedBy = null);

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
    string AskedAt)
{
    /// <summary>
    /// The file this record was read from, set by the reader rather than written in the file.
    ///
    /// It is how one pass is told from another, and nothing else in a record is. A re-ask carries
    /// the same model and the same date as the batch it corrects — both passes ran on 2026-09-09 —
    /// so a loader asking whether it has already stored this answer would compare two identical
    /// strings and keep the old one (PRB-0449). The file name is what the file set already settles
    /// supersession by, and this carries that answer down to the database.
    /// </summary>
    public string File { get; init; } = string.Empty;
}

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

                bySlug[record.Entity] = record with { File = Path.GetFileName(file) };
            }
        }

        return ([.. order.Select(slug => bySlug[slug])], files, replaced);
    }
}
