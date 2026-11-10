using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Loading;

/// <param name="From">The text the claim is about, by its slug.</param>
/// <param name="To">The text it stands in that relation to, by its slug.</param>
/// <param name="Relation">translated-from, revised-from, same-family-as or collated-against.</param>
/// <param name="Scope">Canonical book ranges, <c>1-39</c> or <c>67-79,80</c>; null for every book the text holds.</param>
/// <param name="Source">The work the claim rests on. Required: a relation nobody can check is not one.</param>
internal sealed record TextRelationEntry(
    string From,
    string To,
    string Relation,
    string? Scope,
    string? Note,
    string Source);

/// <summary>
/// What each text was translated from, revised from, compared with or shares a tradition with, as first listed in
/// <c>TextRelations.json</c>. Each claim names its texts by slug and the work it rests on, and says only what that work or
/// the text's own record establishes. The list seeds the hand file <c>texts/relations</c> and the names layer's bases where
/// either is missing; <c>text_relation</c> is the build's <c>tables/text_relation</c>, made from that hand file.
/// </summary>
internal static class TextRelationLoader
{
    private const string Resource = "Essenthos.Core.Loading.TextRelations.json";

    /// <summary>The last book of the widest canon the corpus numbers, the Ge'ez one.</summary>
    internal const int LastCanonicalBook = 92;

    private static readonly JsonSerializerOptions Shape = new() { PropertyNameCaseInsensitive = true };

    public static readonly IReadOnlyList<TextRelationEntry> All = ReadList();

    private static List<TextRelationEntry> ReadList()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
            ?? throw new InvalidOperationException(
                $"{Resource} is not embedded in the Forge assembly. It is Loading/TextRelations.json, listed as an " +
                "EmbeddedResource in Essenthos.Forge.csproj; rebuild after restoring it.");
        var list = JsonSerializer.Deserialize<TextRelationList>(stream, Shape)
            ?? throw new InvalidDataException($"{Resource} holds no list of relations.");
        return Validate(list.Relations);
    }

    /// <summary>The list as it may be written, refused whole where any claim does not read.</summary>
    internal static List<TextRelationEntry> Validate(IReadOnlyList<TextRelationEntry> entries)
    {
        var seen = new HashSet<(string, string, string, string?)>();
        foreach (var entry in entries)
        {
            var claim = $"{entry.From} {entry.Relation} {entry.To}";
            if (string.IsNullOrWhiteSpace(entry.From) || string.IsNullOrWhiteSpace(entry.To) ||
                string.Equals(entry.From, entry.To, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    $"The relation \"{claim}\" must name two different texts by slug.");
            }

            _ = EnumSpelling.ToTextRelationKind(entry.Relation);

            if (string.IsNullOrWhiteSpace(entry.Source))
            {
                throw new InvalidDataException(
                    $"The relation \"{claim}\" names no source. Name the work it rests on, or leave the claim out.");
            }

            if (entry.Scope is not null && Ranges(entry.Scope) is null)
            {
                throw new InvalidDataException(
                    $"The relation \"{claim}\" has the scope \"{entry.Scope}\", which does not read as canonical " +
                    $"book ranges between 1 and {LastCanonicalBook} separated by commas — 1-39, or 67-79,80.");
            }

            if (!seen.Add((entry.From.ToUpperInvariant(), entry.Relation, entry.To.ToUpperInvariant(), entry.Scope)))
            {
                throw new InvalidDataException($"The relation \"{claim}\" is listed twice for the same books.");
            }
        }

        return [.. entries];
    }

    /// <summary><c>1-39</c> or <c>67-79,80</c> as ranges of canonical books, or null where any piece does not read.</summary>
    internal static List<(int First, int Last)>? Ranges(string scope)
    {
        var ranges = new List<(int, int)>();
        foreach (var piece in scope.Split(','))
        {
            var ends = piece.Split('-');
            if (ends.Length is < 1 or > 2 ||
                !int.TryParse(ends[0], NumberStyles.None, CultureInfo.InvariantCulture, out var first) ||
                !int.TryParse(ends[^1], NumberStyles.None, CultureInfo.InvariantCulture, out var last) ||
                first < 1 || last < first || last > LastCanonicalBook)
            {
                return null;
            }

            ranges.Add((first, last));
        }

        return ranges;
    }

    private sealed record TextRelationList(IReadOnlyList<TextRelationEntry> Relations);
}
