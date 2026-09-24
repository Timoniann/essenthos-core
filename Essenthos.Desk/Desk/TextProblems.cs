using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;

namespace Essenthos.Core.Desk;

/// <param name="Id">The ticket's id, shown as the reference it is.</param>
/// <param name="Texts">The texts it names, by identifier.</param>
internal sealed record TextProblem(string Id, string Title, string? Summary, string Status, string? Severity, IReadOnlyList<string> Texts);

/// <param name="Problem">Why the tickets could not be read, where they could not.</param>
internal sealed record TextProblemsResponse(IReadOnlyList<TextProblem> Problems, string? Problem);

/// <summary>A text as the tickets might name it.</summary>
internal sealed record NamedText(string Slug, string Name, IReadOnlyList<string> Aliases);

/// <summary>
/// The problems still open on the board that name a text, as references beside it. The board is
/// avioniq's and stays there: this only asks it for its open problems and says which text each one
/// is about, by the names tickets actually use for the texts.
/// </summary>
internal sealed class TextProblems(Avioniq avioniq, ILogger<TextProblems> logger)
{
    /// <summary>How long one reading of the board is shown before it is asked again.</summary>
    private static readonly TimeSpan Fresh = TimeSpan.FromMinutes(5);

    /// <summary>A problem in one of these is still somebody's to act on; the others are closed.</summary>
    private static readonly string[] Open = ["open", "confirmed", "in-progress", "review"];

    /// <summary>
    /// The names a ticket uses for a text beside its identifier, its own name and its aliases: the
    /// editor or the translator a text is known by. Only names that belong to one text — the
    /// Septuagint is two texts here, so the word names neither.
    /// </summary>
    private static readonly Dictionary<string, string[]> KnownAs = new(StringComparer.OrdinalIgnoreCase)
    {
        ["NESTLE1904"] = ["Nestle"],
        ["TR1894"] = ["Scrivener"],
        ["TR1550"] = ["Stephanus"],
        ["GRCBRENT"] = ["Brenton"],
        ["SWETE"] = ["Swete"],
        ["OTTLEY"] = ["Ottley", "Alexandrinus"],
        ["KJV"] = ["King James"],
        ["RUSV"] = ["Synodal"],
        ["UBIO"] = ["Ohienko", "Ogienko"],
        ["BSB"] = ["Berean"],
        ["RP2018"] = ["Robinson-Pierpont", "Byzantine Textform"],
        ["SP"] = ["Samaritan"],
        ["UKR1871"] = ["Kulish"],
        ["LUTH1912"] = ["Luther", "Lutherbibel"],
        ["ELB1905"] = ["Elberfelder"],
        ["RV1909"] = ["Reina-Valera", "Reina Valera"],
        ["TISCH"] = ["Tischendorf"],
        ["WH1881"] = ["Westcott"],
        ["TYN1534"] = ["Tyndale"],
        ["GNV1599"] = ["Geneva Bible"],
        ["ASV"] = ["American Standard"],
        ["YLT"] = ["Young's Literal"],
        ["WEB"] = ["World English"],
        ["JPS1917"] = ["JPS"],
        ["BBE"] = ["Basic English"],
    };

    /// <summary>Identifiers too short to be told from an ordinary word unless they are written in capitals.</summary>
    private const int ShortIdentifier = 3;

    private readonly SemaphoreSlim _gate = new(1, 1);

    private (DateTimeOffset At, List<JsonObject> Rows)? _read;

    public async Task<TextProblemsResponse> List(IReadOnlyList<NamedText> texts, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_read is not { } read || DateTimeOffset.UtcNow - read.At > Fresh)
            {
                var result = await avioniq.Run(
                    ["problems", "list", "--json", .. Open.SelectMany(status => new[] { "--status", status })],
                    cancellationToken);
                if (!result.Succeeded)
                {
                    logger.LogWarning("avioniq could not list the problems: {Error}", result.Error);
                    return new TextProblemsResponse([], "The board could not be read: " + FirstLine(result.Error));
                }

                _read = read = (DateTimeOffset.UtcNow, Parse(result.Output));
            }

            return new TextProblemsResponse(Match(read.Rows, texts), null);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "avioniq answered the problem list with something that is not JSON");
            return new TextProblemsResponse([], "The board answered in a form the console does not read.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Each problem that names at least one of the texts, most severe first, with the texts it names.</summary>
    public static IReadOnlyList<TextProblem> Match(IEnumerable<JsonObject> problems, IReadOnlyList<NamedText> texts)
    {
        var patterns = texts.Select(text => (text.Slug, Pattern: Pattern(text))).ToList();
        var matched = new List<TextProblem>();
        foreach (var problem in problems)
        {
            var title = Text(problem, "title") ?? string.Empty;
            var summary = Text(problem, "summary");
            var said = title + "\n" + summary;
            var named = patterns.Where(p => p.Pattern.IsMatch(said)).Select(p => p.Slug).ToList();
            if (named.Count == 0 || Text(problem, "id") is not { } id)
            {
                continue;
            }

            matched.Add(new TextProblem(id, title, summary, Text(problem, "status") ?? "open", Text(problem, "severity"), named));
        }

        return [.. matched.OrderBy(p => Rank(p.Severity)).ThenBy(p => p.Id, StringComparer.Ordinal)];
    }

    private static Regex Pattern(NamedText text)
    {
        var names = new List<string> { Name(text.Slug), Name(text.Name) };
        names.AddRange(text.Aliases.Select(Name));
        names.AddRange((KnownAs.GetValueOrDefault(text.Slug) ?? []).Select(Name));
        return new Regex(string.Join("|", names.Distinct(StringComparer.Ordinal)), RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// One name as a whole word. A short identifier — KJV, WEB, SP — matches only in capitals, where
    /// it cannot be the ordinary word; anything longer matches however it is capitalised, because
    /// the tickets written before the identifiers were capitalised spell them in lower case.
    /// </summary>
    private static string Name(string name)
    {
        var escaped = Regex.Escape(name);
        var word = $@"(?<![\p{{L}}\p{{N}}]){escaped}(?![\p{{L}}\p{{N}}])";
        return name.Length <= ShortIdentifier && name.All(char.IsLetter) ? word : $"(?i:{word})";
    }

    private static int Rank(string? severity) => severity switch
    {
        "critical" => 0,
        "high" => 1,
        "medium" => 2,
        "low" => 3,
        _ => 4,
    };

    private static List<JsonObject> Parse(string output) =>
        JsonNode.Parse(output) is JsonArray rows ? [.. rows.OfType<JsonObject>()] : throw new JsonException("Not a list of problems.");

    private static string? Text(JsonObject row, string key) =>
        row[key] is JsonValue value && value.TryGetValue<string>(out var text) && text.Length > 0 ? text : null;

    private static string FirstLine(string text) =>
        text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "no reason given.";

    public static NamedText Named(Database.Entities.Text text) => new(text.Slug, text.Name, TextAliases.Of(text.Slug));
}
