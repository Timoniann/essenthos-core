using Essenthos.Core.Records;
using Npgsql;

namespace Essenthos.Core.Build;

/// <summary>
/// What <c>build --apply</c> loads again whole, beyond the products that moved: the tables a migration of the database created
/// or changed, and a table loaded whole from a product (<c>tables/&lt;t&gt;</c>, <c>carried/&lt;t&gt;</c>) whose product moved.
/// Each table is emptied and filled again by the load a fresh build fills it with, over every text, so the applied database
/// equals a fresh build of the same store. A table that only a fresh build can fill (the texts' tables, the links), or that
/// no load is known to fill, is refused with the reason, and the caller builds <c>--into</c> a new database.
/// </summary>
internal sealed class ApplyReloads
{
    /// <summary>The loads that fill a group of tables whole.</summary>
    public bool Records { get; private set; }

    public bool Names { get; private set; }

    public bool Spellings { get; private set; }

    public bool Strong { get; private set; }

    public bool Numbers { get; private set; }

    public bool Forms { get; private set; }

    public bool Renderings { get; private set; }

    public bool Frames { get; private set; }

    public bool VerseLinks { get; private set; }

    /// <summary>The links counted per pair and the numbered words in list order, loaded from every text's files.</summary>
    public bool StoredStrong { get; private set; }

    /// <summary>Tables loaded whole from <c>tables/&lt;t&gt;</c> or <c>carried/&lt;t&gt;</c>, replaced each from its product.</summary>
    public SortedSet<string> Carried { get; } = new(StringComparer.Ordinal);

    /// <summary>Tables of the serving stage, emptied before it runs so it fills them as it fills a fresh database's.</summary>
    public SortedSet<string> Serve { get; } = new(StringComparer.Ordinal);

    /// <summary>The migrations applied to the database before anything else, in order.</summary>
    public List<string> Migrations { get; } = [];

    /// <summary>Why each group is loaded again, for the log and the report.</summary>
    public SortedSet<string> Because { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => !(Records || Names || Spellings || Strong || Numbers || Forms || Renderings || Frames || VerseLinks || StoredStrong)
                           && Carried.Count == 0 && Serve.Count == 0;

    /// <summary>The tables each whole reload empties first (the loads that fill them per text delete per text, and keep identities going).</summary>
    public static readonly IReadOnlyDictionary<string, string[]> Emptied = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        [nameof(Strong)] = ["strong_rendering", "strong_rendering_place", "strong_reach", "strong_reach_method", "strong_book", "strong_book_reach", "text_book_linked", "strong_book_words"],
        [nameof(Numbers)] = ["word_rendered_strong"],
        [nameof(Forms)] = ["text_form"],
        [nameof(Renderings)] = ["word_rendering", "word_rendering_place", "word_rendering_witness", "word_rendering_source"],
        [nameof(VerseLinks)] = ["verse_link", "verse_link_verse"],
        [nameof(StoredStrong)] = ["strong_occurrence", "text_pair_links"],
    };

    /// <summary>
    /// Tables the build writes that no line of <c>build/products</c> names: the Strong index load's two counts per book.
    /// Bookkeeping that differs between any two builds is named with no load.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string?> Undeclared = new Dictionary<string, string?>(StringComparer.Ordinal)
    {
        ["text_book_linked"] = nameof(Strong),
        ["strong_book_words"] = nameof(Strong),
        ["table_write"] = null,
        ["verification_run"] = null,
    };

    /// <summary>Word groups come from two loads: the carried syntax groups and the words' own (supplied, restored...) numbered after them.</summary>
    private static readonly string[] SharedWithTheWords = ["word_group", "word_group_word"];

    private static readonly Lazy<HashSet<string>> RecordTables = new(() =>
        [.. RecordFiles.All.SelectMany(f => f.Tables).Select(t => t.Trim())]);

    /// <summary>
    /// Adds the load that fills <paramref name="table"/> whole, from the lines of <paramref name="graph"/> that name it; returns why it
    /// cannot be, or null.
    /// </summary>
    private string? Reload(string table, ProductGraph graph, string because)
    {
        var lines = graph.Declarations.Where(d => Declares(d, table)).ToList();
        if (lines.Count == 0)
        {
            if (!Undeclared.TryGetValue(table, out var undeclared))
            {
                return $"{table}: no line of {ProductGraph.Product} names it, so no load is known to fill it whole";
            }

            if (undeclared is not null)
            {
                Set(undeclared, because);
            }

            return null;
        }

        foreach (var line in lines)
        {
            var p = line.Product;
            string? group = p switch
            {
                _ when BuildApply.TextLayer.Contains(p) => throw new Refused($"{table} is filled by the texts' load ({p})"),
                "mapping/{from}-{to}/final" => throw new Refused($"{table} is filled by the links' load ({p})"),
                "texts/{text}/index/strong" or "texts/{text}/index/occurrences" => nameof(Strong),
                "texts/{text}/numbers" => nameof(Numbers),
                "texts/{text}/strong-occurrences" or "texts/{text}/pair-links" => nameof(StoredStrong),
                "texts/{text}/index/forms" => nameof(Forms),
                "texts/{text}/index/renderings" or "texts/{text}/index/rendered" => nameof(Renderings),
                "texts/{text}/index/spellings" => nameof(Spellings),
                "texts/{text}/frame" => nameof(Frames),
                "mapping/{from}-{to}/verses" => nameof(VerseLinks),
                "names/projected/{text}" => nameof(Names),
                _ when p.StartsWith("tables/", StringComparison.Ordinal) || p.StartsWith("carried/", StringComparison.Ordinal) => "carried",
                _ when line.Maker == ProductMaker.Serve => "serve",
                _ when line.Tables.Contains("@records") || p.StartsWith("records/", StringComparison.Ordinal) => nameof(Records),
                _ => throw new Refused($"{table} is filled from {p}, which apply cannot load again whole"),
            };
            switch (group)
            {
                case "carried" when SharedWithTheWords.Contains(table):
                    throw new Refused($"{table} holds the carried syntax groups and the words' own groups numbered after them; only the texts' load fills it whole");
                case "carried" when !CarriedTables.Tables.Contains(table):
                    throw new Refused($"{table}: {p} names it, but no carried table of this Forge loads it");
                case "carried":
                    Carried.Add(table);
                    Because.Add($"{table}: {because}");
                    break;
                case "serve":
                    Serve.Add(table);
                    Because.Add($"{table}: {because}");
                    break;
                default:
                    Set(group, because);
                    break;
            }
        }

        return null;
    }

    /// <summary>The same for each table, the first refusal returned.</summary>
    public string? Reload(IEnumerable<string> tables, ProductGraph graph, string because)
    {
        foreach (var table in tables)
        {
            try
            {
                if (Reload(table, graph, because) is { } refused)
                {
                    return refused;
                }
            }
            catch (Refused refused)
            {
                return refused.Message;
            }
        }

        return null;
    }

    private static bool Declares(ProductDeclaration line, string table) =>
        line.Tables.Contains(table)
        || (line.Tables.Contains("@records") && RecordTables.Value.Contains(table))
        || (line.Tables.Contains("{table}") && CarriedTables.Tables.Contains(table));

    private void Set(string group, string because)
    {
        switch (group)
        {
            case nameof(Records): Records = true; break;
            case nameof(Names): Names = true; break;
            case nameof(Spellings): Spellings = true; break;
            case nameof(Strong): Strong = true; break;
            case nameof(Numbers): Numbers = true; break;
            case nameof(Forms): Forms = true; break;
            case nameof(Renderings): Renderings = true; break;
            case nameof(Frames): Frames = true; break;
            case nameof(VerseLinks): VerseLinks = true; break;
            case nameof(StoredStrong): StoredStrong = true; break;
            default: throw new ArgumentOutOfRangeException(nameof(group), group, null);
        }

        Because.Add($"{group}: {because}");
    }

    /// <summary>Whether the group of <see cref="Emptied"/> named is loaded again whole.</summary>
    public bool Whole(string group) => group switch
    {
        nameof(Strong) => Strong,
        nameof(Numbers) => Numbers,
        nameof(Forms) => Forms,
        nameof(Renderings) => Renderings,
        nameof(VerseLinks) => VerseLinks,
        nameof(StoredStrong) => StoredStrong,
        _ => false,
    };

    /// <summary>Empties <paramref name="tables"/> in the caller's transaction, refusing when a table outside them points at one.</summary>
    public static async Task Empty(NpgsqlConnection connection, NpgsqlTransaction transaction, IReadOnlyCollection<string> tables, CancellationToken cancellationToken)
    {
        await using (var command = new NpgsqlCommand(
                         """
                         SELECT string_agg(DISTINCT conrelid::regclass::text || ' -> ' || confrelid::regclass::text, ', ') FROM pg_constraint
                         WHERE contype = 'f' AND confrelid::regclass::text = ANY(@tables) AND NOT conrelid::regclass::text = ANY(@tables)
                         """, connection, transaction))
        {
            command.Parameters.AddWithValue("tables", tables.ToArray());
            if (await command.ExecuteScalarAsync(cancellationToken) is string pointing)
            {
                throw new InvalidOperationException($"Cannot empty {string.Join(", ", tables)} to load them again: {pointing} points at them. Build --into a new database.");
            }
        }

        await using var truncate = new NpgsqlCommand($"TRUNCATE {string.Join(", ", tables)} RESTART IDENTITY", connection, transaction) { CommandTimeout = 0 };
        await truncate.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>A table apply cannot load again whole.</summary>
    private sealed class Refused(string message) : Exception(message);
}
